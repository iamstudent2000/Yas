using YasPortal.Domain.Guides;
using YasPortal.Domain.PhoneBook;

namespace YasPortal.Domain.Gallery;

/// <summary>
/// One picture of the public image gallery: its title, an optional caption, its place in the order, whether it is shown, and the
/// details of the file. The picture bytes live in <see cref="GalleryImageFile"/> (another table), so lists never load them.
/// Only png, jpg, gif and webp pictures are accepted (never SVG or HTML, which a browser could run), and the size limit is the same
/// as for guide attachments.
/// </summary>
public sealed class GalleryImage
{
    // Length limits for the title and the caption, and how many pictures may be uploaded in one go.
    public const int TitleMax = 150, CaptionMax = 300, MaxPerUpload = 10;

    // The picture types that are accepted (the ending of the file name).
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    /// <summary>The accepted endings, for the file picker.</summary>
    public static IReadOnlyCollection<string> AllowedExtensions => Extensions;

    private GalleryImage()
    {
    }

    // A new picture. When no title is given, the file name (without its ending) becomes the title.
    public GalleryImage(string? title, string? caption, int sortOrder, string fileName, long sizeBytes)
    {
        var error = ValidateFile(fileName, sizeBytes);
        if (error is not null) throw new ArgumentException(error, nameof(fileName));
        Id = Guid.NewGuid();
        FileName = UserGuideAttachment.CleanName(fileName);
        ContentType = UserGuideAttachment.ContentTypeFor(FileName);
        SizeBytes = sizeBytes;
        CreatedAtUtc = DateTime.UtcNow;
        Update(string.IsNullOrWhiteSpace(title) ? TitleFromFileName(FileName) : title, caption, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Caption { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "image/png";
    public long SizeBytes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Used when editing. The title is required; a blank caption means no caption.
    public void Update(string title, string? caption, int sortOrder)
    {
        title = (title ?? "").Trim();
        caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        if (caption is { Length: > CaptionMax })
            throw new ArgumentException($"Caption is at most {CaptionMax} characters.", nameof(caption));
        Title = title;
        Caption = caption;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>A Persian reason the file cannot be a gallery picture, or null when it is fine. Besides the picture types, it uses the guide attachment rules (name length, empty, size).</summary>
    public static string? ValidateFile(string? fileName, long sizeBytes)
    {
        var name = UserGuideAttachment.CleanName(fileName);
        if (!Extensions.Contains(Path.GetExtension(name)))
            return "نوع فایل مجاز نیست. فقط تصویر PNG، JPG، GIF یا WebP پذیرفته می‌شود.";
        return UserGuideAttachment.Validate(name, sizeBytes);
    }

    /// <summary>A Persian reason that this many pictures cannot be uploaded at once, or null when the number is fine.</summary>
    public static string? ValidateCount(int count) =>
        count > MaxPerUpload ? $"در هر بار بارگذاری حداکثر {MaxPerUpload} تصویر مجاز است." : null;

    /// <summary>The title taken from a file name: the name without its ending, cut to the title limit.</summary>
    public static string TitleFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(UserGuideAttachment.CleanName(fileName)).Trim();
        return stem.Length > TitleMax ? stem[..TitleMax] : stem;
    }

    /// <summary>The address that serves the picture itself.</summary>
    public static string UrlFor(Guid id) => "/gallery/images/" + id;

    /// <summary>The search rule: every word typed must be found in the title, the caption or the file name. Persian and Arabic digits and letters count as the same. An empty search matches everything.</summary>
    public static bool Matches(string? title, string? caption, string? fileName, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || PhoneBookText.ContainsAllWords(string.Join(' ', new[] { title, caption, fileName }.Where(x => !string.IsNullOrEmpty(x))), query);
}
