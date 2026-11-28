using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Auditing;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

// Report export = the "download as file" buttons on the report pages. The file is a CSV (plain table text) that opens in Excel.
// Only people who hold the export permission of that report may use it, and every export is written to the audit log.
// Exports follow the filters and sort order currently applied on the page, not just the rows visible on screen.
/// <summary>
/// Exports a list as a CSV file: every row that matches the page's applied filters and sort (not just the
/// visible page), read in batches so a large report never loads at once. The file opens in Excel
/// (UTF-8 with a byte-order mark keeps the Persian text intact).
/// </summary>
public sealed class ReportExportService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, AppState appState, IPermissionChecker permissions, AuditEventWriter audit, AuthenticationStateProvider authState, ExportFilesService files)
{
    // Rows are read from the database 300 at a time so a very large report never has to be loaded all at once.
    private const int BatchSize = 300;

    // Request list export: one row per request with type, requester, status, current step, who it is waiting with, and date.
    /// <summary>
    /// Exports the requests returned by <paramref name="build"/> (which must already be filtered and ordered),
    /// one row per request, and hands the file to the browser.
    /// </summary>
    public Task ExportRequestsAsync(string permission, string fileBaseName, Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build, CancellationToken ct = default) =>
        ExportBatchedAsync(permission, fileBaseName, build,
            ["شناسه", "نوع", "درخواست‌کننده", "سمت درخواست‌کننده", "ثبت‌شده توسط مدیر", "وضعیت", "مرحله جاری", "نزد", "تاریخ ثبت"],
            async (db, chunk, ct2) =>
            {
                var employeeIds = chunk.Select(x => x.RequesterEmployeeId).ToList();
                employeeIds.AddRange(chunk.Where(x => x.CreatedByEmployeeId is not null).Select(x => x.CreatedByEmployeeId!.Value));
                employeeIds.AddRange(chunk.SelectMany(x => x.StepDecisions).Select(x => x.ActedByEmployeeId));
                var names = await EmployeeNamesAsync(db, employeeIds, ct2);

                var requesterPositionIds = chunk.Select(x => x.RequesterPositionId).Distinct().ToList();
                var positionNames = await db.Positions.AsNoTracking().Where(x => requesterPositionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct2);

                var holders = await HoldersAsync(chunk, ct2);
                return chunk.Select(request => RequestRow(request, names, positionNames, holders)).ToList();
            }, ct);

    // Step-by-step export: one row per decision ever taken (all rounds) and one row per step still waiting. This uses the
    // permanent decision log, so history stays complete even after a request was returned and sent again.
    /// <summary>
    /// The step-by-step report: one row for every decision taken on the matching requests (all rounds, from the
    /// permanent decision log) plus one row for each step still waiting, with who decided, on whose behalf,
    /// when, the comment, and how long the step waited.
    /// </summary>
    public Task ExportRequestStepsAsync(string permission, string fileBaseName, Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build, CancellationToken ct = default) =>
        ExportBatchedAsync(permission, fileBaseName, build,
            ["شناسه درخواست", "نوع", "درخواست‌کننده", "وضعیت درخواست", "دور", "ترتیب مرحله", "نام مرحله", "سمت تاییدکننده", "نتیجه مرحله", "تصمیم‌گیرنده / دارنده سمت", "به نیابت از", "زمان تصمیم", "مدت انتظار", "توضیح"],
            async (db, chunk, ct2) =>
            {
                var employeeIds = chunk.Select(x => x.RequesterEmployeeId).ToList();
                foreach (var d in chunk.SelectMany(x => x.StepDecisions))
                {
                    employeeIds.Add(d.ActedByEmployeeId);
                    if (d.OnBehalfOfEmployeeId is Guid onBehalf)
                        employeeIds.Add(onBehalf);
                }
                var names = await EmployeeNamesAsync(db, employeeIds, ct2);

                var positionIds = chunk.SelectMany(x => x.Steps).Select(x => x.ApproverPositionId).ToList();
                positionIds.AddRange(chunk.SelectMany(x => x.StepDecisions).Select(x => x.ApproverPositionId));
                positionIds = positionIds.Distinct().ToList();
                var positionNames = await db.Positions.AsNoTracking().Where(x => positionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct2);

                var holders = await HoldersAsync(chunk, ct2);
                var rows = new List<IReadOnlyList<string>>();
                foreach (var request in chunk)
                    rows.AddRange(StepRows(request, names, positionNames, holders));
                return rows;
            }, ct);

