using YasPortal.Domain.Files;

namespace YasPortal.Domain.Workflows;

// Request attachments: files the requester adds when submitting (for example a screenshot or a document).
// The safety limits below exist so nobody can upload something harmful or huge: only a short list of file
// types,
// a maximum size per file and a maximum number of files per request.
/// <summary>
/// A file the requester attached to a request (a screenshot of an error, a document). Kept in the database in its own
/// table so loading a request never loads file contents. Only a short list of document and image types is accepted
/// (no HTML, SVG or scripts, which a browser could run), each file is limited in size, and a request holds a limited
/// number of files.
/// </summary>
public sealed class WorkflowRequestAttachment
{
    // Limits: 3 files per request (the size, name length and file types are in UploadRules).
    public const int MaxFilesPerRequest = 3;

    // Private empty constructor: needed by the database layer to load rows.
    private WorkflowRequestAttachment()
    {
    }

    // Creating an attachment checks the request, the uploader and the file rules first, so an invalid file
    // can never be stored.
    public WorkflowRequestAttachment(Guid requestId, Guid uploadedByEmployeeId, string fileName, byte[] content)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request is required.", nameof(requestId));
        if (uploadedByEmployeeId == Guid.Empty)
            throw new ArgumentException("Uploader is required.", nameof(uploadedByEmployeeId));
        var error = UploadRules.Validate(fileName, content?.LongLength ?? 0);
        if (error is not null)
            throw new ArgumentException(error, nameof(fileName));

        RequestId = requestId;
        UploadedByEmployeeId = uploadedByEmployeeId;
        FileName = UploadRules.CleanName(fileName);
        ContentType = UploadRules.ContentTypeFor(FileName);
        Content = content!;
        SizeBytes = content!.LongLength;
        UploadedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }
    public Guid UploadedByEmployeeId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public long SizeBytes { get; private set; }
    public byte[] Content { get; private set; } = [];
    public DateTime UploadedAtUtc { get; private set; }
}
