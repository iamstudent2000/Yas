using YasPortal.Domain.Workflows;

namespace YasPortal.Application.Authorization;

// Business meaning: each request type has a printable form, and printing it is controlled by its own permission.
// The permission codes are generated from the list of request types, so a new type automatically gets a print permission.
/// <summary>One permission per request type's printable form («فرم چاپی»), so each form can be given out separately.</summary>
public static class FormPermissions
{
    // Builds the print permission code for one request type, for example "Forms.&lt;Type&gt;.Print".
    public static string For(WorkflowTypeCode type) => "Forms." + type + ".Print";

    /// <summary>Every form permission with its Persian name, for policies and seeding.</summary>
    // OldCode is the earlier code of the same permission, so existing grants can be renamed instead of lost.
    public static IReadOnlyList<(string Code, string Name, string OldCode)> All { get; } =
        Enum.GetValues<WorkflowTypeCode>().Select(type => (For(type), "چاپ فرم " + WorkflowFieldCatalog.GetDisplayName(type), "Forms." + type)).ToList();

    public static IEnumerable<string> Codes => All.Select(x => x.Code);
}
