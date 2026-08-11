namespace YasPortal.Web.Services;

/// <summary>
/// Blazor Server scoped state for application-wide UI notifications and toast messages.
/// One instance is created per Blazor circuit/user.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ToastMessage> _toasts = [];
<<<<<<< HEAD
    private readonly Dictionary<Guid, Timer> _toastTimers = [];
=======
    private readonly CancellationTokenSource _cleanupCts = new();
    private readonly Task _cleanupTask;
>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
    private bool _disposed;

    public event Action? OnChange;

<<<<<<< HEAD
=======
    public AppState()
    {
        _cleanupTask = CleanupExpiredToastsAsync(_cleanupCts.Token);
    }

>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
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
<<<<<<< HEAD
            message,
            level,
            DateTime.UtcNow);

        var delay = Math.Max(1000, durationMs);

        lock (_sync)
        {
            _toasts.Add(toast);

            // Use a Timer rather than an async fire-and-forget delay. The timer
            // reliably fires even though the toast was created from a UI event.
            _toastTimers[toast.Id] = new Timer(
                _ => RemoveToast(toast),
                null,
                delay,
                Timeout.Infinite);
=======
            message.Trim(),
            level,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMilliseconds(Math.Max(1000, durationMs)));

        lock (_sync)
        {
            // Newest toast is stored last. The component renders the list in reverse
            // order so the newest message is always visually above older messages.
            _toasts.Add(toast);
>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
        }

        NotifyStateChanged();
    }

    public void RemoveToast(ToastMessage toast)
    {
<<<<<<< HEAD
        Timer? timer = null;
        var removed = false;

        lock (_sync)
        {
            removed = _toasts.Remove(toast);

            if (_toastTimers.Remove(toast.Id, out timer))
            {
                // The timer has already fired when this method is called by
                // its callback; Dispose is still safe and prevents reuse.
                timer.Dispose();
            }
=======
        if (_disposed)
            return;

        var removed = false;
        lock (_sync)
        {
            removed = _toasts.Remove(toast);
>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
        }

        if (removed)
            NotifyStateChanged();
    }

<<<<<<< HEAD
=======
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

>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
    private void NotifyStateChanged()
    {
        if (!_disposed)
            OnChange?.Invoke();
    }

    public void Dispose()
    {
<<<<<<< HEAD
        Timer[] timers;

        lock (_sync)
        {
            _disposed = true;
            OnChange = null;
            _toasts.Clear();
            timers = _toastTimers.Values.ToArray();
            _toastTimers.Clear();
        }

        foreach (var timer in timers)
            timer.Dispose();
=======
        if (_disposed)
            return;

        _disposed = true;
        _cleanupCts.Cancel();
        _cleanupCts.Dispose();

        lock (_sync)
            _toasts.Clear();

        OnChange = null;
>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f
    }
}

public sealed record ToastMessage(
    Guid Id,
    string Message,
    ToastLevel Level,
<<<<<<< HEAD
    DateTime CreatedAt);
=======
    DateTime CreatedAt,
    DateTime ExpiresAt);
>>>>>>> 7a644de2d5f7ea05ecdb48202efa4affb618be5f

public enum ToastLevel
{
    Success,
    Info,
    Warning,
    Error
}
