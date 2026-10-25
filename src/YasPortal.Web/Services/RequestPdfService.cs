using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using YasPortal.Domain.Workflows;

namespace YasPortal.Web.Services;

/// <summary>
/// Renders a <see cref="RequestReportModel"/> to PDF (A4, right-to-left, Vazirmatn): header with logo and organization
/// name, the summary, the sections, the history table, the signature boxes, and a footer with page numbers.
/// Rendered on the server, so the result is the same in every browser and can be archived or e-mailed.
/// </summary>
public sealed class RequestPdfService(RequestReportBuilder builder)
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    private const string FontFamily = "Vazirmatn";
    private static readonly string Border = Colors.Grey.Lighten1;
    private static readonly string HeaderFill = Colors.Grey.Lighten3;

    /// <summary>The default report of a request (the builder plus every registered customizer), rendered.</summary>
    public async Task<byte[]> BuildAsync(WorkflowRequest request, IReadOnlyDictionary<Guid, string> employeeNames, IReadOnlyDictionary<Guid, string> holderNames, CancellationToken ct = default) =>
        Render(await builder.BuildAsync(request, employeeNames, holderNames, ct));

    /// <summary>Renders any model: build one with <see cref="RequestReportBuilder"/> and change it, or fill one from scratch.</summary>
    public static byte[] Render(RequestReportModel model)
    {
        EnsureInitialized();
        return Document.Create(document => Compose(document, model)).GeneratePdf();
    }

    /// <summary>Registers the license and the Persian fonts once per process.</summary>
    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
                return;
            QuestPDF.Settings.License = LicenseType.Community;
            // A missing glyph (an arrow, say) should print a blank, not fail the whole document.
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
            var folder = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");
            foreach (var file in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Bold.ttf" })
            {
                using var stream = File.OpenRead(Path.Combine(folder, file));
                FontManager.RegisterFont(stream);
            }
            _initialized = true;
        }
    }

    private static void Compose(IDocumentContainer document, RequestReportModel model)
    {
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
                    row.ConstantItem(46).Height(46).Element(c => RenderLogo(c, model));
                    row.ConstantItem(10);
                    row.RelativeItem().Column(titles =>
                    {
                        titles.Item().Text(model.OrganizationName).Bold().FontSize(14);
                        titles.Item().Text(model.Title).FontSize(11).FontColor(Colors.Grey.Darken2);
                    });
                    row.RelativeItem().AlignLeft().Column(ids =>
                    {
                        ids.Item().AlignLeft().Text(model.HeaderLine1).Bold().FontSize(11);
                        ids.Item().AlignLeft().Text(model.HeaderLine2);
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(4).Column(column =>
            {
                column.Spacing(10);
                if (model.Summary.Count > 0)
                    column.Item().Element(c => KeyValueGrid(c, model.Summary));
                foreach (var section in model.Sections)
                    column.Item().Element(c => RenderSection(c, section));
                if (model.Calculated.Count > 0)
                    column.Item().Element(c => RenderSection(c, new ReportSection(model.CalculatedTitle), model.Calculated));
                if (model.History.Count > 0)
                    column.Item().Element(c => RenderHistory(c, model));
                foreach (var section in model.Appendix)
                    column.Item().Element(c => RenderSection(c, section));
                if (model.Signers.Count > 0)
                    column.Item().Element(c => RenderSignatures(c, model));
            });

            page.Footer().PaddingTop(6).Column(column =>
            {
                column.Item().LineHorizontal(0.5f).LineColor(Border);
                column.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                        text.Span($"چاپ‌شده توسط {model.PrintedBy} · {WorkflowDisplay.ToPersianDateTime(model.PrintedAtUtc)}");
                    });
                    row.RelativeItem().AlignLeft().Text(text =>
                    {
                        text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                        text.Span("صفحه ");
                        text.CurrentPageNumber().Format(n => WorkflowDisplay.ToPersianDigits(n?.ToString() ?? ""));
                        text.Span(" از ");
                        text.TotalPages().Format(n => WorkflowDisplay.ToPersianDigits(n?.ToString() ?? ""));
                    });
                });
            });
        });
    }

    private static void RenderLogo(IContainer container, RequestReportModel model)
    {
        if (model.LogoImage is { Length: > 0 })
            container.Image(model.LogoImage).FitArea();
        else if (!string.IsNullOrWhiteSpace(model.LogoSvg))
            container.Svg(model.LogoSvg);
    }

    /// <summary>A titled block: its label/value boxes (or <paramref name="fields"/> when given) followed by its free lines.</summary>
    private static void RenderSection(IContainer container, ReportSection section, IReadOnlyList<ReportField>? fields = null)
    {
        var boxes = fields ?? section.Fields;
        container.Column(column =>
        {
            column.Item().Element(c => SectionTitle(c, section.Title));
            if (boxes.Count > 0)
                column.Item().PaddingTop(4).Element(c => KeyValueGrid(c, boxes));
            foreach (var line in section.Lines)
                column.Item().PaddingTop(3).Text(line);
        });
    }

    private static void RenderHistory(IContainer container, RequestReportModel model)
    {
        container.Column(section =>
        {
            section.Item().Element(h => SectionTitle(h, "سوابق تایید و تصمیم‌ها"));
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
                        h.Cell().Element(HeadCell).Text(title).Bold().FontSize(8.5f);
                });
                foreach (var row in model.History)
                {
                    table.Cell().Element(Cell).Text(row.Round);
                    table.Cell().Element(Cell).Text(row.Step);
                    table.Cell().Element(Cell).Text(row.Position);
                    table.Cell().Element(Cell).Text(row.Person);
                    table.Cell().Element(Cell).Text(row.Result);
                    table.Cell().Element(Cell).Text(row.Time);
                    table.Cell().Element(Cell).Text(row.Comment);
                }
            });
        });
    }

    private static void RenderSignatures(IContainer container, RequestReportModel model)
    {
        container.Column(section =>
        {
            section.Item().Element(h => SectionTitle(h, model.SignaturesTitle));
            section.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });
                foreach (var signer in model.Signers)
                    table.Cell().ShowEntire().Padding(3).Border(0.7f).BorderColor(Border).Padding(6).Column(box =>
                    {
                        box.Item().Text(signer.Role).Bold().FontSize(9);
                        box.Item().Text(signer.Person).FontSize(9);
                        box.Item().Text(signer.Position).FontSize(8).FontColor(Colors.Grey.Darken2);
                        var result = signer.Result + (signer.Date.Length > 0 ? " · " + signer.Date : "");
                        if (result.Length > 0)
                            box.Item().PaddingTop(2).Text(result).FontSize(8);
                        box.Item().PaddingTop(4).Height(38).BorderBottom(0.5f).BorderColor(Border).AlignBottom().Text("امضا").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                    });
                // Fill the last row so the table is complete.
                for (var i = model.Signers.Count % 3; i is > 0 and < 3; i++)
                    table.Cell().Text("");
            });
        });
    }

    private static void SectionTitle(IContainer container, string title) =>
        container.Background(HeaderFill).PaddingVertical(3).PaddingHorizontal(6).Text(title).Bold().FontSize(10);

    private static IContainer HeadCell(IContainer container) =>
        container.Background(HeaderFill).Border(0.5f).BorderColor(Border).Padding(3);

    private static IContainer Cell(IContainer container) =>
        container.Border(0.5f).BorderColor(Border).Padding(3);

    /// <summary>Label/value pairs as boxes, two per row.</summary>
    private static void KeyValueGrid(IContainer container, IReadOnlyList<ReportField> fields)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn();
                c.RelativeColumn();
            });
            foreach (var field in fields)
                table.Cell().Border(0.5f).BorderColor(Border).Padding(4).Column(cell =>
                {
                    cell.Item().Text(field.Label).FontSize(8).FontColor(Colors.Grey.Darken2);
                    cell.Item().Text(string.IsNullOrWhiteSpace(field.Value) ? "—" : field.Value).Bold();
                });
            if (fields.Count % 2 == 1)
                table.Cell().Text("");
        });
    }
}
