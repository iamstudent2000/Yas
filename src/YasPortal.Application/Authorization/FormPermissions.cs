using YasPortal.Domain.Workflows;

namespace YasPortal.Application.Authorization;

/// <summary>One permission per request type's printable form («فرم چاپی»), so each form can be given out separately.</summary>
public static class FormPermissions
{
    public static string For(WorkflowTypeCode type) => "Forms." + type + ".Print";

    /// <summary>Every form permission with its Persian name, for policies and seeding.</summary>
    public static IReadOnlyList<(string Code, string Name, string OldCode)> All { get; } =
        Enum.GetValues<WorkflowTypeCode>().Select(type => (For(type), "چاپ فرم " + WorkflowFieldCatalog.GetDisplayName(type), "Forms." + type)).ToList();

    public static IEnumerable<string> Codes => All.Select(x => x.Code);
}
