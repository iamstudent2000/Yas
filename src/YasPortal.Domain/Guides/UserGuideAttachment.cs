namespace YasPortal.Domain.Guides;

/// <summary>
/// A file attached to a user guide (a form, a sample, a picture). A guide can have several files; readers who may read the guide
/// can download them. Like the other attachments of the portal, the file is kept in the database in its own table so listing guides
/// never loads file contents. Only a short list of document and image types is accepted (no HTML, SVG or scripts, which a browser
/// could run), each file is limited in size, and a guide is limited in the number of files.
/// </summary>
public sealed class UserGuideAttachment
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int FileNameMax = 200;
    /// <summary>The most files one guide may carry.</summary>
    public const int MaxPerGuide = 10;

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

    private UserGuideAttachment()
    {
    }

    // Creating an attachment checks the file first and refuses it if it breaks a rule.
    public UserGuideAttachment(Guid guideId, string fileName, byte[] content)
    {
        var error = Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        Id = Guid.NewGuid();
        GuideId = guideId;
        FileName = CleanName(fileName);
        ContentType = Types[Path.GetExtension(FileName)];
        Content = content!;
        SizeBytes = content!.LongLength;
        UploadedAtUtc = DateTime.UtcNow;
    }

    // Stored details of the file: its own id, the guide it belongs to, name, type, size, the file bytes and when it was uploaded.
    public Guid Id { get; private set; }
    public Guid GuideId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];
    public DateTime UploadedAtUtc { get; private set; }

    /// <summary>A Persian reason the file cannot be attached, or null when it is fine.</summary>
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

    /// <summary>A Persian reason a guide cannot carry this many files, or null when the number is fine.</summary>
    public static string? ValidateCount(int count) =>
        count > MaxPerGuide ? $"هر راهنما حداکثر {MaxPerGuide} فایل پیوست می‌تواند داشته باشد." : null;

    /// <summary>The file name without any folder part or control characters (what a browser may send as a path).</summary>
    public static string CleanName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return new string(name.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
    }
}
