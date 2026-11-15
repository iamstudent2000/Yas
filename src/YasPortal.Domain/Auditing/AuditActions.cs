namespace YasPortal.Domain.Auditing;

// The audit trail is the permanent history of who did what in the portal. Each entry records one action, and entries
// are never deleted (old ones are only moved to an archive). This file holds the fixed names used for those actions.
/// <summary>
/// The values stored in <see cref="AuditEntry.Action"/>. Created / Updated / Deleted are written
/// automatically for every changed row; the rest are explicit events (sign-in, exports, ...).
/// </summary>
public static class AuditActions
{
    // Data changes: written automatically whenever a row in the database is added, changed or removed.
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    // Explicit events: things people do that are not data changes, such as signing in, switching position or exporting.
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoggedOut = "LoggedOut";
    public const string PositionSwitched = "PositionSwitched";
    public const string SessionEnded = "SessionEnded";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string ReportExported = "ReportExported";
    public const string AccessDenied = "AccessDenied";
    public const string Searched = "Searched";
    public const string PageViewed = "PageViewed";
    public const string RequestViewed = "RequestViewed";
    public const string RequestPdfDownloaded = "RequestPdfDownloaded";
    public const string RequestFormPrinted = "RequestFormPrinted";
    public const string PhoneBookPrinted = "PhoneBookPrinted";
    public const string NotificationSent = "NotificationSent";
    public const string NotificationRetracted = "NotificationRetracted";
    public const string NotificationRemoved = "NotificationRemoved";
    public const string NotificationRestored = "NotificationRestored";
    public const string NotificationsViewed = "NotificationsViewed";
    public const string NotificationRead = "NotificationRead";
    public const string NotificationsMarkedRead = "NotificationsMarkedRead";
    public const string AuditEntryViewed = "AuditEntryViewed";
    public const string AuditArchived = "AuditArchived";

    // The list below is used to fill the event filter on the audit page.
    /// <summary>The explicit events, in the order the audit page offers them.</summary>
    public static readonly IReadOnlyList<string> Events =
    [
        LoginSucceeded, LoginFailed, LoggedOut, PositionSwitched, SessionEnded,
        PasswordChanged, PasswordReset, ReportExported, Searched, PageViewed, AccessDenied,
        RequestViewed, RequestPdfDownloaded, RequestFormPrinted, PhoneBookPrinted,
        NotificationSent, NotificationRetracted, NotificationRemoved, NotificationRestored,
        NotificationsViewed, NotificationRead, NotificationsMarkedRead, AuditEntryViewed, AuditArchived,
    ];
}

// Entity types describe what an audit entry is about. Data changes use the name of the changed object instead.
/// <summary>The <see cref="AuditEntry.EntityType"/> values used by explicit events (data changes use the entity's class name).</summary>
public static class AuditEntityTypes
{
    /// <summary>Sign-in, sign-out and position-switch events; <see cref="AuditEntry.EntityId"/> is the employee.</summary>
    public const string Session = "Session";

    /// <summary>A CSV export (<see cref="AuditEntry.EntityId"/> is the file's base name) or a list search (the page's path).</summary>
    public const string Report = "Report";

    /// <summary>A report page opened, or a blocked page visit; the path is in the details.</summary>
    public const string Access = "Access";

    /// <summary>One audit row opened in the details window; <see cref="AuditEntry.EntityId"/> is that row's id.</summary>
    public const string AuditRecord = "AuditEntry";

    /// <summary>A notification send, take-back or removal; <see cref="AuditEntry.EntityId"/> is the batch when there is one.</summary>
    public const string Notification = "Notification";
}

// Reasons give extra detail for a failed or refused event, for example why a sign-in did not succeed.
/// <summary>Why an explicit event happened, stored in its details as <c>Reason</c>.</summary>
public static class AuditReasons
{
    public const string UnknownOrInactive = "UnknownOrInactive";
    public const string BadPassword = "BadPassword";
    public const string NoPosition = "NoPosition";
    public const string IdentityInvalid = "IdentityInvalid";
}
