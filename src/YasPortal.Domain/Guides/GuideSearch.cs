using YasPortal.Domain.PhoneBook;

namespace YasPortal.Domain.Guides;

/// <summary>
/// The search rule for guides, shared by the reader page and the admin list: a guide matches when every word the person typed
/// is found in its title, its short summary or its group name. Persian and Arabic digits and letters count as the same
/// (the same reading rules as the phone book). An empty search matches every guide.
/// </summary>
public static class GuideSearch
{
    public static bool Matches(string? title, string? summary, string? groupName, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || PhoneBookText.ContainsAllWords(string.Join(' ', new[] { title, summary, groupName }.Where(x => !string.IsNullOrEmpty(x))), query);
}
