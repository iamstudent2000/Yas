namespace YasPortal.Application.Authorization;

/// <summary>
/// One permission per list that can export a report, so access to the CSV exports can be given page by page.
/// The codes are used by the authorization policies, the filter bar's ⋯ menu, the export service and the seeder.
/// </summary>
public static class ReportPermissions
{
    public const string AllRequests = "Reports.AllRequests";
    public const string ActiveRequests = "Reports.ActiveRequests";
    public const string RejectedRequests = "Reports.RejectedRequests";
    public const string TeamRequests = "Reports.TeamRequests";
    public const string MyRequests = "Reports.MyRequests";
    public const string Users = "Reports.Users";
    public const string Organizations = "Reports.Organizations";
    public const string Positions = "Reports.Positions";
    public const string AssignmentHistory = "Reports.AssignmentHistory";
    public const string AuditLog = "Reports.AuditLog";
    public const string Delegations = "Reports.Delegations";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (AllRequests, "خروجی گزارش درخواست‌ها (مدیریت)"),
        (ActiveRequests, "خروجی درخواست‌های در جریان (مدیریت)"),
        (RejectedRequests, "خروجی درخواست‌های ردشده (مدیریت)"),
        (TeamRequests, "خروجی درخواست‌های زیرمجموعه"),
        (MyRequests, "خروجی درخواست‌های من"),
        (Users, "خروجی فهرست کاربران"),
        (Organizations, "خروجی فهرست سازمان‌ها"),
        (Positions, "خروجی فهرست سمت‌ها"),
        (AssignmentHistory, "خروجی سوابق تخصیص سمت‌ها"),
        (AuditLog, "خروجی گزارش رویدادها"),
        (Delegations, "خروجی فهرست تفویض‌ها"),
    ];
}
