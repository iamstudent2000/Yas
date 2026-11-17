using YasPortal.Domain.Workflows;

namespace YasPortal.Application.Authorization;

// Business meaning: an administrator may register a request on behalf of another employee.
// Instead of one all-or-nothing permission, there is one permission for each request type (leave, store order, and so on).
/// <summary>
/// One permission per request type for «ثبت درخواست برای دیگران» (an administrator sending a request in another employee's name),
/// so each type can be given out separately. The store order is one of the types.
/// </summary>
public static class CreateForOthersPermissions
{
    // Every per-type permission code starts with this text; the request type name is added at the end.
    public const string Prefix = "Admin.Requests.CreateForOthers.";

    /// <summary>Policy for the page that lists the types: passes when the person holds at least one of the per-type permissions.</summary>
    public const string AnyPolicy = "Admin.Requests.CreateForOthers.Any";

    /// <summary>The permission that the earlier single permission was split into (kept only so the seeder and SQL can move its grants).</summary>
    public const string LegacyCode = "Admin.Requests.CreateForOthers";

    /// <summary>The store-order permission as a constant (an attribute argument must be one).</summary>
    public const string StoreOrder = Prefix + "StoreOrder";

    // Builds the permission code for one request type.
    public static string For(WorkflowTypeCode type) => Prefix + type;

    // The full list of per-type permissions (code plus display name), generated from every known request type.
    public static IReadOnlyList<(string Code, string Name)> All { get; } =
        Enum.GetValues<WorkflowTypeCode>().Select(type => (For(type), "ثبت درخواست برای دیگران: " + WorkflowFieldCatalog.GetDisplayName(type))).ToList();

    public static IEnumerable<string> Codes => All.Select(x => x.Code);

    // True when the code is one of the per-type permissions (and not the "any of them" page policy).
    public static bool IsOne(string code) => code.StartsWith(Prefix, StringComparison.Ordinal) && code != AnyPolicy;
}
