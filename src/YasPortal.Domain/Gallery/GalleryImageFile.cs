namespace YasPortal.Domain.Gallery;

/// <summary>
/// The bytes of a gallery picture, kept in their own table (one row per picture, removed together with it) so that listing pictures
/// never loads the files.
/// </summary>
public sealed class GalleryImageFile
{
    private GalleryImageFile()
    {
    }

    public GalleryImageFile(Guid imageId, byte[] content)
    {
        ImageId = imageId;
        Content = content;
    }

    public Guid ImageId { get; private set; }
    public byte[] Content { get; private set; } = [];
}
