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

    /// <summary>A step targets a position nobody currently holds (spec §5.4). Recipient: SuperAdmins.</summary>
    VacantPosition,
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

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RecipientEmployeeId { get; private set; }
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
