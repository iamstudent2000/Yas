namespace YasPortal.Application.Authorization;

/// <summary>Which statistics blocks of the dashboard a person may see. «Dashboard.View» still opens the page itself.</summary>
public static class DashboardPermissions
{
    public const string Mine = "Dashboard.Mine";
    public const string Team = "Dashboard.Team";
    public const string System = "Dashboard.System";

    public static readonly IReadOnlyList<(string Code, string Name)> All =
    [
        (Mine, "آمار درخواست‌های من در داشبورد"),
        (Team, "آمار درخواست‌های زیرمجموعه در داشبورد"),
        (System, "آمار کل سامانه در داشبورد"),
    ];
}
