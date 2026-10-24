using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>
/// Exports a list as a CSV file: every row that matches the page's applied filters and sort (not just the
/// visible page), read in batches so a large report never loads at once. The file opens in Excel
/// (UTF-8 with a byte-order mark keeps the Persian text intact).
/// </summary>
public sealed class ReportExportService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, IJSRuntime js, AppState appState, IPermissionChecker permissions)
{
    /// <summary>Upper limit of one export; a bigger result is cut off and the user is told.</summary>
    public const int MaxRows = 20000;
    private const int BatchSize = 300;

    /// <summary>
    /// Exports the requests returned by <paramref name="build"/> (which must already be filtered and ordered),
    /// one row per request, and hands the file to the browser.
    /// </summary>
    public Task ExportRequestsAsync(string fileBaseName, Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build, CancellationToken ct = default) =>
        ExportBatchedAsync(fileBaseName, build,
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

    /// <summary>
    /// The step-by-step report: one row for every decision taken on the matching requests (all rounds, from the
    /// permanent decision log) plus one row for each step still waiting, with who decided, on whose behalf,
    /// when, the comment, and how long the step waited.
    /// </summary>
    public Task ExportRequestStepsAsync(string fileBaseName, Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build, CancellationToken ct = default) =>
        ExportBatchedAsync(fileBaseName, build,
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

    /// <summary>Reads the query in batches, turns each batch into rows, and downloads the CSV.</summary>
    private async Task ExportBatchedAsync(
        string fileBaseName,
        Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build,
        IReadOnlyList<string> headers,
        Func<ApplicationDbContext, List<WorkflowRequest>, CancellationToken, Task<List<IReadOnlyList<string>>>> rowsFor,
        CancellationToken ct)
    {
        if (!await AllowedAsync())
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = build(db);
        var total = await query.CountAsync(ct);
        if (total == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        var take = Math.Min(total, MaxRows);
        var rows = new List<IReadOnlyList<string>>();
        for (var skip = 0; skip < take; skip += BatchSize)
        {
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

        await DownloadAsync(fileBaseName, headers, rows);
        if (total > MaxRows)
            appState.AddToast($"فقط {MaxRows:N0} درخواست اول از {total:N0} درخواست در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    /// <summary>
    /// Exports any ordered query in batches: <paramref name="rowsFor"/> turns each batch of entities into CSV rows
    /// (looking up names as needed). The query must have a stable order (end it with the Id) so batches never overlap.
    /// </summary>
    public async Task ExportTableAsync<T>(
        string fileBaseName,
        IReadOnlyList<string> headers,
        Func<ApplicationDbContext, IQueryable<T>> build,
        Func<ApplicationDbContext, List<T>, CancellationToken, Task<IReadOnlyList<IReadOnlyList<string>>>> rowsFor,
        CancellationToken ct = default)
    {
        if (!await AllowedAsync())
            return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = build(db);
        var total = await query.CountAsync(ct);
        if (total == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        var take = Math.Min(total, MaxRows);
        var rows = new List<IReadOnlyList<string>>();
        for (var skip = 0; skip < take; skip += BatchSize)
        {
            var chunk = await query.Skip(skip).Take(Math.Min(BatchSize, take - skip)).ToListAsync(ct);
            rows.AddRange(await rowsFor(db, chunk, ct));
        }

        await DownloadAsync(fileBaseName, headers, rows);
        if (total > MaxRows)
            appState.AddToast($"فقط {MaxRows:N0} ردیف اول از {total:N0} ردیف در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    /// <summary>For lists that are only reachable through a paged service call: reads page after page and maps each item to a row.</summary>
    public async Task ExportPagedAsync<T>(
        string fileBaseName,
        IReadOnlyList<string> headers,
        Func<int, int, Task<AdminQueryService.PageResult<T>>> loadPage,
        Func<T, IReadOnlyList<string>> toRow)
    {
        if (!await AllowedAsync())
            return;
        const int pageSize = 100;
        var rows = new List<IReadOnlyList<string>>();
        var total = 0;
        for (var page = 1; rows.Count < MaxRows; page++)
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

        await DownloadAsync(fileBaseName, headers, rows);
        if (total > rows.Count)
            appState.AddToast($"فقط {rows.Count:N0} ردیف اول از {total:N0} ردیف در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
    }

    /// <summary>Exports rows the page already holds in memory (the positions tree, say).</summary>
    public async Task ExportRowsAsync(string fileBaseName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (!await AllowedAsync())
            return;
        if (rows.Count == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }
        await DownloadAsync(fileBaseName, headers, rows);
    }

    /// <summary>A UTC time as a Persian date and local time in Persian digits, or an empty cell.</summary>
    public static string Time(DateTime? utc) => utc is DateTime value ? WorkflowDisplay.ToPersianDateTime(value) : "";

    private static async Task<Dictionary<Guid, string>> EmployeeNamesAsync(ApplicationDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        return await db.Employees.AsNoTracking().Where(x => distinct.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
    }

    private async Task<Dictionary<Guid, string>> HoldersAsync(List<WorkflowRequest> chunk, CancellationToken ct)
    {
        var pending = chunk.SelectMany(x => x.CurrentSteps).Where(x => x.Status == WorkflowStepStatus.Pending).Select(x => x.ApproverPositionId).Distinct().ToList();
        return pending.Count == 0 ? [] : await workflows.GetCurrentHolderNamesAsync(pending, ct);
    }

    private static IEnumerable<IReadOnlyList<string>> StepRows(WorkflowRequest request, Dictionary<Guid, string> names, Dictionary<Guid, string> positionNames, Dictionary<Guid, string> holders)
    {
        var requestId = request.Id.ToString("N")[..8];
        var type = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);
        var requester = Name(names, request.RequesterEmployeeId);
        var status = WorkflowDisplay.StatusLabel(request.Status);
        var now = DateTime.UtcNow;
        var stepNames = request.Steps.ToDictionary(x => x.Id, x => x.Name);

        // The step is "waiting" from the moment the previous step of its round was approved (or, for the first
        // round's first step, from submission). Later rounds' first steps have no stored start, so they stay blank.
        DateTime? WaitingSince(int round, int order) =>
            request.StepDecisions.Where(d => d.Round == round && d.Order < order && d.Outcome == WorkflowStepStatus.Approved).Select(d => (DateTime?)d.ActedAtUtc).Max()
            ?? (round == 1 ? request.CreatedAtUtc : null);

        var ordered = request.StepDecisions.OrderBy(d => d.Round).ThenBy(d => d.Order).ThenBy(d => d.ActedAtUtc);
        foreach (var d in ordered)
        {
            var since = WaitingSince(d.Round, d.Order);
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

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        var text = span.TotalDays >= 1 ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه"
            : $"{(int)span.TotalMinutes} دقیقه";
        return WorkflowDisplay.ToPersianDigits(text);
    }

    private static IReadOnlyList<string> RequestRow(WorkflowRequest request, Dictionary<Guid, string> names, Dictionary<Guid, string> positionNames, Dictionary<Guid, string> holders)
    {
        // The same "current step" / "held by" wording the report page shows.
        var waiting = request.Status is WorkflowRequestStatus.PendingApproval
            ? request.CurrentSteps.Where(s => s.Status == WorkflowStepStatus.Pending).ToList()
            : new List<WorkflowRequestStep>();
        var end = waiting.Count == 0 ? WorkflowRequestEndState.Of(request) : null;

        string heldBy;
        if (waiting.Count > 0)
            heldBy = string.Join("، ", waiting.Select(s => holders.TryGetValue(s.ApproverPositionId, out var h) ? h : "سمت خالی"));
        else if (end is not null && end.WithRequester)
            heldBy = $"{Name(names, request.RequesterEmployeeId)} (درخواست‌کننده)";
        else if (end is not null && end.DeciderEmployeeId is Guid decider)
            heldBy = $"{Name(names, decider)} (تصمیم‌گیرنده)";
        else
            heldBy = "—";

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

    private static string Name(Dictionary<Guid, string> names, Guid id) => names.GetValueOrDefault(id, "—");

    /// <summary>The menu is hidden without the permission, but the check is repeated here so a stale page cannot export.</summary>
    private async Task<bool> AllowedAsync()
    {
        if (await permissions.HasPermissionAsync("Reports.Export"))
            return true;
        appState.AddToast("اجازه دریافت خروجی را ندارید.", ToastLevel.Error);
        return false;
    }

    private async Task DownloadAsync(string fileBaseName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var bytes = BuildCsv(headers, rows);
        var fileName = $"{fileBaseName}-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        await js.InvokeVoidAsync("yasDownloadFile", fileName, "text/csv;charset=utf-8", bytes);
        appState.AddToast($"خروجی آماده شد ({rows.Count:N0} ردیف).", ToastLevel.Success);
    }

    /// <summary>UTF-8 with a byte-order mark so Excel reads Persian text correctly.</summary>
    public static byte[] BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        text.AppendJoin(',', headers.Select(Escape)).Append("\r\n");
        foreach (var row in rows)
            text.AppendJoin(',', row.Select(Escape)).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    private static string Escape(string value)
    {
        // A cell that starts like a formula would run in Excel; a leading apostrophe keeps it plain text.
        if (value.Length > 0 && value[0] is '=' or '+' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
