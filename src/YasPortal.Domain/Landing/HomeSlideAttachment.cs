namespace YasPortal.Domain.Landing;

/// <summary>
/// The optional file attached to a landing slide (a notice as PDF, a form, a picture). Visitors can download it while the slide is shown.
/// It is kept in the database in its own table so listing the slides never loads file contents. Only a short list of document and
/// image types is accepted (no HTML, SVG or scripts, which a browser could run), and each file is limited in size.
/// </summary>
public sealed class HomeSlideAttachment
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int FileNameMax = 200;

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

    private HomeSlideAttachment()
    {
    }

    public HomeSlideAttachment(Guid slideId, string fileName, byte[] content)
    {
        var error = Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        SlideId = slideId;
        Replace(fileName, content);
    }

    /// <summary>Swaps the stored file for another one (same checks as when attaching).</summary>
    public void Replace(string fileName, byte[] content)
    {
        var error = Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        FileName = CleanName(fileName);
        ContentType = Types[Path.GetExtension(FileName)];
        Content = content!;
        SizeBytes = content!.LongLength;
        UploadedAtUtc = DateTime.UtcNow;
    }

    /// <summary>One file per slide: the slide's id is the key.</summary>
    public Guid SlideId { get; private set; }
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

    /// <summary>The file name without any folder part or control characters (what a browser may send as a path).</summary>
    public static string CleanName(string? fileName)
    {
        var name = (fileName ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return new string(name.Where(c => !char.IsControl(c) && c != '"').ToArray()).Trim();
    }
}
