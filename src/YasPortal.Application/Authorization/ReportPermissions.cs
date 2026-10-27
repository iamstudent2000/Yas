namespace YasPortal.Application.Authorization;

/// <summary>
/// One permission per list that can export a report, so access to the CSV exports can be given page by page.
/// The codes are used by the authorization policies, the filter bar's ⋯ menu, the export service and the seeder.
/// </summary>
public static class ReportPermissions
{
    public const string AllRequests = "Reports.AllRequests.Export";
    public const string ActiveRequests = "Reports.ActiveRequests.Export";
    public const string RejectedRequests = "Reports.RejectedRequests.Export";
    public const string TeamRequests = "Reports.TeamRequests.Export";
    public const string MyRequests = "Reports.MyRequests.Export";
    public const string Users = "Reports.Users.Export";
    public const string Organizations = "Reports.Organizations.Export";
    public const string Positions = "Reports.Positions.Export";
    public const string AssignmentHistory = "Reports.AssignmentHistory.Export";
    public const string AuditLog = "Reports.AuditLog.Export";
    public const string Delegations = "Reports.Delegations.Export";
    public const string Notifications = "Reports.Notifications.Export";

    public static readonly IReadOnlyList<(string Code, string Name, string OldCode)> All =
    [
        (AllRequests, "خروجی گزارش درخواست‌ها (مدیریت)", "Reports.AllRequests"),
        (ActiveRequests, "خروجی درخواست‌های در جریان (مدیریت)", "Reports.ActiveRequests"),
        (RejectedRequests, "خروجی درخواست‌های ردشده (مدیریت)", "Reports.RejectedRequests"),
        (TeamRequests, "خروجی درخواست‌های زیرمجموعه", "Reports.TeamRequests"),
        (MyRequests, "خروجی درخواست‌های من", "Reports.MyRequests"),
        (Users, "خروجی فهرست کاربران", "Reports.Users"),
        (Organizations, "خروجی فهرست سازمان‌ها", "Reports.Organizations"),
        (Positions, "خروجی فهرست سمت‌ها", "Reports.Positions"),
        (AssignmentHistory, "خروجی سوابق تخصیص سمت‌ها", "Reports.AssignmentHistory"),
        (AuditLog, "خروجی گزارش رویدادها", "Reports.AuditLog"),
        (Delegations, "خروجی فهرست تفویض‌ها", "Reports.Delegations"),
        (Notifications, "خروجی اعلان‌ها (گزارش همه اعلان‌ها و فهرست اعلان‌های من)", ""),
    ];
}
