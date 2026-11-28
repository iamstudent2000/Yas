using YasPortal.Domain.Files;
using YasPortal.Domain.Guides;

namespace YasPortal.Domain.Landing;

/// <summary>
/// A file uploaded for a home-page link, so the link downloads that file instead of opening a page. A link has at most one file;
/// it is stored under the link's own id and removed together with the link. The same file types and size limit as guide attachments apply.
/// </summary>
public sealed class PortalLinkFile
{
    private PortalLinkFile()
    {
    }

    public PortalLinkFile(Guid linkId, string fileName, byte[] content)
    {
        LinkId = linkId;
        Replace(fileName, content);
    }

    public Guid LinkId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public byte[] Content { get; private set; } = [];

    // Puts a new file in place of the old one. The file is checked first and refused when it breaks a rule (type, empty, too big).
    public void Replace(string fileName, byte[] content)
    {
        var error = UploadRules.Validate(fileName, content?.LongLength ?? 0);
        if (error is not null) throw new ArgumentException(error, nameof(fileName));
        FileName = UploadRules.CleanName(fileName);
        ContentType = UploadRules.ContentTypeFor(FileName);
        Content = content!;
    }

    /// <summary>The address a link uses to download its file.</summary>
    public static string UrlFor(Guid linkId) => "/links/files/" + linkId;

    /// <summary>True when the link address is the download address of an uploaded file.</summary>
    public static bool IsFileUrl(string? url) => url is not null && url.StartsWith("/links/files/", StringComparison.Ordinal);
}
