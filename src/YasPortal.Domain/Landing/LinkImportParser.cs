namespace YasPortal.Domain.Landing;

/// <summary>One line of a pasted list of links, checked.</summary>
public sealed record ImportLine(int LineNumber, IReadOnlyList<string> GroupPath, string Title, string? Url, string? Description, string? Error);

/// <summary>
/// Reads a pasted list, one link per line: <c>group &gt; subgroup | title | address | description (optional)</c>.
/// The group part may be empty (the link is then in no group) and may have up to <see cref="LinkGroup.MaxDepth"/> levels separated by ">".
/// Blank lines and lines starting with # are skipped. Nothing is saved here.
/// </summary>
public static class LinkImportParser
{
    public static IReadOnlyList<ImportLine> Parse(string? text)
    {
        var result = new List<ImportLine>();
        var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith('#'))
                continue;
            var parts = raw.Split('|').Select(x => x.Trim()).ToArray();
            var number = i + 1;
            if (parts.Length < 3)
            {
                result.Add(new ImportLine(number, [], raw, null, null, "هر خط باید «گروه | عنوان | آدرس» باشد."));
                continue;
            }
            var path = parts[0].Split('>').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var title = parts[1];
            var url = PortalLink.NormalizeUrl(parts[2]);
            var description = parts.Length > 3 && parts[3].Length > 0 ? string.Join(" | ", parts.Skip(3)).Trim() : null;
            string? error = null;
            if (path.Count > LinkGroup.MaxDepth) error = $"گروه‌ها حداکثر {LinkGroup.MaxDepth} سطح می‌توانند باشند.";
            else if (path.Any(x => x.Length > LinkGroup.NameMax)) error = "نام گروه خیلی بلند است.";
            else if (title.Length == 0 || title.Length > PortalLink.TitleMax) error = "عنوان لازم است و نباید خیلی بلند باشد.";
            else if (url is null) error = "آدرس معتبر نیست.";
            else if (description is { Length: > PortalLink.DescriptionMax }) error = "توضیح خیلی بلند است.";
            result.Add(new ImportLine(number, path, title, url, description, error));
        }
        return result;
    }
}
