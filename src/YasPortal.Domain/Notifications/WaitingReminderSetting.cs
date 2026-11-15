namespace YasPortal.Domain.Notifications;

// Waiting reminders nag approvers about requests that sit too long in their inbox. Each approver sets their
// own limit in hours; the reminder is sent as an in-app notification.
/// <summary>
/// One employee's own "remind me" threshold: after a request has waited this many hours for their decision, they get one
/// in-app notification for it. Every person chooses their own number; there is no system-wide value. A person without a row
/// gets no reminders. One row per employee.
/// </summary>
public sealed class WaitingReminderSetting
{
    // Allowed range for the waiting time, in hours (one hour up to 30 days).
    public const int MinHours = 1;
    public const int MaxHours = 720;   // 30 days

    private WaitingReminderSetting()
    {
    }

    // Creating a setting stores the waiting time and, optionally, whether and how often to repeat the
    // reminder.
    public WaitingReminderSetting(Guid employeeId, int afterHours, bool repeat = false, int? repeatEveryHours = null)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));
        EmployeeId = employeeId;
        SetHours(afterHours);
        Update(afterHours, repeat, repeatEveryHours);
    }

    // Settings: after how many hours to remind, and when it was last changed.
    public Guid EmployeeId { get; private set; }
    public int AfterHours { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When true the reminder is sent again every <see cref="AfterHours"/> hours until the request is handled; otherwise once per waiting period.</summary>
    public bool Repeat { get; private set; }

    /// <summary>Hours between repeats; null means "the same as <see cref="AfterHours"/>". Only used when <see cref="Repeat"/> is on.</summary>
    public int? RepeatEveryHours { get; private set; }

    /// <summary>The number of hours actually used between repeats.</summary>
    public int EffectiveRepeatHours => RepeatEveryHours ?? AfterHours;

    // Changing the settings. Hours outside the allowed range are refused; the repeat interval is only kept
    // when repeating is on.
    public void Update(int afterHours, bool repeat, int? repeatEveryHours = null)
    {
        SetHours(afterHours);
        if (repeat && repeatEveryHours is int every && Validate(every) is { } error)
            throw new ArgumentOutOfRangeException(nameof(repeatEveryHours), error);
        Repeat = repeat;
        RepeatEveryHours = repeat ? repeatEveryHours : null;
    }

    public void SetHours(int afterHours)
    {
        var error = Validate(afterHours);
        if (error is not null)
            throw new ArgumentOutOfRangeException(nameof(afterHours), error);
        AfterHours = afterHours;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // The returned message is shown to the person in Persian.
    /// <summary>A Persian reason the number is not allowed, or null when it is fine.</summary>
    public static string? Validate(int hours) =>
        hours is < MinHours or > MaxHours ? $"تعداد ساعت باید بین {MinHours} و {MaxHours} باشد." : null;
}