    // Shared engine for the request exports: check permission, count, read in batches, build rows, then download.
    /// <summary>Reads the query in batches, turns each batch into rows, and downloads the CSV.</summary>
    private async Task ExportBatchedAsync(
        string permission,
        string fileBaseName,
        Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build,
        IReadOnlyList<string> headers,
        Func<ApplicationDbContext, List<WorkflowRequest>, CancellationToken, Task<List<IReadOnlyList<string>>>> rowsFor,
        CancellationToken ct)
    {
        // Stop silently when the user lacks permission (the denial is logged and a message is shown by the check itself).
        if (!await AllowedAsync(permission))
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = build(db);
        var total = await query.CountAsync(ct);
        if (total == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        var take = Math.Min(total, ExportFilesService.CsvMaxRows);
        var rows = new List<IReadOnlyList<string>>();
        for (var skip = 0; skip < take; skip += BatchSize)
        {
            // Each batch loads the requests together with their steps, decisions and changes so the rows can be built without extra queries.
            var chunk = await query
                .Include(x => x.Steps)
                .Include(x => x.RequesterActions)
                .Include(x => x.StepDecisions)
                .Include(x => x.FieldChanges)
                .AsSplitQuery()
                .Skip(skip)
                .Take(Math.Min(BatchSize, take - skip))
                .ToListAsync(ct);
            rows.AddRange(await rowsFor(db, chunk, ct));
        }

        await DownloadAsync(permission, fileBaseName, headers, rows);
        if (total > ExportFilesService.CsvMaxRows)
            appState.AddToast($"فقط {ExportFilesService.CsvMaxRows:N0} درخواست اول از {total:N0} درخواست در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    // Generic export for any list: the page supplies the query and how each record becomes a row.
    /// <summary>
    /// Exports any ordered query in batches: <paramref name="rowsFor"/> turns each batch of entities into CSV rows
    /// (looking up names as needed). The query must have a stable order (end it with the Id) so batches never overlap.
    /// </summary>
    public async Task ExportTableAsync<T>(
        string permission,
        string fileBaseName,
        IReadOnlyList<string> headers,
        Func<ApplicationDbContext, IQueryable<T>> build,
        Func<ApplicationDbContext, List<T>, CancellationToken, Task<IReadOnlyList<IReadOnlyList<string>>>> rowsFor,
        CancellationToken ct = default)
    {
        if (!await AllowedAsync(permission))
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = build(db);
        var total = await query.CountAsync(ct);
        if (total == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        var take = Math.Min(total, ExportFilesService.CsvMaxRows);
        var rows = new List<IReadOnlyList<string>>();
        for (var skip = 0; skip < take; skip += BatchSize)
        {
            var chunk = await query.Skip(skip).Take(Math.Min(BatchSize, take - skip)).ToListAsync(ct);
            rows.AddRange(await rowsFor(db, chunk, ct));
        }

        await DownloadAsync(permission, fileBaseName, headers, rows);
        if (total > ExportFilesService.CsvMaxRows)
            appState.AddToast($"فقط {ExportFilesService.CsvMaxRows:N0} ردیف اول از {total:N0} ردیف در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    // Export for lists that can only be read page by page through a service; pages are read until all rows or the limit are reached.
    /// <summary>For lists that are only reachable through a paged service call: reads page after page and maps each item to a row.</summary>
    public async Task ExportPagedAsync<T>(
        string permission,
        string fileBaseName,
        IReadOnlyList<string> headers,
        Func<int, int, Task<AdminQueryService.PageResult<T>>> loadPage,
        Func<T, IReadOnlyList<string>> toRow)
    {
        if (!await AllowedAsync(permission))
            return;
        const int pageSize = 100;
        var rows = new List<IReadOnlyList<string>>();
        var total = 0;
        for (var page = 1; rows.Count < ExportFilesService.CsvMaxRows; page++)
        {
            var result = await loadPage(page, pageSize);
            total = result.TotalCount;
            if (result.Items.Count == 0)
                break;
            rows.AddRange(result.Items.Select(toRow));
            if (rows.Count >= total)
                break;
        }

        if (rows.Count == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        await DownloadAsync(permission, fileBaseName, headers, rows);
        if (total > rows.Count)
            appState.AddToast($"فقط {rows.Count:N0} ردیف اول از {total:N0} ردیف در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    // Export for rows the page already has on screen, for example the positions tree.
    /// <summary>Exports rows the page already holds in memory (the positions tree, say).</summary>
    public async Task ExportRowsAsync(string permission, string fileBaseName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (!await AllowedAsync(permission))
            return;
        if (rows.Count == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }
        await DownloadAsync(permission, fileBaseName, headers, rows);
    }

    // Formats a stored (universal) time as a Persian date and time in Persian digits.
    /// <summary>A UTC time as a Persian date and local time in Persian digits, or an empty cell.</summary>
    public static string Time(DateTime? utc) => utc is DateTime value ? WorkflowDisplay.ToPersianDateTime(value) : "";

    private static async Task<Dictionary<Guid, string>> EmployeeNamesAsync(ApplicationDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Employees.AsNoTracking().Where(x => distinct.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    // Names of the people currently holding each waiting step's position, shown in the "with" column.
    private async Task<Dictionary<Guid, string>> HoldersAsync(List<WorkflowRequest> chunk, CancellationToken ct)
    {
        var pending = chunk.SelectMany(x => x.CurrentSteps).Where(x => x.Status == WorkflowStepStatus.Pending).Select(x => x.ApproverPositionId).Distinct().ToList();
        return pending.Count == 0 ? [] : await workflows.GetCurrentHolderNamesAsync(pending, ct);
    }

    // Builds the rows of the step report for one request: first the decisions already taken, then the steps still waiting.
    private static IEnumerable<IReadOnlyList<string>> StepRows(WorkflowRequest request, Dictionary<Guid, string> names, Dictionary<Guid, string> positionNames, Dictionary<Guid, string> holders)
    {
        var requestId = request.Id.ToString("N")[..8];
        var type = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);
        var requester = Name(names, request.RequesterEmployeeId);
        var status = WorkflowDisplay.StatusLabel(request.Status);
        var now = DateTime.UtcNow;
        var stepNames = request.Steps.ToDictionary(x => x.Id, x => x.Name);

        // A step counts as waiting from the moment the previous step of its round was approved, or from submission for the first round.
        // The step is "waiting" from the moment the previous step of its round was approved (or, for the first
        // round's first step, from submission). Later rounds' first steps have no stored start, so they stay blank.
        DateTime? WaitingSince(int round, int order) =>
            request.StepDecisions.Where(d => d.Round == round && d.Order < order && d.Outcome == WorkflowStepStatus.Approved).Select(d => (DateTime?)d.ActedAtUtc).Max()
            ?? (round == 1 ? request.CreatedAtUtc : null);

        var ordered = request.StepDecisions.OrderBy(d => d.Round).ThenBy(d => d.Order).ThenBy(d => d.ActedAtUtc);
        foreach (var d in ordered)
        {
            var since = WaitingSince(d.Round, d.Order);
            // One row per decision: who decided, on whose behalf (delegation), when, the comment and how long the step had waited.
            yield return
            [
                requestId, type, requester, status,
                WorkflowDisplay.ToPersianDigits(d.Round.ToString()), WorkflowDisplay.ToPersianDigits(d.Order.ToString()),
                stepNames.GetValueOrDefault(d.StepId, "—"),
                positionNames.GetValueOrDefault(d.ApproverPositionId, "—"),
                WorkflowDisplay.StepLabel(d.Outcome, isCurrent: false),
                Name(names, d.ActedByEmployeeId),
                d.OnBehalfOfEmployeeId is Guid onBehalf ? Name(names, onBehalf) : "",
                WorkflowDisplay.ToPersianDateTime(d.ActedAtUtc),
                since is DateTime from ? Duration(d.ActedAtUtc - from) : "",
                d.Comment ?? "",
            ];
        }

        // Steps still waiting for a decision get a row with the current holder and the waiting time so far.
        foreach (var step in request.CurrentSteps.Where(x => x.Status == WorkflowStepStatus.Pending && request.Status == WorkflowRequestStatus.PendingApproval))
        {
            var since = WaitingSince(step.Round, step.Order);
            yield return
            [
                requestId, type, requester, status,
                WorkflowDisplay.ToPersianDigits(step.Round.ToString()), WorkflowDisplay.ToPersianDigits(step.Order.ToString()),
                step.Name,
                positionNames.GetValueOrDefault(step.ApproverPositionId, "—"),
                "در انتظار تصمیم",
                holders.GetValueOrDefault(step.ApproverPositionId, "سمت خالی"),
                "", "",
                since is DateTime from ? Duration(now - from) + " (تا الان)" : "",
                "",
            ];
        }
    }

    // Turns a length of time into friendly Persian text (days and hours, hours and minutes, or minutes).
    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        var text = span.TotalDays >= 1 ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه"
            : $"{(int)span.TotalMinutes} دقیقه";
        return WorkflowDisplay.ToPersianDigits(text);
    }

    // Builds the row of the request list export, using the same wording as the "Report of requests" page.
    private static IReadOnlyList<string> RequestRow(WorkflowRequest request, Dictionary<Guid, string> names, Dictionary<Guid, string> positionNames, Dictionary<Guid, string> holders)
    {
        // The same "current step" / "held by" wording the report page shows.
        var waiting = request.Status is WorkflowRequestStatus.PendingApproval
            ? request.CurrentSteps.Where(s => s.Status == WorkflowStepStatus.Pending).ToList()
            : new List<WorkflowRequestStep>();
        var end = waiting.Count == 0 ? WorkflowRequestEndState.Of(request) : null;

        // Who the request is with: the position holders if it is still moving, the requester if it was returned, or the decider.
        string heldBy;
        if (waiting.Count > 0)
            heldBy = string.Join("، ", waiting.Select(s => holders.TryGetValue(s.ApproverPositionId, out var h) ? h : "سمت خالی"));
        else if (end is not null && end.WithRequester)
            heldBy = $"{Name(names, request.RequesterEmployeeId)} (درخواست‌کننده)";
        else if (end is not null && end.DeciderEmployeeId is Guid decider)
            heldBy = $"{Name(names, decider)} (تصمیم‌گیرنده)";
        else
            heldBy = "—";

        // Column order here must match the header list given in ExportRequestsAsync.
        return
        [
            request.Id.ToString("N")[..8],
            WorkflowFieldCatalog.GetDisplayName(request.WorkflowType),
            Name(names, request.RequesterEmployeeId),
            positionNames.GetValueOrDefault(request.RequesterPositionId, "—"),
            request.CreatedByEmployeeId is Guid by ? Name(names, by) : "",
            WorkflowDisplay.StatusLabel(request.Status),
            waiting.Count > 0 ? string.Join(" / ", waiting.Select(s => s.Name)) : end?.StageLabel ?? "—",
            heldBy,
            WorkflowDisplay.ToPersianDate(request.CreatedAtUtc),
        ];
    }

    // Shows a dash when a person's name cannot be found.
    private static string Name(Dictionary<Guid, string> names, Guid id) => names.GetValueOrDefault(id, "—");

    // The permission is checked again here because a page that was opened earlier could still show an export button.
    /// <summary>The menu is hidden without the permission, but the check is repeated here so a stale page cannot export.</summary>
    private async Task<bool> AllowedAsync(string permission)
    {
        if (await permissions.HasPermissionAsync(permission))
            return true;
        await audit.WriteAsync(AuditActions.AccessDenied, AuditEntityTypes.Report, null, new { Permission = permission, Reason = "ExportNotAllowed" });
        appState.AddToast("اجازه دریافت خروجی را ندارید.", ToastLevel.Error);
        return false;
    }

    // Creates the file in the browser (a CSV, or a PDF of the same rows when «خروجی PDF» was chosen), records the export in the audit log with the filters used,
    // the format and the row count, and shows a toast.
    private async Task DownloadAsync(string permission, string fileBaseName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        // the PDF heading is the Persian name of the permission; ExportFilesService decides between CSV and PDF
        var written = await files.SaveAsync(TitleOf(permission, fileBaseName), fileBaseName, headers, rows);
        // Recorded once the file has been handed to the browser; the row count is what was really exported (cut at ExportFilesService.CsvMaxRows, or at the PDF limit).
        // The person comes from the signed-in claims, not the ambient audit actor, which a Blazor circuit may not have.
        var user = (await authState.GetAuthenticationStateAsync()).User;
        Guid? employeeId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var employee) ? employee : null;
        Guid? positionId = Guid.TryParse(user.FindFirstValue(AuthClaimNames.ActivePositionId), out var position) ? position : null;
        // The filters the list had when the button was pressed (set by the filter bar), so the event says what was exported.
        var filters = audit.PendingExportFilters;
        audit.PendingExportFilters = null;
        await audit.WriteAsync(AuditActions.ReportExported, AuditEntityTypes.Report, fileBaseName,
            AuditEventWriter.Describe(("Permission", permission), filters, ("RowCount", written), ("Format", files.WantsPdf ? "PDF" : "CSV")),
            employeeId: employeeId, activePositionId: positionId);
        appState.AddToast($"خروجی آماده شد ({written:N0} ردیف).", ToastLevel.Success);
    }

    // The heading of a PDF: the Persian name of the export permission without the word «خروجی» (for example «فهرست کاربران»); the file name when there is none.
    private static string TitleOf(string permission, string fileBaseName)
    {
        var name = ReportPermissions.All.FirstOrDefault(p => p.Code == permission).Name;
        return string.IsNullOrEmpty(name) ? fileBaseName : name.StartsWith("خروجی ", StringComparison.Ordinal) ? name["خروجی ".Length..] : name;
    }

    // Creates the CSV with a byte-order mark so Excel shows Persian letters correctly.
    /// <summary>UTF-8 with a byte-order mark so Excel reads Persian text correctly.</summary>
    public static byte[] BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        text.AppendJoin(',', headers.Select(Escape)).Append("\r\n");
        foreach (var row in rows)
            text.AppendJoin(',', row.Select(Escape)).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    // Makes one cell safe for CSV: quotes cells that contain commas or line breaks and neutralizes cells that look like formulas.
    private static string Escape(string value)
    {
        // A cell that starts like a formula would run in Excel; a leading apostrophe keeps it plain text.
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
