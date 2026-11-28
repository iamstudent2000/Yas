namespace YasPortal.Infrastructure.Landing;

/// <summary>One slide of the public landing page.</summary>
public sealed class LandingSlide
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>A Tabler icon class such as <c>ti-building</c>.</summary>
    public string Icon { get; set; } = "ti-sparkles";
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    /// <summary>The slide's attached file, when it has one (served from <c>/landing/files/{slide id}</c>).</summary>
    public Guid? FileSlideId { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
}
