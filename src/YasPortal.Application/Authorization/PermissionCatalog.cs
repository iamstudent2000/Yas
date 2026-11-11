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
    public const string RequestsTeamCreate = "Requests.Team.Create";
    public const string RequestsTeamApprovalsView = "Requests.TeamApprovals.View";
    public const string RequestsDecisionApprove = "Requests.Decision.Approve";
    public const string RequestsDecisionReject = "Requests.Decision.Reject";
    public const string RequestsDecisionReturnToRequester = "Requests.Decision.ReturnToRequester";
    public const string RequestsDecisionReturnToPreviousStep = "Requests.Decision.ReturnToPreviousStep";
    public const string RequestsDecisionReturnToSpecificStep = "Requests.Decision.ReturnToSpecificStep";
    /// <summary>Take back a rejection the holder made themselves (from the inbox history), as long as it still stands.</summary>
    public const string RequestsDecisionReopenOwn = "Requests.Decision.ReopenOwn";

    public const string DelegationsOwnManage = "Delegations.Own.Manage";

    /// <summary>Send a notification by hand to any active employee, and see the ones you sent.</summary>
    public const string NotificationsMessageSend = "Notifications.Message.Send";

    /// <summary>Send the same notification to every active employee, or to all members of chosen organizations (on top of <see cref="NotificationsMessageSend"/>).</summary>

    public const string AdminRequestsView = "Admin.Requests.View";
    public const string AdminRequestsForceClose = "Admin.Requests.ForceClose";
    public const string AdminRequestsReopen = "Admin.Requests.Reopen";
    /// <summary>See the helpdesk report: counts by category and priority, resolution times and the longest-waiting open requests (read-only).</summary>
    public const string AdminHelpdeskReportView = "Admin.Helpdesk.Report";

    public const string AdminUsersView = "Admin.Users.View";
    public const string AdminUsersManage = "Admin.Users.Manage";
    /// <summary>Set or change another user's password from the users page (a new user's first password still comes with creating the user).</summary>
    public const string AdminUsersPasswordSet = "Admin.Users.PasswordSet";
    /// <summary>See the password rules and try a sample password against them (read-only).</summary>
    public const string AdminPasswordPolicyView = "Admin.PasswordPolicy.View";
    /// <summary>Change the system-wide password rules. Implies the view permission.</summary>
    public const string AdminPasswordPolicyManage = "Admin.PasswordPolicy.Manage";
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
    /// <summary>Move old events of the audit log to the archive by hand. Implies the view permission.</summary>
    public const string AdminAuditLogManage = "Admin.AuditLog.Manage";
    public const string AdminLandingSlidesView = "Admin.LandingSlides.View";
    public const string AdminLandingSlidesManage = "Admin.LandingSlides.Manage";
    public const string AdminLinksView = "Admin.Links.View";
    public const string AdminLinksManage = "Admin.Links.Manage";
    /// <summary>Print the phone book (names, extensions and phones, in two columns on A4). Reading the phone book needs no permission, even before sign-in.</summary>
    public const string PhoneBookDirectoryPrint = "PhoneBook.Directory.Print";
    public const string AdminPhoneBookView = "Admin.PhoneBook.View";
    public const string AdminPhoneBookManage = "Admin.PhoneBook.Manage";
    /// <summary>The report of every notification in the system (who sent what to whom, read or not).</summary>
    public const string AdminNotificationsView = "Admin.Notifications.View";
    /// <summary>Remove (and restore) other people's notifications from the all-notifications report. Implies the view permission.</summary>
    public const string AdminNotificationsManage = "Admin.Notifications.Manage";
    /// <summary>Write new user guides, and edit, switch off and delete the guides this person wrote. Other people's guides stay untouched.</summary>
    public const string GuidesOwnManage = "Guides.Own.Manage";
    /// <summary>See every guide in the guides workspace, also guides that are off (read-only).</summary>
    public const string AdminGuidesView = "Admin.Guides.View";
    /// <summary>Write guides and edit, switch off and delete the guides of everyone. Implies the view permission.</summary>
    public const string AdminGuidesManage = "Admin.Guides.Manage";
    /// <summary>Browse the store catalogue and see what is available.</summary>
    public const string StoreShopView = "Store.Shop.View";
    /// <summary>Make store requests (an order of items from the store). Admins are still kept out of the requester pages.</summary>
    public const string StoreOrdersCreate = "Store.Orders.Create";
    /// <summary>Hand approved store orders to the requester (this takes the goods out of stock).</summary>
    public const string StoreOrdersIssue = "Store.Orders.Issue";
    /// <summary>The store reports (summary, out of stock, issued goods, dormant goods, orders) with print and CSV export.</summary>
    public const string StoreReportsView = "Store.Reports.View";
    public const string StoreItemsView = "Store.Items.View";
    /// <summary>Add and edit store categories and items. Implies the view permission.</summary>
    public const string StoreItemsManage = "Store.Items.Manage";
    public const string StoreStockView = "Store.Stock.View";
    /// <summary>Record receipts, issues and count corrections. Implies the view permission.</summary>
    public const string StoreStockManage = "Store.Stock.Manage";
    /// <summary>Policy (not a permission code): the guides workspace is open to anyone who holds any of the three guide permissions.</summary>
    public const string GuidesWorkspacePolicy = "Guides.Workspace";
    /// <summary>Policy (not a permission code): the store admin area is open to anyone who holds any store admin permission.</summary>
    public const string StoreAdminPolicy = "Store.Admin";

    /// <summary>Anything of the form X.View is also satisfied by X.Manage.</summary>
    public static string? ManageFor(string viewCode) =>
        viewCode.EndsWith(".View", StringComparison.OrdinalIgnoreCase) ? viewCode[..^".View".Length] + ".Manage" : null;
}

