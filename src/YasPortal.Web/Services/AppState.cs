namespace YasPortal.Web.Services;

/// <summary>
/// Blazor Server scoped state for application-wide UI notifications and toast messages.
/// One instance is created per Blazor circuit/user.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ToastMessage> _toasts = [];
    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly Task _cleanupTask;
    private bool _disposed;

    public event Action? OnChange;

    public AppState()
    {
        _cleanupTask = CleanupExpiredToastsAsync(_cleanupCts.Token);
    }

    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_sync)
                return _toasts.ToArray();
        }
    }

    public int UnreadNotificationCount { get; private set; }

    public void SetUnreadNotificationCount(int count)
    {
        if (_disposed)
            return;

        UnreadNotificationCount = Math.Max(0, count);
        NotifyStateChanged();
    }

    public void AddToast(string message, ToastLevel level = ToastLevel.Info, int durationMs = 4500)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
            return;

        var toast = new ToastMessage(
            Guid.NewGuid(),
            message.Trim(),
            level,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMilliseconds(Math.Max(1000, durationMs)));

        lock (_sync)
        {
            // Newest toast is stored last. The component renders the list in reverse
            // order so the newest message is always visually above older messages.
            _toasts.Add(toast);
        }

        NotifyStateChanged();
    }

    public void RemoveToast(ToastMessage toast)
    {
        if (_disposed)
            return;

        var removed = false;
        lock (_sync)
        {
            removed = _toasts.Remove(toast);
        }

        if (removed)
            NotifyStateChanged();
    }

    private async Task CleanupExpiredToastsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                List<ToastMessage>? expired = null;

                lock (_sync)
                {
                    if (_toasts.Count == 0)
                        continue;

                    var now = DateTime.UtcNow;
                    for (var i = _toasts.Count - 1; i >= 0; i--)
                    {
                        if (_toasts[i].ExpiresAt <= now)
                        {
                            expired ??= [];
                            expired.Add(_toasts[i]);
                            _toasts.RemoveAt(i);
                        }
                    }
                }

                if (expired is not null)
                    NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the Blazor circuit is disposed.
        }
    }

    private void NotifyStateChanged()
    {
        if (!_disposed)
            OnChange?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupCts.Cancel();
        _cleanupCts.Dispose();

        lock (_sync)
            _toasts.Clear();

        OnChange = null;
    }
}

public sealed record ToastMessage(
    Guid Id,
    string Message,
    ToastLevel Level,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public enum ToastLevel
{
    Success,
    Info,
    Warning,
    Error
}
