namespace YasPortal.Web.Services;

/// <summary>
/// Blazor Server scoped state for application-wide UI notifications and toast messages.
/// One instance is created per Blazor circuit/user.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ToastMessage> _toasts = [];
    private readonly Dictionary<Guid, Timer> _toastTimers = [];
    private bool _disposed;

    public event Action? OnChange;

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
            DateTime.UtcNow);

        var delay = Math.Max(1000, durationMs);

        lock (_sync)
        {
            _toasts.Add(toast);

            // Each toast gets its own timer, so multiple messages can coexist
            // and each one is removed independently after its own duration.
            _toastTimers[toast.Id] = new Timer(
                _ => RemoveToast(toast),
                null,
                delay,
                Timeout.Infinite);
        }

        NotifyStateChanged();
    }

    public void RemoveToast(ToastMessage toast)
    {
        if (_disposed)
            return;

        Timer? timer = null;
        var removed = false;

        lock (_sync)
        {
            removed = _toasts.Remove(toast);

            if (_toastTimers.Remove(toast.Id, out timer))
            {
                // Dispose the timer so it cannot fire again.
                timer.Dispose();
            }
        }

        if (removed)
            NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        if (!_disposed)
            OnChange?.Invoke();
    }

    public void Dispose()
    {
        Timer[] timers;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            OnChange = null;
            _toasts.Clear();
            timers = _toastTimers.Values.ToArray();
            _toastTimers.Clear();
        }

        foreach (var timer in timers)
            timer.Dispose();
    }
}

public sealed record ToastMessage(
    Guid Id,
    string Message,
    ToastLevel Level,
    DateTime CreatedAt);

public enum ToastLevel
{
    Success,
    Info,
    Warning,
    Error
}
