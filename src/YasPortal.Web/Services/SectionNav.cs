using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Web.Services;

/// <summary>
/// One tab of a section. A person sees it when they satisfy at least one of the alternatives, and an alternative is
/// satisfied when every policy in it passes (so "own notifications" is one alternative of two policies, and
/// "delegations" is two alternatives of one policy each).
/// </summary>
public sealed record SectionTab(string Href, string Label, string Icon, string[][] Allowed, bool ExactMatch = false, string? BadgeKey = null)
{
    public bool IsCurrent(string path)
    {
        var p = path.Trim('/');
        var h = Href.Trim('/');
        return ExactMatch ? string.Equals(p, h, StringComparison.OrdinalIgnoreCase)
            : string.Equals(p, h, StringComparison.OrdinalIgnoreCase) || p.StartsWith(h + "/", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record SectionDef(string Key, string Title, string Icon, IReadOnlyList<SectionTab> Tabs);

/// <summary>
/// The groups of related pages that share one tab bar (like the store admin) and one sidebar link. Which tabs a person
/// gets, and where the sidebar link leads, both come from the same permission rules defined here.
/// </summary>
public static class SectionNav
{
    private static string[][] One(string policy) => [[policy]];

    public static readonly SectionDef Access = new("access", "مدیریت کاربران و دسترسی‌ها", Icons.AccessGroup,
    [
        new("/admin/users", "کاربران", Icons.Users, One(Perm.AdminUsersView)),
        new("/admin/access", "مدیریت دسترسی", Icons.AccessManagement, One(Perm.AdminAccessView)),
        new("/admin/password-policy", "سیاست رمز عبور", Icons.PasswordPolicy, One(Perm.AdminPasswordPolicyView)),
        new("/admin/permissions", "مجوزها", Icons.Permission, One(Perm.AdminPermissionsView)),
        new("/admin/permission-groups", "گروه‌های مجوز", Icons.PermissionGroup, One(Perm.AdminPermissionsView)),
    ]);

    public static readonly SectionDef Structure = new("structure", "ساختار سازمانی", Icons.OrgStructure,
    [
        new("/admin/positions", "سمت‌ها", Icons.Position, One(Perm.AdminPositionsView)),
        new("/admin/organizations", "سازمان‌ها", Icons.Organization, One(Perm.AdminOrganizationsView)),
        new("/admin/assignment-history", "سوابق تخصیص سمت‌ها", Icons.History, One(Perm.AdminAssignmentHistoryView)),
        new("/delegations", "تفویض اختیار", Icons.Delegation, [[Perm.DelegationsOwnManage], [Perm.AdminDelegationsView]]),
    ]);

    public static readonly SectionDef RequestAdmin = new("request-admin", "مدیریت درخواست‌ها", Icons.Workflow,
    [
        new("/admin/requests", "گزارش درخواست‌ها", Icons.RequestsReport, One(Perm.AdminRequestsView), ExactMatch: true),
        new("/admin/helpdesk-report", "گزارش پشتیبانی", Icons.HelpdeskReport, One(Perm.AdminHelpdeskReportView)),
        new("/admin/requests/new", "ثبت درخواست برای دیگران", Icons.NewRequestForOthers, One(Perm.AdminRequestsCreateForOthers)),
        new("/admin/workflows", "مراحل گردش‌کار", Icons.Workflow, One(Perm.AdminWorkflowsView)),
        new("/admin/rejected-requests", "درخواست‌های ردشده", Icons.RejectedRequests, One(Perm.AdminRequestsReopen)),
        new("/admin/active-requests", "درخواست‌های فعال", Icons.ActiveRequests, One(Perm.AdminRequestsForceClose)),
    ]);

    public static readonly SectionDef Notifications = new("notifications", "اعلان‌ها", Icons.Bell,
    [
        new("/notifications", "اعلان‌ها", Icons.Bell, [[Perm.RequestsOwnView, "NonAdmin"]], ExactMatch: true),
        new("/admin/notifications", "اعلان‌های مدیر", Icons.Bell, One("AdminOnly"), ExactMatch: true),
        new("/notifications/send", "ارسال اعلان", Icons.SendNotification, One(Perm.NotificationsMessageSend)),
        new("/admin/notifications/report", "گزارش اعلان‌ها", Icons.NotificationsReport, One(Perm.AdminNotificationsView)),
    ]);

    // An alternative with no policies is always satisfied (any signed-in person).
    private static readonly string[][] Anyone = [[]];

    /// <summary>The pages a person uses for their own requests. Tabs carry the approvals-inbox and my-requests counters.</summary>
    public static readonly SectionDef MyRequests = new("my-requests", "درخواست‌ها", Icons.RequestsGroup,
    [
        new("/requests/new", "درخواست جدید", Icons.NewRequest, [[Perm.RequestsOwnCreate, "NonAdmin"]]),
        new("/requests/new-for-team", "ثبت برای زیرمجموعه", Icons.NewRequestForOthers, [[Perm.RequestsTeamCreate, "NonAdmin"]]),
        new("/requests/mine", "درخواست‌های من", Icons.MyRequests, [[Perm.RequestsOwnView, "NonAdmin"]], BadgeKey: "mine"),
        new("/requests/inbox", "کارتابل تاییدات", Icons.Inbox, [[Perm.RequestsOwnView, "NonAdmin"]], BadgeKey: "inbox"),
        new("/requests/team", "درخواست‌های زیرمجموعه", Icons.Subordinates, [["Requests.ViewSubordinates", "NonAdmin"]]),
    ]);

    public static readonly SectionDef Account = new("account", "تنظیمات حساب", Icons.Profile,
    [
        new("/my-profile", "پروفایل من", Icons.Profile, One(Perm.ProfileOwnView)),
        new("/settings/appearance", "ظاهر برنامه", Icons.Appearance, Anyone),
        new("/my-positions", "سمت‌های من", Icons.Position, One("NonAdmin")),
    ]);

    public static readonly SectionDef Shop = new("store", "فروشگاه", Icons.Store,
    [
        new("/store", "فروشگاه", Icons.Store, One(Perm.StoreShopView), ExactMatch: true),
        new("/store/orders", "سفارش‌های من", Icons.MyRequests, [[Perm.StoreOrdersCreate, "NonAdmin"]]),
    ]);

    public static readonly SectionDef Content = new("content-admin", "محتوای صفحه اصلی", Icons.Links,
    [
        new("/admin/links", "پیوندهای صفحه اصلی", Icons.Links, One(Perm.AdminLinksView)),
        new("/admin/landing-slides", "اسلایدهای صفحه اصلی", Icons.LandingSlides, One(Perm.AdminLandingSlidesView)),
        new("/guides/manage", "مدیریت راهنماها", Icons.Guide, One(Perm.GuidesWorkspacePolicy)),
    ]);

    public static readonly SectionDef StoreAdmin = new("store-admin", "مدیریت فروشگاه", Icons.Store,
    [
        new("/admin/store/items", "کالاها و گروه‌ها", Icons.Box, One(Perm.StoreItemsView)),
        new("/admin/store/stock", "موجودی و گردش انبار", Icons.Store, One(Perm.StoreStockView)),
        new("/admin/store/issue", "تحویل کالا", Icons.Send, One(Perm.StoreOrdersIssue)),
        new("/admin/store/lottery", "قرعه‌کشی", Icons.Users, One(Perm.StoreOrdersIssue)),
        new("/admin/store/reports", "گزارش‌ها", Icons.AuditLog, One(Perm.StoreReportsView)),
    ]);

    private static readonly SectionDef[] All = [Access, Structure, RequestAdmin, Notifications, MyRequests, Account, Shop, Content, StoreAdmin];

    public static SectionDef? Find(string key) => All.FirstOrDefault(x => x.Key == key);

    /// <summary>The tabs of a section that this person may open, in order. The first one is where the sidebar link leads.</summary>
    public static async Task<IReadOnlyList<SectionTab>> VisibleAsync(IAuthorizationService authorization, ClaimsPrincipal user, SectionDef section)
    {
        var cache = new Dictionary<string, bool>();
        async Task<bool> Passes(string policy)
        {
            if (!cache.TryGetValue(policy, out var ok))
                cache[policy] = ok = (await authorization.AuthorizeAsync(user, resource: null, policyName: policy)).Succeeded;
            return ok;
        }

        var visible = new List<SectionTab>();
        foreach (var tab in section.Tabs)
        {
            foreach (var alternative in tab.Allowed)
            {
                var all = true;
                foreach (var policy in alternative)
                    if (!await Passes(policy)) { all = false; break; }
                if (all) { visible.Add(tab); break; }
            }
        }
        return visible;
    }
}
