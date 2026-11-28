using YasPortal.Domain.Files;

namespace YasPortal.Domain.Landing;

// A slide may carry one downloadable file. The shared upload rules (UploadRules) keep it safe: only listed file types, a size
// limit, and a cleaned file name.
/// <summary>
/// The optional file attached to a landing slide (a notice as PDF, a form, a picture). Visitors can download it while the slide is shown.
/// It is kept in the database in its own table so listing the slides never loads file contents. Only a short list of document and
/// image types is accepted (no HTML, SVG or scripts, which a browser could run), and each file is limited in size.
/// </summary>
public sealed class HomeSlideAttachment
{
    private HomeSlideAttachment()
    {
    }

    // Creating an attachment checks the file first and refuses it if it breaks a rule.
    public HomeSlideAttachment(Guid slideId, string fileName, byte[] content)
    {
        var error = UploadRules.Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        SlideId = slideId;
        Replace(fileName, content ?? []);
    }

    /// <summary>Swaps the stored file for another one (same checks as when attaching).</summary>
    public void Replace(string fileName, byte[] content)
    {
        var error = UploadRules.Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));
        FileName = UploadRules.CleanName(fileName);
        ContentType = UploadRules.ContentTypeFor(FileName);
        Content = content!;
        SizeBytes = content!.LongLength;
        UploadedAtUtc = DateTime.UtcNow;
    }

    // Stored details of the file: name, type, size, the file bytes themselves, and when it was uploaded.
    /// <summary>One file per slide: the slide's id is the key.</summary>
    public Guid SlideId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];
    public DateTime UploadedAtUtc { get; private set; }

}