/// <summary>Whose data a permission lets the holder reach.</summary>
public enum PermissionReach
{
    /// <summary>Only the holder's own data.</summary>
    Own,
    /// <summary>Requests routed to the holder as an approver (the holder acts on other people's requests, but only those assigned).</summary>
    Assigned,
    /// <summary>Requests and statistics of the holder's subordinates.</summary>
    Team,
    /// <summary>Data of everyone in the system.</summary>
    All,
}

public static class PermissionCatalog
{
    private static readonly HashSet<string> ExportAll = new(StringComparer.OrdinalIgnoreCase)
    {
        ReportPermissions.AllRequests, ReportPermissions.ActiveRequests, ReportPermissions.RejectedRequests, ReportPermissions.Users,
        ReportPermissions.Organizations, ReportPermissions.Positions, ReportPermissions.AssignmentHistory, ReportPermissions.AuditLog,
        ReportPermissions.Delegations,
    };

    /// <summary>Whose data the permission exposes. Shown as a badge wherever permissions are listed or granted.</summary>
    public static PermissionReach ReachOf(string code)
    {
        if (code.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase) || ExportAll.Contains(code) || code == Perm.DashboardSystemView)
            return PermissionReach.All;
        if (code.StartsWith("Store.", StringComparison.OrdinalIgnoreCase) && code is not (Perm.StoreShopView or Perm.StoreOrdersCreate))
            return PermissionReach.All;
        if (code.StartsWith("Requests.Decision.", StringComparison.OrdinalIgnoreCase))
            return PermissionReach.Assigned;
        if (code is Perm.RequestsTeamView or Perm.RequestsTeamApprovalsView or Perm.RequestsTeamCreate or Perm.DashboardTeamView || code == ReportPermissions.TeamRequests)
            return PermissionReach.Team;
        return PermissionReach.Own;
    }

    /// <summary>True when the permission lets the holder change or decide on data that is not theirs (not just read it).</summary>
    public static bool ActsOnOthers(string code) =>
        code.StartsWith("Requests.Decision.", StringComparison.OrdinalIgnoreCase)
        || code.EndsWith(".Manage", StringComparison.OrdinalIgnoreCase) && code.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase)
        || code is Perm.StoreItemsManage or Perm.StoreStockManage or Perm.StoreOrdersIssue
        || CreateForOthersPermissions.IsOne(code)
        || code is Perm.RequestsTeamCreate or Perm.AdminRequestsForceClose or Perm.AdminRequestsReopen;

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
            new(Perm.RequestsTeamCreate, "ثبت درخواست برای زیرمجموعه"),
            new(Perm.RequestsTeamApprovalsView, "مشاهده تصمیمات تایید زیرمجموعه", "Requests.ViewSubordinateApprovals"),
            new(Perm.RequestsDecisionApprove, "تأیید درخواست‌ها", "Requests.Approve"),
            new(Perm.RequestsDecisionReject, "رد درخواست‌ها", "Requests.Reject"),
            new(Perm.RequestsDecisionReturnToRequester, "بازگرداندن درخواست به درخواست‌کننده", "Requests.ReturnToRequester"),
            new(Perm.RequestsDecisionReturnToPreviousStep, "بازگرداندن درخواست به مرحله قبل", "Requests.ReturnToPreviousStep"),
            new(Perm.RequestsDecisionReturnToSpecificStep, "بازگشت به مرحله دلخواه", "Requests.ReturnToSpecificStep"),
            new(Perm.RequestsDecisionReopenOwn, "بازگشایی درخواستی که خودم رد کرده‌ام"),

            new(Perm.DelegationsOwnManage, "تفویض اختیار سمت خود به دیگری", "Requests.Delegation", "Delegations.Create"),

            new(Perm.NotificationsMessageSend, "ارسال اعلان به یک یا چند نفر، سازمان‌ها یا همه کارکنان، نگهداری گروه‌های گیرنده و مشاهده و پس‌گرفتن اعلان‌های ارسالی خود"),

            new(Perm.AdminRequestsView, "مشاهده گزارش همه درخواست‌ها", "Admin.Requests"),
            new(Perm.AdminRequestsForceClose, "بستن اجباری درخواست‌های گیر کرده", "Requests.ForceClose"),
            new(Perm.AdminRequestsReopen, "بازگشایی درخواست‌های ردشده", "Requests.Reopen"),
            new(Perm.AdminHelpdeskReportView, "مشاهده گزارش پشتیبانی (آمار درخواست‌های پشتیبانی، فقط خواندنی)"),

            new(Perm.AdminUsersView, "مشاهده کاربران (فقط خواندنی)"),
            new(Perm.AdminUsersManage, "مدیریت کاربران (ایجاد، ویرایش، فعال/غیرفعال)", "Admin.Users"),
            new(Perm.AdminUsersPasswordSet, "تعیین یا تغییر رمز عبور سایر کاربران"),
            new(Perm.AdminPasswordPolicyView, "مشاهده سیاست رمز عبور (فقط خواندنی)"),
            new(Perm.AdminPasswordPolicyManage, "مدیریت سیاست رمز عبور (ویرایش قوانین)"),
            new(Perm.AdminOrganizationsView, "مشاهده سازمان‌ها (فقط خواندنی)"),
            new(Perm.AdminOrganizationsManage, "مدیریت سازمان‌ها (ایجاد، ویرایش، تخصیص کارکنان)", "Admin.Organizations"),
            new(Perm.AdminPositionsView, "مشاهده سمت‌ها (فقط خواندنی)"),
            new(Perm.AdminPositionsManage, "مدیریت سمت‌ها (ایجاد، ویرایش، تخصیص، حذف)", "Admin.Positions"),
            new(Perm.AdminWorkflowsView, "مشاهده مراحل گردش‌کار (فقط خواندنی)"),
            new(Perm.AdminWorkflowsManage, "مدیریت مراحل گردش‌کار (افزودن، ویرایش، ترتیب، حذف)", "Admin.Workflows"),
            new(Perm.AdminDelegationsView, "مشاهده همه تفویض‌ها (فقط خواندنی)"),
            new(Perm.AdminDelegationsManage, "مدیریت همه تفویض‌ها و تعیین جانشین موقت", "Admin.Delegation", "Delegations.Manage"),
            new(Perm.AdminAccessView, "مشاهده دسترسی‌ها (فقط خواندنی)"),
            new(Perm.AdminAccessManage, "مدیریت دسترسی‌ها (تخصیص مجوز و گروه)", "Admin.Access"),
            new(Perm.AdminPermissionsView, "مشاهده مجوزها و گروه‌ها (فقط خواندنی)"),
            new(Perm.AdminPermissionsManage, "مدیریت مجوزها و گروه‌ها (ویرایش نام، ساخت گروه)", "Admin.Permissions"),
            new(Perm.AdminAssignmentHistoryView, "مشاهده سوابق تخصیص سمت‌ها", "Admin.AssignmentHistory"),
            new(Perm.AdminAuditLogView, "مشاهده گزارش رویدادها", "Admin.AuditLog"),
            new(Perm.AdminAuditLogManage, "مدیریت گزارش رویدادها (بایگانی رویدادهای قدیمی)"),
            new(Perm.AdminLandingSlidesView, "مشاهده اسلایدهای صفحه اصلی (فقط خواندنی)"),
            new(Perm.AdminLinksView, "مشاهده پیوندهای صفحه اصلی (فقط خواندنی)"),
            new(Perm.AdminLinksManage, "مدیریت پیوندهای صفحه اصلی (گروه‌ها، پیوندها، دسترسی سریع، ورود گروهی)"),
            new(Perm.AdminLandingSlidesManage, "مدیریت اسلایدهای صفحه اصلی (افزودن، ویرایش، حذف، تاریخ انقضا)"),
            new(Perm.PhoneBookDirectoryPrint, "چاپ دفترچه تلفن (نام، داخلی و تلفن در دو ستون A4)"),
            new(Perm.AdminPhoneBookView, "مشاهده مدیریت دفترچه تلفن (فقط خواندنی)"),
            new(Perm.AdminPhoneBookManage, "مدیریت دفترچه تلفن (گروه‌ها و مخاطبان)"),
            new(Perm.AdminNotificationsView, "مشاهده گزارش همه اعلان‌ها (فقط خواندنی)"),
            new(Perm.AdminNotificationsManage, "مدیریت اعلان‌ها (حذف و بازگردانی اعلان‌های دیگران)"),
            new(Perm.GuidesOwnManage, "نوشتن راهنمای کاربری و ویرایش راهنماهای خودم (غیرفعال‌کردن و حذف راهنماهای خودم)"),
            new(Perm.AdminGuidesView, "مشاهده همه راهنماهای کاربری (فقط خواندنی)"),
            new(Perm.AdminGuidesManage, "مدیریت همه راهنماهای کاربری (ویرایش، غیرفعال‌کردن و حذف راهنمای همه نویسنده‌ها)"),

            new(Perm.StoreShopView, "مشاهده فروشگاه (کالاها و موجودی)"),
            new(Perm.StoreOrdersCreate, "ثبت درخواست کالا از فروشگاه (و شرکت در قرعه‌کشی)"),
            new(Perm.StoreOrdersIssue, "تحویل کالای سفارش‌های تأییدشده فروشگاه (کسر از موجودی)"),
            new(Perm.StoreReportsView, "مشاهده گزارش‌های فروشگاه (چاپ و خروجی CSV)"),
            new(Perm.StoreItemsView, "مشاهده مدیریت کالاها (فقط خواندنی)"),
            new(Perm.StoreItemsManage, "مدیریت کالاها (گروه‌ها و کالاها)"),
            new(Perm.StoreStockView, "مشاهده موجودی و گردش انبار (فقط خواندنی)"),
            new(Perm.StoreStockManage, "مدیریت انبار (ورود کالا، خروج و اصلاح موجودی)"),
        };

        list.AddRange(ReportPermissions.All.Select(x => new PermissionDefinition(x.Code, x.Name, string.IsNullOrEmpty(x.OldCode) ? [] : [x.OldCode])));
        list.AddRange(CreateForOthersPermissions.All.Select(x => new PermissionDefinition(x.Code, x.Name)));
        list.AddRange(FormPermissions.All.Select(x => new PermissionDefinition(x.Code, x.Name, x.OldCode)));
        return list;
    }
}
