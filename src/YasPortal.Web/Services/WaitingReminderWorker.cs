namespace YasPortal.Web.Services;

/// <summary>
/// Every 15 minutes, sends the "waiting too long" reminders people asked for on their profile (see <see cref="WaitingReminderService"/>).
/// The first check is two minutes after start-up. A failure is logged and tried again at the next check.
/// </summary>
public sealed class WaitingReminderWorker(IServiceScopeFactory scopes, ILogger<WaitingReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
            do
            {
                await RunAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<WaitingReminderService>().RunAsync(DateTime.UtcNow, ct);
            if (count > 0)
                logger.LogInformation("Sent {Count} waiting reminders.", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending waiting reminders failed; it will be tried again at the next check.");
        }
    }
}
