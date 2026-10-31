using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>
/// Checks every hour whether the automatic archive is due. The administrator decides on the audit page whether it is on and how
/// old a row must be (see <see cref="AuditArchiveSetting"/>); when on, it runs about once a day. The first check is a couple of
/// minutes after start-up. A failure is logged and tried again at the next check.
/// </summary>
public sealed class AuditArchiveWorker(IServiceScopeFactory scopes, ILogger<AuditArchiveWorker> logger) : BackgroundService
{
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
