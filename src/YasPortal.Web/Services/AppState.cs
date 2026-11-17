namespace YasPortal.Web.Services;

// Holds the small pieces of screen state that belong to one signed-in browser session: the pop-up "toast" messages
// (short success or error notes that vanish by themselves) and the unread-notification count shown in the top bar.
// Pages and layout parts subscribe to the change event so the screen refreshes when something here changes.
/// <summary>
/// Blazor Server scoped state for application-wide UI notifications and toast messages.
/// One instance is created per Blazor circuit/user.
/// </summary>
public sealed class AppState : IDisposable
{
    // The lock protects the list of toasts because timers fire on other threads while the page is also reading it.
    private readonly object _sync = new();
    private readonly List<ToastMessage> _toasts = [];
    private readonly Dictionary<Guid, Timer> _toastTimers = [];
    private bool _disposed;

    // Raised whenever anything on this object changes, so listening components redraw themselves.
    public event Action? OnChange;

    /// <summary>
    /// Newest toast is always first. A snapshot is returned so the renderer never
    /// enumerates the mutable backing collection outside the lock.
    /// </summary>
    public IReadOnlyList<ToastMessage> Toasts
    {
        get {
            lock (_sync)
                return _toasts.ToArray();
        }
    }

    // Number shown on the bell icon in the top bar. Never negative.
    public int UnreadNotificationCount
    {
        get; private set;
    }

    public void SetUnreadNotificationCount(int count)
    {
        if (_disposed)
            return;

        UnreadNotificationCount = Math.Max(0, count);
        NotifyStateChanged();
    }

    // Used by the sidebar: asks it to re-read its counters (requests and inbox badges) without changing toasts.
    /// <summary>
    /// Tells anything listening (e.g. the sidebar's request/inbox badge counts) to
    /// re-fetch, without touching toasts or <see cref="UnreadNotificationCount"/>.
    /// Call this right after an in-page action changes what those badges should show
    /// (marking a request seen, acting on an inbox item) so the same circuit's sidebar
    /// updates immediately instead of waiting for the next page navigation.
    /// </summary>
    public void NotifyRequestBadgesChanged()
    {
        if (!_disposed)
            NotifyStateChanged();
    }

    // Shows a toast message. Empty messages are ignored and each toast disappears on its own after the given time (min 1 second).
    public void AddToast(string message, ToastLevel level = ToastLevel.Info, int durationMs = 4500)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
            return;

        var toast = new ToastMessage(
            Guid.NewGuid(),
            message.Trim(),
            level,
            DateTime.UtcNow);

        // The minimum display time is one second even if a shorter time is requested.
        var delay = Math.Max(1000, durationMs);

        lock (_sync)
        {
            // Insert at index 0. The UI therefore has one deterministic rule:
            // newest message is always rendered at the top of the stack.
            _toasts.Insert(0, toast);

            // Every toast owns its own one-shot timer. Adding a second toast never
            // replaces or resets the timer belonging to the first toast.
            _toastTimers[toast.Id] = new Timer(
                static state => {
                    if (state is ToastRemovalState removal)
                        removal.AppState.RemoveToast(removal.Toast);
                },
                new ToastRemovalState(this, toast),
                delay,
                Timeout.Infinite);
        }

        NotifyStateChanged();
    }

    // Removes one toast, either because its timer ran out or because it was closed.
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
                timer.Dispose();
        }

        if (removed)
            NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        if (!_disposed)
            OnChange?.Invoke();
    }

    // Called when the session ends: stops every toast timer and forgets all listeners so nothing keeps running.
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

    private sealed record ToastRemovalState(AppState AppState, ToastMessage Toast);
}

// One toast: a unique id, the text, its level and when it was created.
public sealed record ToastMessage(
    Guid Id,
    string Message,
    ToastLevel Level,
    DateTime CreatedAt);

// Colour and meaning of a toast: success, plain information, warning or error.
public enum ToastLevel
{
    Success,
    Info,
    Warning,
    Error
}
