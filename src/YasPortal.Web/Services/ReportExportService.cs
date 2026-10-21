using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>
/// Exports a list as a CSV file: every row that matches the page's applied filters and sort (not just the
/// visible page), read in batches so a large report never loads at once. The file opens in Excel
/// (UTF-8 with a byte-order mark keeps the Persian text intact).
/// </summary>
public sealed class ReportExportService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, IJSRuntime js, AppState appState)
{
    /// <summary>Upper limit of one export; a bigger result is cut off and the user is told.</summary>
    public const int MaxRows = 20000;
    private const int BatchSize = 300;

    /// <summary>
    /// Exports the requests returned by <paramref name="build"/> (which must already be filtered and ordered)
    /// and hands the file to the browser.
    /// </summary>
    public async Task ExportRequestsAsync(string fileBaseName, Func<ApplicationDbContext, IQueryable<WorkflowRequest>> build, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = build(db);
        var total = await query.CountAsync(ct);
        if (total == 0)
        {
            appState.AddToast("موردی برای خروجی وجود ندارد.", ToastLevel.Info);
            return;
        }

        var take = Math.Min(total, MaxRows);
        var rows = new List<IReadOnlyList<string>>(take);
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

            var employeeIds = chunk.Select(x => x.RequesterEmployeeId).ToList();
            employeeIds.AddRange(chunk.Where(x => x.CreatedByEmployeeId is not null).Select(x => x.CreatedByEmployeeId!.Value));
            employeeIds.AddRange(chunk.SelectMany(x => x.StepDecisions).Select(x => x.ActedByEmployeeId));
            employeeIds = employeeIds.Distinct().ToList();
            var names = await db.Employees.AsNoTracking().Where(x => employeeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

            var requesterPositionIds = chunk.Select(x => x.RequesterPositionId).Distinct().ToList();
            var positionNames = await db.Positions.AsNoTracking().Where(x => requesterPositionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);

            var pendingPositionIds = chunk.SelectMany(x => x.CurrentSteps).Where(x => x.Status == WorkflowStepStatus.Pending).Select(x => x.ApproverPositionId).Distinct().ToList();
            Dictionary<Guid, string> holders = pendingPositionIds.Count == 0 ? [] : await workflows.GetCurrentHolderNamesAsync(pendingPositionIds, ct);

            foreach (var request in chunk)
                rows.Add(RequestRow(request, names, positionNames, holders));
        }

        string[] headers = ["شناسه", "نوع", "درخواست‌کننده", "سمت درخواست‌کننده", "ثبت‌شده توسط مدیر", "وضعیت", "مرحله جاری", "نزد", "تاریخ ثبت"];
        await DownloadAsync(fileBaseName, headers, rows);
        if (total > MaxRows)
            appState.AddToast($"فقط {MaxRows:N0} ردیف اول از {total:N0} ردیف در خروجی قرار گرفت؛ فیلترها را محدودتر کنید.", ToastLevel.Warning);
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
