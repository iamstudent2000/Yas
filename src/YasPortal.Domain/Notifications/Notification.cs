namespace YasPortal.Domain.Notifications;

/// <summary>What happened, for the in-app notification (spec §17.2).</summary>
public enum NotificationKind
{
    /// <summary>A request reached a step: submitted, resubmitted, or moved on after an approval. Recipient: that step's assignee.</summary>
    StepReached,

    /// <summary>A later step sent the request back to a previous step. Recipient: that step's assignee.</summary>
    ReturnedToStep,

    /// <summary>All steps approved. Recipient: the requester.</summary>
    RequestApproved,

    /// <summary>The request was rejected. Recipient: the requester.</summary>
    RequestRejected,

    /// <summary>The request was returned to the requester. Recipient: the requester.</summary>
    RequestReturned,

    /// <summary>A SuperAdmin force-closed the request. Recipient: the requester.</summary>
    RequestForceClosed,

    /// <summary>A rejected request was reopened at this step. Recipient: that step's assignee.</summary>
    RequestReopened,

    /// <summary>Someone delegated a position's approval authority to this employee (spec §17.2). Recipient: the delegate.</summary>
    DelegationReceived,

    /// <summary>An administrator submitted a request on the requester's behalf. Recipient: the requester.</summary>
    RequestCreatedForYou,

    /// <summary>A step targets a position nobody currently holds (spec §5.4). Recipient: SuperAdmins.</summary>
    VacantPosition,

    /// <summary>A message one person sent to another by hand (not produced by a workflow event). Recipient: the chosen employee.</summary>
    Manual,
}

/// <summary>
/// One in-app notification for one employee (spec §17). Notifications are never deleted
/// automatically (§17.4); the only change after creation is the recipient marking one as read.
/// </summary>
public sealed class Notification
{
    private Notification()
    {
    }

    public Notification(Guid recipientEmployeeId, NotificationKind kind, Guid? requestId, string requestType, string message, string linkPath)
    {
        if (recipientEmployeeId == Guid.Empty)
            throw new ArgumentException("Recipient is required.", nameof(recipientEmployeeId));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));
        if (string.IsNullOrWhiteSpace(linkPath))
            throw new ArgumentException("Link is required.", nameof(linkPath));

        RecipientEmployeeId = recipientEmployeeId;
        Kind = kind;
        RequestId = requestId;
        RequestType = requestType ?? string.Empty;
        Message = message.Trim();
        LinkPath = linkPath;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>The longest message a person can send by hand (also the column size).</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>The longest audience description kept with a message sent to many people.</summary>
    public const int MaxAudienceLabelLength = 300;

    /// <summary>
    /// A message sent by hand from <paramref name="senderEmployeeId"/> to <paramref name="recipientEmployeeId"/>.
    /// <paramref name="senderLabel"/> is kept as text (like <see cref="RequestType"/>) so the line reads the same forever.
    /// </summary>
    public static Notification FromSender(Guid senderEmployeeId, Guid recipientEmployeeId, string senderLabel, string message, string linkPath,
        Guid? batchId = null, string? audienceLabel = null)
    {
        if (senderEmployeeId == Guid.Empty)
            throw new ArgumentException("Sender is required.", nameof(senderEmployeeId));
        if (senderEmployeeId == recipientEmployeeId)
            throw new ArgumentException("A notification cannot be sent to yourself.", nameof(recipientEmployeeId));
        if (message is not null && message.Trim().Length > MaxMessageLength)
            throw new ArgumentException($"Message is longer than {MaxMessageLength} characters.", nameof(message));

        var notification = new Notification(recipientEmployeeId, NotificationKind.Manual, null, $"پیام از «{senderLabel}»", message ?? string.Empty, linkPath);
        notification.SenderEmployeeId = senderEmployeeId;
        notification.BatchId = batchId;
        notification.AudienceLabel = string.IsNullOrWhiteSpace(audienceLabel) ? null : Truncate(audienceLabel.Trim(), MaxAudienceLabelLength);
        return notification;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RecipientEmployeeId { get; private set; }

    /// <summary>Who sent it, for hand-written messages (<see cref="NotificationKind.Manual"/>); null for notifications the system produced.</summary>
    public Guid? SenderEmployeeId { get; private set; }

    /// <summary>Ties together the copies of one message sent to several people at once; null for a message sent to a single person and for system notifications.</summary>
    public Guid? BatchId { get; private set; }

    /// <summary>Who the message went to, as text ("همه کارکنان", "سازمان‌ها: …", "۱۲ نفر منتخب"), for messages sent to several people.</summary>
    public string? AudienceLabel { get; private set; }

    /// <summary>When the message was removed (retracted by its sender, or removed by an administrator); null while it is shown. A removed notification disappears from the recipient's list but stays in the report.</summary>
    public DateTime? RemovedAtUtc { get; private set; }
    public Guid? RemovedByEmployeeId { get; private set; }
    public bool IsRemoved => RemovedAtUtc is not null;
    public NotificationKind Kind { get; private set; }
    public Guid? RequestId { get; private set; }

    /// <summary>Display name of the request type, kept as text so the notification reads the same forever.</summary>
    public string RequestType { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    /// <summary>App-relative link that opens the request (e.g. the inbox).</summary>
    public string LinkPath { get; private set; } = string.Empty;
    public bool IsRead { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>Hides the notification from its recipient (the record is kept). Removing twice keeps the first removal.</summary>
    public void Remove(Guid byEmployeeId)
    {
        if (byEmployeeId == Guid.Empty)
            throw new ArgumentException("Who removed it is required.", nameof(byEmployeeId));
        if (IsRemoved)
            return;
        RemovedAtUtc = DateTime.UtcNow;
        RemovedByEmployeeId = byEmployeeId;
    }

    /// <summary>Shows a removed notification to its recipient again.</summary>
    public void Restore()
    {
        RemovedAtUtc = null;
        RemovedByEmployeeId = null;
    }

    public void MarkRead()
    {
        if (IsRead)
            return;
        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
    }
}
