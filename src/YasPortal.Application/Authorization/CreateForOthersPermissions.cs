using YasPortal.Domain.Workflows;

namespace YasPortal.Application.Authorization;

/// <summary>
/// One permission per request type for «ثبت درخواست برای دیگران» (an administrator sending a request in another employee's name),
/// so each type can be given out separately. The store order is one of the types.
/// </summary>
public static class CreateForOthersPermissions
{
    public const string Prefix = "Admin.Requests.CreateForOthers.";

    /// <summary>Policy for the page that lists the types: passes when the person holds at least one of the per-type permissions.</summary>
    public const string AnyPolicy = "Admin.Requests.CreateForOthers.Any";

    /// <summary>The permission that the earlier single permission was split into (kept only so the seeder and SQL can move its grants).</summary>
    public const string LegacyCode = "Admin.Requests.CreateForOthers";

    /// <summary>The store-order permission as a constant (an attribute argument must be one).</summary>
    public const string StoreOrder = Prefix + "StoreOrder";

    public static string For(WorkflowTypeCode type) => Prefix + type;

    public static IReadOnlyList<(string Code, string Name)> All { get; } =
        Enum.GetValues<WorkflowTypeCode>().Select(type => (For(type), "ثبت درخواست برای دیگران: " + WorkflowFieldCatalog.GetDisplayName(type))).ToList();

    public static IEnumerable<string> Codes => All.Select(x => x.Code);

    public static bool IsOne(string code) => code.StartsWith(Prefix, StringComparison.Ordinal) && code != AnyPolicy;
}
