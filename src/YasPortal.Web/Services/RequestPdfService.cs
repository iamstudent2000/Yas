using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>
/// Builds the printable PDF of one request: a header with the logo and organization name, the form data,
/// the approval history, a signature block, and a footer with page numbers. Rendered on the server, so the
/// result is the same in every browser and can be archived or e-mailed.
/// </summary>
public sealed class RequestPdfService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AuthenticationStateProvider authentication,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    private const string FontFamily = "Vazirmatn";
    private static readonly string Border = Colors.Grey.Lighten1;
    private static readonly string HeaderFill = Colors.Grey.Lighten3;

    /// <summary>Registers the license and the Persian fonts once per process.</summary>
    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
                return;
            QuestPDF.Settings.License = LicenseType.Community;
            // A missing glyph (an arrow, say) should print a blank, not fail the whole document.
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
            var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
            foreach (var file in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf" })
            {
                using var stream = File.OpenRead(Path.Combine(folder, file));
                FontManager.RegisterFont(stream);
            }
            _initialized = true;
        }
    }

    /// <param name="request">The request with Steps, StepDecisions, RequesterActions and FieldChanges loaded.</param>
    /// <param name="employeeNames">Names the page already resolved; anything missing is looked up here.</param>
    /// <param name="holderNames">Current holder of each position that still has a pending step.</param>
    public async Task<byte[]> BuildAsync(WorkflowRequest request, IReadOnlyDictionary<Guid, string> employeeNames, IReadOnlyDictionary<Guid, string> holderNames, CancellationToken ct = default)
    {
        EnsureInitialized();
        var data = await LoadAsync(request, employeeNames, holderNames, ct);
        var logo = ReadLogo();
        var organization = configuration["Report:OrganizationName"] ?? "یاس پورتال";
        return Document.Create(document => Compose(document, data, logo, organization)).GeneratePdf();
    }

    // ---------------------------------------------------------------- data

    private sealed record PrintData(
        WorkflowRequest Request,
        Dictionary<Guid, string> Employees,
        Dictionary<Guid, string> Positions,
        string? OrganizationName,
        string? RequesterPositionName,
        IReadOnlyDictionary<Guid, string> Holders,
        string PrintedBy);

    private async Task<PrintData> LoadAsync(WorkflowRequest request, IReadOnlyDictionary<Guid, string> employeeNames, IReadOnlyDictionary<Guid, string> holderNames, CancellationToken ct)
    {
        var employeeIds = new HashSet<Guid> { request.RequesterEmployeeId };
        if (request.CreatedByEmployeeId is Guid createdBy)
            employeeIds.Add(createdBy);
        foreach (var d in request.StepDecisions)
        {
            employeeIds.Add(d.ActedByEmployeeId);
            if (d.OnBehalfOfEmployeeId is Guid onBehalf)
                employeeIds.Add(onBehalf);
        }
        foreach (var s in request.Steps.Where(x => x.ActedByEmployeeId is not null))
            employeeIds.Add(s.ActedByEmployeeId!.Value);
        foreach (var a in request.RequesterActions)
            employeeIds.Add(a.EmployeeId);
        foreach (var c in request.FieldChanges)
            employeeIds.Add(c.ChangedByEmployeeId);

        var positionIds = request.Steps.Select(x => x.ApproverPositionId).Concat(request.StepDecisions.Select(x => x.ApproverPositionId)).Append(request.RequesterPositionId).ToHashSet();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var employees = employeeIds.Where(employeeNames.ContainsKey).ToDictionary(id => id, id => employeeNames[id]);
        var missing = employeeIds.Where(id => !employees.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var row in await db.Employees.AsNoTracking().Where(x => missing.Contains(x.Id)).Select(x => new { x.Id, x.FullName }).ToListAsync(ct))
                employees[row.Id] = row.FullName;

        var positionRows = await db.Positions.AsNoTracking().Where(x => positionIds.Contains(x.Id)).Select(x => new { x.Id, x.Name, x.OrganizationId }).ToListAsync(ct);
        var positions = positionRows.ToDictionary(x => x.Id, x => x.Name);
        var organizationId = positionRows.FirstOrDefault(x => x.Id == request.RequesterPositionId)?.OrganizationId;
        var organizationName = organizationId is Guid orgId
            ? await db.Organizations.AsNoTracking().Where(x => x.Id == orgId).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;

        var state = await authentication.GetAuthenticationStateAsync();
        return new PrintData(request, employees, positions, organizationName,
            positions.GetValueOrDefault(request.RequesterPositionId), holderNames, state.User.Identity?.Name ?? "—");
    }

    private byte[]? ReadLogo()
    {
        foreach (var name in new[] { "report-logo.png", "report-logo.jpg" })
        {
            var path = Path.Combine(environment.WebRootPath ?? "", name);
            if (File.Exists(path))
                return File.ReadAllBytes(path);
        }
        return null;
    }

    private string? ReadFallbackLogoSvg()
    {
        var path = Path.Combine(environment.WebRootPath ?? "", "favicon.svg");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    // ---------------------------------------------------------------- layout

    private void Compose(IDocumentContainer document, PrintData data, byte[]? logo, string organization)
    {
        var request = data.Request;
        var shortId = request.Id.ToString("N")[..8];
        var typeName = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(9.5f).LineHeight(1.35f));

            page.Header().PaddingBottom(8).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.ConstantItem(46).Height(46).Element(c => RenderLogo(c, logo));
                    row.ConstantItem(10);
                    row.RelativeItem().Column(titles =>
                    {
                        titles.Item().Text(organization).Bold().FontSize(14);
                        titles.Item().Text("برگه درخواست").FontSize(11).FontColor(Colors.Grey.Darken2);
                    });
                    row.RelativeItem().AlignLeft().Column(ids =>
                    {
                        ids.Item().AlignLeft().Text(typeName).Bold().FontSize(11);
                        ids.Item().AlignLeft().Text(text => { text.Span("شناسه: "); text.Span("#" + shortId); });
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(4).Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(c => RenderSummary(c, data));
                RenderFieldSections(column, data);
                column.Item().Element(c => RenderHistory(c, data));
                column.Item().Element(c => RenderSignatures(c, data));
            });

            page.Footer().PaddingTop(6).Column(column =>
            {
                column.Item().LineHorizontal(0.5f).LineColor(Border);
                column.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                        text.Span($"چاپ‌شده توسط {data.PrintedBy} · {Fa(WorkflowDisplay.ToPersianDateTime(DateTime.UtcNow))}");
                    });
                    row.RelativeItem().AlignLeft().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                        text.Span("صفحه ");
                        text.CurrentPageNumber().Format(n => Fa(n?.ToString() ?? ""));
                        text.Span(" از ");
                        text.TotalPages().Format(n => Fa(n?.ToString() ?? ""));
                    });
                });
            });
        });
    }

    private void RenderLogo(IContainer container, byte[]? logo)
    {
        if (logo is not null)
        {
            container.Image(logo).FitArea();
            return;
        }
        var svg = ReadFallbackLogoSvg();
        if (svg is not null)
            container.Svg(svg);
    }

    private static void RenderSummary(IContainer container, PrintData data)
    {
        var request = data.Request;
        var rows = new List<(string Label, string Value)>
        {
            ("درخواست‌کننده", Name(data, request.RequesterEmployeeId)),
            ("سمت درخواست‌کننده", data.RequesterPositionName ?? "—"),
            ("سازمان", data.OrganizationName ?? "—"),
            ("وضعیت", WorkflowDisplay.StatusLabel(request.Status)),
            ("تاریخ ثبت", Fa(WorkflowDisplay.ToPersianDateTime(request.CreatedAtUtc))),
        };
        if (request.CreatedByEmployeeId is Guid by)
            rows.Add(("ثبت‌شده توسط مدیر", Name(data, by)));
        if (request.Status != WorkflowRequestStatus.PendingApproval && request.Status != WorkflowRequestStatus.ReturnedToRequester)
        {
            var last = request.StepDecisions.OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
            if (last is not null)
                rows.Add(("تاریخ نتیجه", Fa(WorkflowDisplay.ToPersianDateTime(last.ActedAtUtc))));
        }

        container.Element(c => KeyValueGrid(c, rows, columns: 2));
    }

    /// <summary>The form data, one block per part of the request (a request type can have several).</summary>
    private static void RenderFieldSections(ColumnDescriptor column, PrintData data)
    {
        var request = data.Request;
        Dictionary<string, string?> values;
        try { values = JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson) ?? []; }
        catch (JsonException) { values = []; }

        var parts = WorkflowFieldCatalog.GetParts(request.WorkflowType);
        var multi = parts.Count > 1;
        foreach (var part in parts)
        {
            var rows = WorkflowFieldCatalog.GetFields(request.WorkflowType, part.Part)
                .Select(f => (f.Label, Value: WorkflowDisplay.FormatFieldValue(f, values.GetValueOrDefault(f.Key))))
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => (x.Label, Value: Fa(x.Value)))
                .ToList();
            if (rows.Count == 0 && !multi)
                continue;
            column.Item().Element(c => c.Column(section =>
            {
                section.Item().Element(h => SectionTitle(h, multi ? $"اطلاعات درخواست — بخش {Fa(part.Part.ToString())}: {part.Title}" : "اطلاعات درخواست"));
                if (rows.Count == 0)
                    section.Item().PaddingTop(4).Text("هنوز تکمیل نشده است.").FontColor(Colors.Grey.Darken1);
                else
                    section.Item().PaddingTop(4).Element(g => KeyValueGrid(g, rows, columns: 2));
            }));
        }
    }

    private static void RenderHistory(IContainer container, PrintData data)
    {
        var request = data.Request;
        var decisions = request.StepDecisions.OrderBy(d => d.Round).ThenBy(d => d.Order).ThenBy(d => d.ActedAtUtc).ToList();
        var stepNames = request.Steps.ToDictionary(s => s.Id, s => s.Name);
        var pending = request.Status == WorkflowRequestStatus.PendingApproval
            ? request.CurrentSteps.Where(s => s.Status == WorkflowStepStatus.Pending).ToList()
            : [];

        container.Column(section =>
        {
            section.Item().Element(h => SectionTitle(h, "سوابق تایید و تصمیم‌ها"));
            if (decisions.Count == 0 && pending.Count == 0)
            {
                section.Item().PaddingTop(4).Text("هنوز تصمیمی ثبت نشده است.").FontColor(Colors.Grey.Darken1);
                return;
            }

            section.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(26);
                    c.RelativeColumn(2.2f);
                    c.RelativeColumn(2f);
                    c.RelativeColumn(2.4f);
                    c.RelativeColumn(1.8f);
                    c.RelativeColumn(2.2f);
                    c.RelativeColumn(3f);
                });
                table.Header(h =>
                {
                    foreach (var title in new[] { "دور", "مرحله", "سمت", "تصمیم‌گیرنده", "نتیجه", "زمان", "توضیح" })
                        h.Cell().Element(c => HeadCell(c)).Text(title).Bold().FontSize(8.5f);
                });

                foreach (var d in decisions)
                {
                    var who = Name(data, d.ActedByEmployeeId);
                    if (d.OnBehalfOfEmployeeId is Guid onBehalf)
                        who += $" (به نیابت از {Name(data, onBehalf)})";
                    table.Cell().Element(Cell).Text(Fa(d.Round.ToString()));
                    table.Cell().Element(Cell).Text(stepNames.GetValueOrDefault(d.StepId, "—"));
                    table.Cell().Element(Cell).Text(data.Positions.GetValueOrDefault(d.ApproverPositionId, "—"));
                    table.Cell().Element(Cell).Text(who);
                    table.Cell().Element(Cell).Text(WorkflowDisplay.StepLabel(d.Outcome, isCurrent: false));
                    table.Cell().Element(Cell).Text(Fa(WorkflowDisplay.ToPersianDateTime(d.ActedAtUtc)));
                    table.Cell().Element(Cell).Text(Fa(d.Comment ?? ""));
                }
                foreach (var step in pending)
                {
                    table.Cell().Element(Cell).Text(Fa(step.Round.ToString()));
                    table.Cell().Element(Cell).Text(step.Name);
                    table.Cell().Element(Cell).Text(data.Positions.GetValueOrDefault(step.ApproverPositionId, "—"));
                    table.Cell().Element(Cell).Text(data.Holders.GetValueOrDefault(step.ApproverPositionId, "سمت خالی"));
                    table.Cell().Element(Cell).Text("در انتظار تصمیم");
                    table.Cell().Element(Cell).Text("—");
                    table.Cell().Element(Cell).Text("");
                }
            });

            var edits = request.FieldChanges.OrderBy(x => x.ChangedAtUtc).ToList();
            if (edits.Count > 0)
            {
                var labels = WorkflowFieldCatalog.GetFields(request.WorkflowType).ToDictionary(f => f.Key, f => f.Label);
                section.Item().PaddingTop(8).Element(h => SectionTitle(h, "تاریخچه تغییر فیلدها"));
                foreach (var change in edits)
                {
                    var label = labels.GetValueOrDefault(change.FieldKey, change.FieldKey);
                    section.Item().PaddingTop(3).Text(text =>
                    {
                        text.Span($"{label} (دور {Fa(change.Round.ToString())}): ").Bold();
                        text.Span($"{Dash(change.OldValue)} ← {Dash(change.NewValue)}");
                        text.Span($"  — {Name(data, change.ChangedByEmployeeId)} · {Fa(WorkflowDisplay.ToPersianDateTime(change.ChangedAtUtc))}").FontColor(Colors.Grey.Darken1);
                    });
                }
            }

            var reopens = request.RequesterActions.Where(a => a.Kind == WorkflowRequesterActionKind.Reopened).OrderBy(a => a.AtUtc).ToList();
            if (reopens.Count > 0)
            {
                section.Item().PaddingTop(8).Element(h => SectionTitle(h, "بازگشایی‌ها"));
                foreach (var reopen in reopens)
                    section.Item().PaddingTop(3).Text(text =>
                    {
                        text.Span($"دور {Fa(reopen.Round.ToString())}: ").Bold();
                        text.Span(string.IsNullOrWhiteSpace(reopen.Reason) ? "—" : Fa(reopen.Reason));
                        text.Span($"  — {Name(data, reopen.EmployeeId)} · {Fa(WorkflowDisplay.ToPersianDateTime(reopen.AtUtc))}").FontColor(Colors.Grey.Darken1);
                    });
            }
        });
    }

    /// <summary>One box per signer: the requester first, then every approver of the current round, with room to sign.</summary>
    private static void RenderSignatures(IContainer container, PrintData data)
    {
        var request = data.Request;
        var signers = new List<(string Role, string Person, string Position, string Result, string Date)>
        {
            ("درخواست‌کننده", Name(data, request.RequesterEmployeeId), data.RequesterPositionName ?? "—", "ثبت درخواست", Fa(WorkflowDisplay.ToPersianDateTime(request.CreatedAtUtc))),
        };

        foreach (var step in request.Steps.Where(s => s.Round == request.CurrentRound).OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            var decision = request.StepDecisions.Where(d => d.StepId == step.Id && d.Round == request.CurrentRound).OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
            var position = data.Positions.GetValueOrDefault(step.ApproverPositionId, "—");
            if (decision is not null)
            {
                var who = Name(data, decision.ActedByEmployeeId);
                if (decision.OnBehalfOfEmployeeId is Guid onBehalf)
                    who += $" (به نیابت از {Name(data, onBehalf)})";
                signers.Add((step.Name, who, position, WorkflowDisplay.StepLabel(decision.Outcome, isCurrent: false), Fa(WorkflowDisplay.ToPersianDateTime(decision.ActedAtUtc))));
            }
            else
            {
                var waiting = request.Status == WorkflowRequestStatus.PendingApproval && step.Status == WorkflowStepStatus.Pending;
                signers.Add((step.Name, waiting ? data.Holders.GetValueOrDefault(step.ApproverPositionId, "سمت خالی") : "—", position,
                    waiting ? "در انتظار تصمیم" : WorkflowDisplay.StepLabel(step.Status, isCurrent: false), ""));
            }
        }

        container.Column(section =>
        {
            section.Item().Element(h => SectionTitle(h, "امضاها"));
            section.Item().PaddingTop(6).Grid(grid =>
            {
                grid.Columns(12);
                grid.HorizontalSpacing(8);
                grid.VerticalSpacing(8);
                foreach (var signer in signers)
                    grid.Item(4).ShowEntire().Border(0.7f).BorderColor(Border).Padding(6).Column(box =>
                    {
                        box.Item().Text(signer.Role).Bold().FontSize(9);
                        box.Item().Text(signer.Person).FontSize(9);
                        box.Item().Text(signer.Position).FontSize(8).FontColor(Colors.Grey.Darken2);
                        box.Item().PaddingTop(2).Text(signer.Result + (signer.Date.Length > 0 ? " · " + signer.Date : "")).FontSize(8);
                        box.Item().PaddingTop(4).Height(38).BorderBottom(0.5f).BorderColor(Border).AlignBottom().Text("امضا").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                    });
            });
        });
    }

    // ---------------------------------------------------------------- small pieces

    private static void SectionTitle(IContainer container, string title) =>
        container.Background(HeaderFill).PaddingVertical(3).PaddingHorizontal(6).Text(title).Bold().FontSize(10);

    private static IContainer HeadCell(IContainer container) =>
        container.Background(HeaderFill).Border(0.5f).BorderColor(Border).Padding(3);

    private static IContainer Cell(IContainer container) =>
        container.Border(0.5f).BorderColor(Border).Padding(3);

    /// <summary>Label/value pairs laid out in a grid of boxes, <paramref name="columns"/> pairs per row.</summary>
    private static void KeyValueGrid(IContainer container, IReadOnlyList<(string Label, string Value)> rows, int columns)
    {
        container.Grid(grid =>
        {
            grid.Columns(12);
            grid.HorizontalSpacing(0);
            grid.VerticalSpacing(0);
            foreach (var (label, value) in rows)
                grid.Item(12 / columns).Border(0.5f).BorderColor(Border).Padding(4).Column(cell =>
                {
                    cell.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
                    cell.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).Bold();
                });
        });
    }

    private static string Name(PrintData data, Guid employeeId) => data.Employees.GetValueOrDefault(employeeId, "—");
    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : Fa(value);
    private static string Fa(string value) => WorkflowDisplay.ToPersianDigits(value);
}
