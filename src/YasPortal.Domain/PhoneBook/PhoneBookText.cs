using System.Text;

namespace YasPortal.Domain.PhoneBook;

// Search helpers for the phone book. People type Persian or Arabic digits and letters in mixed ways, so
// text is first made uniform before comparing.
/// <summary>Makes text comparable for searching: Persian and Arabic digits become Latin digits and the Arabic ي and ك become ی and ک.</summary>
public static class PhoneBookText
{
    // Makes digits and look-alike letters uniform, and turns the half-space into a normal space.
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '‌' => ' ', // zero-width non-joiner: «می‌خواهم» is found by «می خواهم»
                _ => c,
            });
        }
        return sb.ToString();
    }

    // A search matches when every word typed is found somewhere in the text, in any order.
    /// <summary>True when every word of <paramref name="query"/> appears somewhere in <paramref name="haystack"/> (both normalized, case-insensitive).</summary>
    public static bool ContainsAllWords(string? haystack, string? query)
    {
        var words = Normalize(query).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return true;
        var text = Normalize(haystack);
        return words.All(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}
