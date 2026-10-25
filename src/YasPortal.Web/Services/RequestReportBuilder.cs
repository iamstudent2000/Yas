using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>What a customizer gets to look at: the request, and a database context for anything extra it needs to read.</summary>
public sealed record RequestReportContext(WorkflowRequest Request, ApplicationDbContext Db, IReadOnlyDictionary<Guid, string> EmployeeNames, IReadOnlyDictionary<Guid, string> PositionNames);

/// <summary>
/// A hook that changes the report model before it is rendered: read more data from the database, compute figures,
/// add signers or sections, rename the title. Register any number in Program.cs with
/// <c>builder.Services.AddScoped&lt;IRequestReportCustomizer, MyCustomizer&gt;()</c>; they run in registration order.
/// </summary>
public interface IRequestReportCustomizer
{
    Task CustomizeAsync(RequestReportContext context, RequestReportModel model, CancellationToken ct);
}

/// <summary>Turns a request into a <see cref="RequestReportModel"/> and then runs the registered customizers on it.</summary>
public sealed class RequestReportBuilder(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AuthenticationStateProvider authentication,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IEnumerable<IRequestReportCustomizer> customizers)
{
    /// <param name="request">The request with Steps, StepDecisions, RequesterActions and FieldChanges loaded.</param>
    /// <param name="employeeNames">Names the page already resolved; anything missing is looked up here.</param>
    /// <param name="holderNames">Current holder of each position that still has a pending step.</param>
    public async Task<RequestReportModel> BuildAsync(WorkflowRequest request, IReadOnlyDictionary<Guid, string> employeeNames, IReadOnlyDictionary<Guid, string> holderNames, CancellationToken ct = default)
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
        var requesterOrganization = organizationId is Guid orgId
            ? await db.Organizations.AsNoTracking().Where(x => x.Id == orgId).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;

