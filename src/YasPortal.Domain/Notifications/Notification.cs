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

    /// <summary>
    /// A message sent by hand from <paramref name="senderEmployeeId"/> to <paramref name="recipientEmployeeId"/>.
    /// <paramref name="senderLabel"/> is kept as text (like <see cref="RequestType"/>) so the line reads the same forever.
    /// </summary>
    public static Notification FromSender(Guid senderEmployeeId, Guid recipientEmployeeId, string senderLabel, string message, string linkPath)
    {
        if (senderEmployeeId == Guid.Empty)
            throw new ArgumentException("Sender is required.", nameof(senderEmployeeId));
        if (senderEmployeeId == recipientEmployeeId)
            throw new ArgumentException("A notification cannot be sent to yourself.", nameof(recipientEmployeeId));
        if (message is not null && message.Trim().Length > MaxMessageLength)
            throw new ArgumentException($"Message is longer than {MaxMessageLength} characters.", nameof(message));

        var notification = new Notification(recipientEmployeeId, NotificationKind.Manual, null, $"پیام از «{senderLabel}»", message ?? string.Empty, linkPath);
        notification.SenderEmployeeId = senderEmployeeId;
        return notification;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RecipientEmployeeId { get; private set; }

    /// <summary>Who sent it, for hand-written messages (<see cref="NotificationKind.Manual"/>); null for notifications the system produced.</summary>
    public Guid? SenderEmployeeId { get; private set; }
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

    public void MarkRead()
    {
        if (IsRead)
            return;
        IsRead = true;
        ReadAtUtc = DateTime.UtcNow;
    }
}
