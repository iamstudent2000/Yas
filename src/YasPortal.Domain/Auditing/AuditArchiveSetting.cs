namespace YasPortal.Domain.Auditing;

/// <summary>
/// How old audit rows are archived. There is exactly one row (<see cref="SingletonId"/>), created with the defaults the first time
/// it is read. The administrator picks the method: automatic (a daily job archives rows older than <see cref="AfterMonths"/>) or
/// manual only (nothing happens until someone presses "archive now"). "Archive now" is available with either method.
/// </summary>
public sealed class AuditArchiveSetting
{
    public const int SingletonId = 1;
    public const int LowestMonths = 1;
    public const int HighestMonths = 120;

    private AuditArchiveSetting()
    {
    }

    public static AuditArchiveSetting CreateDefault(bool automatic = true, int afterMonths = 12) => new()
    {
        AutomaticEnabled = automatic,
        AfterMonths = Math.Clamp(afterMonths, LowestMonths, HighestMonths),
    };

    public int Id { get; private set; } = SingletonId;
    /// <summary>True: the daily job archives. False: manual only.</summary>
    public bool AutomaticEnabled { get; private set; } = true;
    /// <summary>The automatic job archives rows older than this many months (also the default for "archive now").</summary>
    public int AfterMonths { get; private set; } = 12;
    public DateTime? LastAutomaticRunAtUtc { get; private set; }
    public int LastAutomaticRunCount { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    public void Update(bool automaticEnabled, int afterMonths)
    {
        if (afterMonths < LowestMonths || afterMonths > HighestMonths)
            throw new ArgumentOutOfRangeException(nameof(afterMonths), $"Months must be between {LowestMonths} and {HighestMonths}.");
        AutomaticEnabled = automaticEnabled;
        AfterMonths = afterMonths;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RecordAutomaticRun(DateTime atUtc, int count)
    {
        LastAutomaticRunAtUtc = atUtc;
        LastAutomaticRunCount = count;
    }

    /// <summary>True when the daily job should run now: automatic is on and it has not run in the last 23 hours.</summary>
    public bool IsAutomaticRunDue(DateTime nowUtc) =>
        AutomaticEnabled && (LastAutomaticRunAtUtc is null || nowUtc - LastAutomaticRunAtUtc.Value >= TimeSpan.FromHours(23));
}