        var state = await authentication.GetAuthenticationStateAsync();
        var data = new Data(request, employees, positions, holderNames);
        var model = new RequestReportModel
        {
            // The organization printed in the header: the configured name, or else the requester's organization.
            OrganizationName = configuration["Report:OrganizationName"] ?? requesterOrganization ?? "یاس پورتال",
            HeaderLine1 = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType),
            HeaderLine2 = "شناسه: #" + request.Id.ToString("N")[..8],
            PrintedBy = state.User.Identity?.Name ?? "—",
            FileName = "request-" + request.Id.ToString("N")[..8],
            RequestNumber = request.Id.ToString("N")[..8],
        };
        ReadLogo(model);

        FillSummary(model, data, positions.GetValueOrDefault(request.RequesterPositionId), requesterOrganization);
        FillFormSections(model, data);
        FillPartFillers(model, data);
        FillHistory(model, data);
        FillAppendix(model, data);
        FillSigners(model, data, positions.GetValueOrDefault(request.RequesterPositionId));

        var context = new RequestReportContext(request, db, employees, positions);
        foreach (var customizer in customizers)
            await customizer.CustomizeAsync(context, model, ct);
        return model;
    }

    private sealed record Data(WorkflowRequest Request, Dictionary<Guid, string> Employees, Dictionary<Guid, string> Positions, IReadOnlyDictionary<Guid, string> Holders);

    private void ReadLogo(RequestReportModel model)
    {
        var root = environment.WebRootPath ?? "";
        foreach (var name in new[] { "report-logo.png", "report-logo.jpg" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                model.LogoImage = File.ReadAllBytes(path);
                return;
            }
        }
        var svg = Path.Combine(root, "favicon.svg");
        if (File.Exists(svg))
            model.LogoSvg = File.ReadAllText(svg);
    }

    private static void FillSummary(RequestReportModel model, Data data, string? requesterPosition, string? organization)
    {
        var request = data.Request;
        model.Summary.Add(new("درخواست‌کننده", Name(data, request.RequesterEmployeeId)));
        model.Summary.Add(new("سمت درخواست‌کننده", requesterPosition ?? "—"));
        model.Summary.Add(new("سازمان", organization ?? "—"));
        model.Summary.Add(new("وضعیت", WorkflowDisplay.StatusLabel(request.Status)));
        model.Summary.Add(new("تاریخ ثبت", WorkflowDisplay.ToPersianDateTime(request.CreatedAtUtc)));
        if (request.CreatedByEmployeeId is Guid by)
            model.Summary.Add(new("ثبت‌شده توسط مدیر", Name(data, by)));
        if (request.Status is not (WorkflowRequestStatus.PendingApproval or WorkflowRequestStatus.ReturnedToRequester))
        {
            var last = request.StepDecisions.OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
            if (last is not null)
                model.Summary.Add(new("تاریخ نتیجه", WorkflowDisplay.ToPersianDateTime(last.ActedAtUtc)));
        }
    }

    /// <summary>The form data, one section per part of the request (a request type can have several).</summary>
    private static void FillFormSections(RequestReportModel model, Data data)
    {
        var request = data.Request;
        Dictionary<string, string?> values;
        try { values = JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson) ?? []; }
        catch (JsonException) { values = []; }

        var parts = WorkflowFieldCatalog.GetParts(request.WorkflowType);
        var multi = parts.Count > 1;
        foreach (var part in parts)
        {
            var fields = WorkflowFieldCatalog.GetFields(request.WorkflowType, part.Part)
                .Select(f => new ReportField(f.Label, WorkflowDisplay.ToPersianDigits(WorkflowDisplay.FormatFieldValue(f, values.GetValueOrDefault(f.Key)))))
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToList();
            if (fields.Count == 0 && !multi)
                continue;
            foreach (var field in fields)
                model.Values[WorkflowFieldCatalog.GetFields(request.WorkflowType, part.Part).First(f => f.Label == field.Label).Key] = field.Value;
            var section = new ReportSection(multi ? $"اطلاعات درخواست — بخش {WorkflowDisplay.ToPersianDigits(part.Part.ToString())}: {part.Title}" : "اطلاعات درخواست");
            section.Fields.AddRange(fields);
            if (fields.Count == 0)
                section.Lines.Add("هنوز تکمیل نشده است.");
            model.Sections.Add(section);
        }
    }

    /// <summary>For each later part of a multi-part form, the approver who filled it: the decision on the step that carries the part.</summary>
    private static void FillPartFillers(RequestReportModel model, Data data)
    {
        var request = data.Request;
        var last = request.StepDecisions.Where(d => d.Outcome == WorkflowStepStatus.Approved).OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
        if (request.Status == WorkflowRequestStatus.Approved && last is not null)
            model.FinalDate = WorkflowDisplay.ToPersianDateTime(last.ActedAtUtc);

        foreach (var part in WorkflowFieldCatalog.GetParts(request.WorkflowType).Where(p => p.Part > 1))
        {
            var step = request.Steps.FirstOrDefault(s => s.Round == request.CurrentRound && s.FillsPart == part.Part);
            if (step is null)
                continue;
            var decision = request.StepDecisions.Where(d => d.StepId == step.Id && d.Round == request.CurrentRound && d.Outcome == WorkflowStepStatus.Approved).OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
            if (decision is null)
                continue;
            var who = Name(data, decision.ActedByEmployeeId);
            if (decision.OnBehalfOfEmployeeId is Guid onBehalf)
                who += $" (به نیابت از {Name(data, onBehalf)})";
            model.PartFillers[part.Part] = new ReportSigner(step.Name, who, data.Positions.GetValueOrDefault(step.ApproverPositionId, "—"), "", WorkflowDisplay.ToPersianDateTime(decision.ActedAtUtc));
        }
    }

    private static void FillHistory(RequestReportModel model, Data data)
    {
        var request = data.Request;
        var stepNames = request.Steps.ToDictionary(s => s.Id, s => s.Name);
        foreach (var d in request.StepDecisions.OrderBy(d => d.Round).ThenBy(d => d.Order).ThenBy(d => d.ActedAtUtc))
        {
            var who = Name(data, d.ActedByEmployeeId);
            if (d.OnBehalfOfEmployeeId is Guid onBehalf)
                who += $" (به نیابت از {Name(data, onBehalf)})";
            model.History.Add(new(Fa(d.Round), stepNames.GetValueOrDefault(d.StepId, "—"), data.Positions.GetValueOrDefault(d.ApproverPositionId, "—"), who,
                WorkflowDisplay.StepLabel(d.Outcome, isCurrent: false), WorkflowDisplay.ToPersianDateTime(d.ActedAtUtc), WorkflowDisplay.ToPersianDigits(d.Comment ?? "")));
        }
        if (request.Status == WorkflowRequestStatus.PendingApproval)
            foreach (var step in request.CurrentSteps.Where(s => s.Status == WorkflowStepStatus.Pending))
                model.History.Add(new(Fa(step.Round), step.Name, data.Positions.GetValueOrDefault(step.ApproverPositionId, "—"),
                    data.Holders.GetValueOrDefault(step.ApproverPositionId, "سمت خالی"), "در انتظار تصمیم", "—", ""));
    }

    private static void FillAppendix(RequestReportModel model, Data data)
    {
        var request = data.Request;
        if (request.FieldChanges.Count > 0)
        {
            var labels = WorkflowFieldCatalog.GetFields(request.WorkflowType).ToDictionary(f => f.Key, f => f.Label);
            var section = new ReportSection("تاریخچه تغییر فیلدها");
            foreach (var change in request.FieldChanges.OrderBy(x => x.ChangedAtUtc))
                section.Lines.Add($"{labels.GetValueOrDefault(change.FieldKey, change.FieldKey)} (دور {Fa(change.Round)}): {Dash(change.OldValue)} ← {Dash(change.NewValue)}"
                    + $" — {Name(data, change.ChangedByEmployeeId)} · {WorkflowDisplay.ToPersianDateTime(change.ChangedAtUtc)}");
            model.Appendix.Add(section);
        }

        var reopens = request.RequesterActions.Where(a => a.Kind == WorkflowRequesterActionKind.Reopened).OrderBy(a => a.AtUtc).ToList();
        if (reopens.Count > 0)
        {
            var section = new ReportSection("بازگشایی‌ها");
            foreach (var reopen in reopens)
                section.Lines.Add($"دور {Fa(reopen.Round)}: {(string.IsNullOrWhiteSpace(reopen.Reason) ? "—" : WorkflowDisplay.ToPersianDigits(reopen.Reason))}"
                    + $" — {Name(data, reopen.EmployeeId)} · {WorkflowDisplay.ToPersianDateTime(reopen.AtUtc)}");
            model.Appendix.Add(section);
        }
    }

    /// <summary>The requester first, then every approver of the current round, with their decision when there is one.</summary>
    private static void FillSigners(RequestReportModel model, Data data, string? requesterPosition)
    {
        var request = data.Request;
        model.Signers.Add(new("درخواست‌کننده", Name(data, request.RequesterEmployeeId), requesterPosition ?? "—", "ثبت درخواست", WorkflowDisplay.ToPersianDateTime(request.CreatedAtUtc)));
        foreach (var step in request.Steps.Where(s => s.Round == request.CurrentRound).OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            var decision = request.StepDecisions.Where(d => d.StepId == step.Id && d.Round == request.CurrentRound).OrderByDescending(d => d.ActedAtUtc).FirstOrDefault();
            var position = data.Positions.GetValueOrDefault(step.ApproverPositionId, "—");
            if (decision is not null)
            {
                var who = Name(data, decision.ActedByEmployeeId);
                if (decision.OnBehalfOfEmployeeId is Guid onBehalf)
                    who += $" (به نیابت از {Name(data, onBehalf)})";
                model.Signers.Add(new(step.Name, who, position, WorkflowDisplay.StepLabel(decision.Outcome, isCurrent: false), WorkflowDisplay.ToPersianDateTime(decision.ActedAtUtc)));
            }
            else
            {
                var waiting = request.Status == WorkflowRequestStatus.PendingApproval && step.Status == WorkflowStepStatus.Pending;
                model.Signers.Add(new(step.Name, waiting ? data.Holders.GetValueOrDefault(step.ApproverPositionId, "سمت خالی") : "—", position,
                    waiting ? "در انتظار تصمیم" : WorkflowDisplay.StepLabel(step.Status, isCurrent: false)));
            }
        }
    }

    private static string Name(Data data, Guid employeeId) => data.Employees.GetValueOrDefault(employeeId, "—");
    private static string Fa(int number) => WorkflowDisplay.ToPersianDigits(number.ToString());
    private static string Dash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : WorkflowDisplay.ToPersianDigits(value);
}

