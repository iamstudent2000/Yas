using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

// Background job (runs inside the web application, no one clicks anything) that archives old audit history automatically.
// Audit history = the permanent record of who did what and when; archiving moves old rows out of the main table.
/// <summary>
/// Checks every hour whether the automatic archive is due. The administrator decides on the audit page whether it is on and how
/// old a row must be (see <see cref="AuditArchiveSetting"/>); when on, it runs about once a day. The first check is a couple of
/// minutes after start-up. A failure is logged and tried again at the next check.
/// </summary>
public sealed class AuditArchiveWorker(IServiceScopeFactory scopes, ILogger<AuditArchiveWorker> logger) : BackgroundService
{
    // Waits two minutes after start-up, then checks once an hour. It stops quietly when the application shuts down.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            do
            {
                await RunIfDueAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    // One check: reads the administrator's setting, does nothing if automatic archiving is off or not yet due, otherwise
    // archives everything older than the chosen number of months, remembers the run, and writes an audit entry about it.
    // A fresh set of services is created for each check because this job lives as long as the whole application.
    private async Task RunIfDueAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<AuditArchiveSettingsService>();
            var setting = await settings.GetAsync(ct);
            var now = DateTime.UtcNow;
            if (!setting.IsAutomaticRunDue(now))
                return;

            var cutoff = now.AddMonths(-setting.AfterMonths);
            var count = await scope.ServiceProvider.GetRequiredService<AuditArchiver>().ArchiveOlderThanAsync(cutoff, ct);
            await settings.RecordAutomaticRunAsync(now, count, ct);
            if (count > 0)
                await scope.ServiceProvider.GetRequiredService<AuditEventWriter>().WriteAsync(
                    AuditActions.AuditArchived, AuditEntityTypes.AuditRecord, null,
                    new { Count = count, CutoffAtUtc = cutoff, Automatic = true }, anonymous: true, ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Archiving old audit rows failed; it will be tried again at the next hourly check.");
        }
    }
}
