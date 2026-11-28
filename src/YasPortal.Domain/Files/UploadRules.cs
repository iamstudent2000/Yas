namespace YasPortal.Domain.Files;

/// <summary>
/// The one set of rules for every file a person can upload to the portal (guide, slide, gallery picture, link and request attachments):
/// which file types are accepted, how big a file may be and how long its name may be. Only a short list of document and image types is
/// accepted (no HTML, SVG or scripts, which a browser could run). To change a limit, change it here.
/// </summary>
public static class UploadRules
{
    /// <summary>The largest file allowed, in bytes (5 MB).</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The longest file name allowed, in characters.</summary>
    public const int FileNameMax = 200;

    // The allowed file types: the extension (ending of the file name) mapped to its internet content type.
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".txt"] = "text/plain",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint", [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip",
    };

    /// <summary>The accepted extensions, for the file picker.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => Types.Keys;

    /// <summary>A Persian reason the file cannot be accepted (type, name length, empty or too big), or null when it is fine.</summary>
    public static string? Validate(string? fileName, long sizeBytes)
    {
        var name = CleanName(fileName);
        if (name.Length == 0 || !Types.ContainsKey(Path.GetExtension(name)))
            return "نوع فایل مجاز نیست. فایل‌های PDF، تصویر، Word، Excel، PowerPoint، متن و ZIP پذیرفته می‌شود.";
        if (name.Length > FileNameMax)
            return $"نام فایل باید حداکثر {FileNameMax} نویسه باشد.";
        if (sizeBytes <= 0)
            return "فایل خالی است.";
        if (sizeBytes > MaxBytes)
            return $"حجم فایل باید حداکثر {MaxBytes / (1024 * 1024)} مگابایت باشد.";
        return null;
    }

    /// <summary>The internet content type of an allowed file, found from the ending of its name (for example .pdf).</summary>
    public static string ContentTypeFor(string fileName) => Types[Path.GetExtension(CleanName(fileName))];

    /// <summary>The file name without any folder part or control characters (what a browser may send as a path).</summary>
    public static string CleanName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return new string(name.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
    }
}
