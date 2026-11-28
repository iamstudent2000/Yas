using YasPortal.Domain.Files;
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
    public GalleryImage(string? title, string? caption, int sortOrder, string fileName, long sizeBytes, Guid? groupId = null)
    {
        var error = ValidateFile(fileName, sizeBytes);
        if (error is not null) throw new ArgumentException(error, nameof(fileName));
        Id = Guid.NewGuid();
        SetFile(fileName, sizeBytes);
        CreatedAtUtc = DateTime.UtcNow;
        Update(string.IsNullOrWhiteSpace(title) ? TitleFromFileName(FileName) : title, caption, sortOrder, groupId);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Caption { get; private set; }
    public int SortOrder { get; private set; }
    /// <summary>The group the picture is listed under on the gallery page (see <see cref="GalleryGroup"/>); empty = no group.</summary>
    public Guid? GroupId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "image/png";
    public long SizeBytes { get; private set; }
    /// <summary>Goes up by one each time the picture is replaced. It is part of the picture's address, so browsers do not show the old picture from their memory.</summary>
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    // Takes over the details of a file (already checked by the caller).
    private void SetFile(string fileName, long sizeBytes)
    {
        FileName = UploadRules.CleanName(fileName);
        ContentType = UploadRules.ContentTypeFor(FileName);
        SizeBytes = sizeBytes;
    }

    /// <summary>Puts another picture in place of this one (title, caption and order stay). The file is checked first and refused when it breaks a rule.</summary>
    public void ReplaceFile(string fileName, long sizeBytes)
    {
        var error = ValidateFile(fileName, sizeBytes);
        if (error is not null) throw new ArgumentException(error, nameof(fileName));
        SetFile(fileName, sizeBytes);
        Version++;
    }

    // Used when editing. The title is required; a blank caption means no caption; an empty group id means no group.
    public void Update(string title, string? caption, int sortOrder, Guid? groupId = null)
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
        GroupId = groupId == Guid.Empty ? null : groupId;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>A Persian reason the file cannot be a gallery picture, or null when it is fine. Besides the picture types, it uses the guide attachment rules (name length, empty, size).</summary>
    public static string? ValidateFile(string? fileName, long sizeBytes)
    {
        var name = UploadRules.CleanName(fileName);
        if (!Extensions.Contains(Path.GetExtension(name)))
            return "نوع فایل مجاز نیست. فقط تصویر PNG، JPG، GIF یا WebP پذیرفته می‌شود.";
        return UploadRules.Validate(name, sizeBytes);
    }

    /// <summary>A Persian reason that this many pictures cannot be uploaded at once, or null when the number is fine.</summary>
    public static string? ValidateCount(int count) =>
        count > MaxPerUpload ? $"در هر بار بارگذاری حداکثر {MaxPerUpload} تصویر مجاز است." : null;

    /// <summary>The title taken from a file name: the name without its ending, cut to the title limit.</summary>
    public static string TitleFromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(UploadRules.CleanName(fileName)).Trim();
        return stem.Length > TitleMax ? stem[..TitleMax] : stem;
    }

    /// <summary>The address that serves the picture itself. The version changes when the picture is replaced, so the new picture is fetched.</summary>
    public static string UrlFor(Guid id, int version) => "/gallery/images/" + id + "?v=" + version;

    /// <summary>The address that downloads the picture as a file (with its own file name) instead of showing it.</summary>
    public static string DownloadUrlFor(Guid id) => "/gallery/images/" + id + "?download=1";

    /// <summary>The search rule: every word typed must be found in the title, the caption, the file name or the group name. Persian and Arabic digits and letters count as the same. An empty search matches everything.</summary>
    public static bool Matches(string? title, string? caption, string? fileName, string? groupName, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || PhoneBookText.ContainsAllWords(string.Join(' ', new[] { title, caption, fileName, groupName }.Where(x => !string.IsNullOrEmpty(x))), query);
}
