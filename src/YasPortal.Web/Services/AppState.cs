namespace YasPortal.Web.Services;

/// <summary>
/// Blazor Server scoped state for UI-level notifications and toast messages.
/// One instance is created per Blazor circuit/user.
/// </summary>
public sealed class AppState : IDisposable
{
    private readonly object _sync = new();
    private readonly List<ToastMessage> _toasts = [];
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
        UnreadNotificationCount = Math.Max(0, count);
        NotifyStateChanged();
    }

    public void AddToast(string message, ToastLevel level = ToastLevel.Info, int durationMs = 4500)
    {
        if (_disposed || string.IsNullOrWhiteSpace(message))
            return;

        var toast = new ToastMessage(Guid.NewGuid(), message, level, DateTime.UtcNow);
        lock (_sync)
            _toasts.Add(toast);

        NotifyStateChanged();
        _ = AutoRemoveAsync(toast, Math.Max(1000, durationMs));
    }

    public void RemoveToast(ToastMessage toast)
    {
        if (_disposed)
            return;

        var removed = false;
        lock (_sync)
            removed = _toasts.Remove(toast);

        if (removed)
            NotifyStateChanged();
    }

    private async Task AutoRemoveAsync(ToastMessage toast, int durationMs)
    {
        try
        {
            await Task.Delay(durationMs);
            RemoveToast(toast);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void NotifyStateChanged()
    {
        if (!_disposed)
            OnChange?.Invoke();
    }

    public void Dispose()
    {
        _disposed = true;
        OnChange = null;
        lock (_sync)
            _toasts.Clear();
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
