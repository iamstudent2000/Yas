using YasPortal.Domain.Workflows;

namespace YasPortal.Application.Authorization;

/// <summary>
/// One permission: its code (Area.Resource.Action), Persian name, and the earlier codes it replaces.
/// The old codes let the seeder (and the SQL migration script) rename existing rows in place, so every grant is kept.
/// </summary>
public sealed record PermissionDefinition(string Code, string Name, params string[] OldCodes);

/// <summary>Constants for every permission code that is checked in code. Naming: Area.Resource.Action.</summary>
public static class Perm
{
    public const string DashboardPageView = "Dashboard.Page.View";
    public const string DashboardMineView = "Dashboard.Mine.View";
    public const string DashboardTeamView = "Dashboard.Team.View";
    public const string DashboardSystemView = "Dashboard.System.View";

    public const string ProfileOwnView = "Profile.Own.View";

    public const string RequestsOwnCreate = "Requests.Own.Create";
    public const string RequestsOwnView = "Requests.Own.View";
    public const string RequestsTeamView = "Requests.Team.View";
    public const string RequestsTeamApprovalsView = "Requests.TeamApprovals.View";
    public const string RequestsDecisionApprove = "Requests.Decision.Approve";
    public const string RequestsDecisionReject = "Requests.Decision.Reject";
    public const string RequestsDecisionReturnToRequester = "Requests.Decision.ReturnToRequester";
    public const string RequestsDecisionReturnToPreviousStep = "Requests.Decision.ReturnToPreviousStep";
    public const string RequestsDecisionReturnToSpecificStep = "Requests.Decision.ReturnToSpecificStep";

    public const string DelegationsOwnManage = "Delegations.Own.Manage";

    public const string AdminRequestsView = "Admin.Requests.View";
    public const string AdminRequestsCreateForOthers = "Admin.Requests.CreateForOthers";
    public const string AdminRequestsForceClose = "Admin.Requests.ForceClose";
    public const string AdminRequestsReopen = "Admin.Requests.Reopen";

    public const string AdminUsersView = "Admin.Users.View";
    public const string AdminUsersManage = "Admin.Users.Manage";
    public const string AdminOrganizationsView = "Admin.Organizations.View";
    public const string AdminOrganizationsManage = "Admin.Organizations.Manage";
    public const string AdminPositionsView = "Admin.Positions.View";
    public const string AdminPositionsManage = "Admin.Positions.Manage";
    public const string AdminWorkflowsView = "Admin.Workflows.View";
    public const string AdminWorkflowsManage = "Admin.Workflows.Manage";
    public const string AdminDelegationsView = "Admin.Delegations.View";
    public const string AdminDelegationsManage = "Admin.Delegations.Manage";
    public const string AdminAccessView = "Admin.Access.View";
    public const string AdminAccessManage = "Admin.Access.Manage";
    public const string AdminPermissionsView = "Admin.Permissions.View";
    public const string AdminPermissionsManage = "Admin.Permissions.Manage";
    public const string AdminAssignmentHistoryView = "Admin.AssignmentHistory.View";
    public const string AdminAuditLogView = "Admin.AuditLog.View";

