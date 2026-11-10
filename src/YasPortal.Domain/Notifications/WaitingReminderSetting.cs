namespace YasPortal.Domain.Notifications;

/// <summary>
/// One employee's own "remind me" threshold: after a request has waited this many hours for their decision, they get one
/// in-app notification for it. Every person chooses their own number; there is no system-wide value. A person without a row
/// gets no reminders. One row per employee.
/// </summary>
public sealed class WaitingReminderSetting
{
    public const int MinHours = 1;
    public const int MaxHours = 720;   // 30 days

    private WaitingReminderSetting()
    {
    }

    public WaitingReminderSetting(Guid employeeId, int afterHours, bool repeat = false)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));
        EmployeeId = employeeId;
        SetHours(afterHours);
        Repeat = repeat;
    }

    public Guid EmployeeId { get; private set; }
    public int AfterHours { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When true the reminder is sent again every <see cref="AfterHours"/> hours until the request is handled; otherwise once per waiting period.</summary>
    public bool Repeat { get; private set; }

    public void Update(int afterHours, bool repeat)
    {
        SetHours(afterHours);
        Repeat = repeat;
    }

    public void SetHours(int afterHours)
    {
        var error = Validate(afterHours);
        if (error is not null)
            throw new ArgumentOutOfRangeException(nameof(afterHours), error);
        AfterHours = afterHours;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>A Persian reason the number is not allowed, or null when it is fine.</summary>
    public static string? Validate(int hours) =>
        hours is < MinHours or > MaxHours ? $"تعداد ساعت باید بین {MinHours} و {MaxHours} باشد." : null;
}
