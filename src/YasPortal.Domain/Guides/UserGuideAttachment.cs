using YasPortal.Domain.Files;

namespace YasPortal.Domain.Guides;

/// <summary>
/// A file attached to a user guide (a form, a sample, a picture). A guide can have several files; readers who may read the guide
/// can download them. Like the other attachments of the portal, the file is kept in the database in its own table so listing guides
/// never loads file contents. Only a short list of document and image types is accepted (no HTML, SVG or scripts, which a browser
/// could run), each file is limited in size, and a guide is limited in the number of files.
/// </summary>
public sealed class UserGuideAttachment
{
    /// <summary>The most files one guide may carry.</summary>
    public const int MaxPerGuide = 10;

    private UserGuideAttachment()
    {
    }

    // Creating an attachment checks the file first and refuses it if it breaks a rule.
    public UserGuideAttachment(Guid guideId, string fileName, byte[] content)
    {
        var error = UploadRules.Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        Id = Guid.NewGuid();
        GuideId = guideId;
        FileName = UploadRules.CleanName(fileName);
        ContentType = UploadRules.ContentTypeFor(FileName);
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

    /// <summary>A Persian reason a guide cannot carry this many files, or null when the number is fine.</summary>
    public static string? ValidateCount(int count) =>
        count > MaxPerGuide ? $"هر راهنما حداکثر {MaxPerGuide} فایل پیوست می‌تواند داشته باشد." : null;

}