/// <summary>
/// The figures every report gets by default. It is also the example to copy: read what you need from
/// <see cref="RequestReportContext.Db"/> or the request, then add fields, sections or signers to the model.
/// </summary>
public sealed class DefaultRequestReportCustomizer : IRequestReportCustomizer
{
    public Task CustomizeAsync(RequestReportContext context, RequestReportModel model, CancellationToken ct)
    {
        var request = context.Request;
        var decisions = request.StepDecisions.OrderBy(d => d.ActedAtUtc).ToList();
        model.Calculated.Add(new("تعداد دورها", WorkflowDisplay.ToPersianDigits(Math.Max(1, request.CurrentRound).ToString())));
        model.Calculated.Add(new("تعداد تصمیم‌ها", WorkflowDisplay.ToPersianDigits(decisions.Count.ToString())));
        if (decisions.Count > 0)
        {
            var end = request.Status == WorkflowRequestStatus.PendingApproval ? DateTime.UtcNow : decisions[^1].ActedAtUtc;
            model.Calculated.Add(new(request.Status == WorkflowRequestStatus.PendingApproval ? "مدت از ثبت تا الان" : "مدت از ثبت تا آخرین تصمیم", Duration(end - request.CreatedAtUtc)));
        }
        return Task.CompletedTask;
    }

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        var text = span.TotalDays >= 1 ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه"
            : $"{(int)span.TotalMinutes} دقیقه";
        return WorkflowDisplay.ToPersianDigits(text);
    }
}