    /// <summary>Anything of the form X.View is also satisfied by X.Manage.</summary>
    public static string? ManageFor(string viewCode) =>
        viewCode.EndsWith(".View", StringComparison.OrdinalIgnoreCase) ? viewCode[..^".View".Length] + ".Manage" : null;
}

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDefinition> All { get; } = Build();

    public static IEnumerable<string> Codes => All.Select(x => x.Code);

    private static List<PermissionDefinition> Build()
    {
        var list = new List<PermissionDefinition>
        {
            new(Perm.DashboardPageView, "مشاهده صفحه داشبورد", "Dashboard.View"),
            new(Perm.DashboardMineView, "آمار درخواست‌های من در داشبورد", "Dashboard.Mine"),
            new(Perm.DashboardTeamView, "آمار درخواست‌های زیرمجموعه در داشبورد", "Dashboard.Team"),
            new(Perm.DashboardSystemView, "آمار کل سامانه در داشبورد", "Dashboard.System"),

            new(Perm.ProfileOwnView, "مشاهده و ویرایش پروفایل خود", "Profile.View"),

            new(Perm.RequestsOwnCreate, "ثبت درخواست جدید", "Requests.Create"),
            new(Perm.RequestsOwnView, "مشاهده درخواست‌ها و کارتابل خود", "Requests.View"),
            new(Perm.RequestsTeamView, "مشاهده درخواست‌های زیرمجموعه", "Requests.ViewSubordinateRequests"),
            new(Perm.RequestsTeamApprovalsView, "مشاهده تصمیمات تایید زیرمجموعه", "Requests.ViewSubordinateApprovals"),
            new(Perm.RequestsDecisionApprove, "تأیید درخواست‌ها", "Requests.Approve"),
            new(Perm.RequestsDecisionReject, "رد درخواست‌ها", "Requests.Reject"),
            new(Perm.RequestsDecisionReturnToRequester, "بازگرداندن درخواست به درخواست‌کننده", "Requests.ReturnToRequester"),
            new(Perm.RequestsDecisionReturnToPreviousStep, "بازگرداندن درخواست به مرحله قبل", "Requests.ReturnToPreviousStep"),
            new(Perm.RequestsDecisionReturnToSpecificStep, "بازگشت به مرحله دلخواه", "Requests.ReturnToSpecificStep"),

            new(Perm.DelegationsOwnManage, "تفویض اختیار سمت خود به دیگری", "Requests.Delegation", "Delegations.Create"),

            new(Perm.AdminRequestsView, "گزارش و جستجوی همه درخواست‌ها", "Admin.Requests"),
            new(Perm.AdminRequestsCreateForOthers, "ثبت درخواست برای دیگران", "Admin.RequestForOthers"),
            new(Perm.AdminRequestsForceClose, "بستن اجباری درخواست‌های گیر کرده", "Requests.ForceClose"),
            new(Perm.AdminRequestsReopen, "بازگشایی درخواست‌های ردشده", "Requests.Reopen"),

            new(Perm.AdminUsersView, "مشاهده کاربران"),
            new(Perm.AdminUsersManage, "مدیریت کاربران", "Admin.Users"),
            new(Perm.AdminOrganizationsView, "مشاهده سازمان‌ها"),
            new(Perm.AdminOrganizationsManage, "مدیریت سازمان‌ها", "Admin.Organizations"),
            new(Perm.AdminPositionsView, "مشاهده سمت‌ها"),
            new(Perm.AdminPositionsManage, "مدیریت سمت‌ها", "Admin.Positions"),
            new(Perm.AdminWorkflowsView, "مشاهده مراحل گردش‌کار"),
            new(Perm.AdminWorkflowsManage, "مدیریت مراحل گردش‌کار", "Admin.Workflows"),
            new(Perm.AdminDelegationsView, "مشاهده تفویض‌ها"),
            new(Perm.AdminDelegationsManage, "مدیریت تفویض‌ها و تعیین جانشین موقت", "Admin.Delegation", "Delegations.Manage"),
            new(Perm.AdminAccessView, "مشاهده دسترسی‌ها"),
            new(Perm.AdminAccessManage, "مدیریت دسترسی‌ها", "Admin.Access"),
            new(Perm.AdminPermissionsView, "مشاهده مجوزها و گروه‌های مجوز"),
            new(Perm.AdminPermissionsManage, "مدیریت مجوزها و گروه‌های مجوز", "Admin.Permissions"),
            new(Perm.AdminAssignmentHistoryView, "مشاهده سوابق تخصیص سمت‌ها", "Admin.AssignmentHistory"),
            new(Perm.AdminAuditLogView, "مشاهده گزارش رویدادها", "Admin.AuditLog"),
        };

        list.AddRange(ReportPermissions.All.Select(x => new PermissionDefinition(x.Code, x.Name, x.OldCode)));
        list.AddRange(FormPermissions.All.Select(x => new PermissionDefinition(x.Code, x.Name, x.OldCode)));
        return list;
    }
}
