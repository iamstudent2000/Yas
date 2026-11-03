namespace YasPortal.Domain.Guides;

/// <summary>Reading direction of a guide or of one block.</summary>
public enum GuideDirection
{
    Rtl = 0,
    Ltr = 1,
}

/// <summary>What a block is. A list block shows each line of its text as one bullet.</summary>
public enum GuideBlockKind
{
    Paragraph = 0,
    Heading = 1,
    Subheading = 2,
    List = 3,
    /// <summary>A highlighted box (tip, warning). Its <see cref="GuideBlock.Tone"/> sets the colour.</summary>
    Note = 4,
}

public enum GuideAlign
{
    Start = 0,
    Center = 1,
    End = 2,
    Justify = 3,
}

public enum GuideSize
{
    Normal = 0,
    Small = 1,
    Large = 2,
}

/// <summary>A fixed palette: an administrator picks a tone, never a free colour or CSS.</summary>
public enum GuideTone
{
    Default = 0,
    Accent = 1,
    Success = 2,
    Warning = 3,
    Danger = 4,
    Muted = 5,
}

/// <summary>
/// One block of a user guide: a paragraph, heading, list or note with its own style and direction.
/// Text is stored and shown as plain text (never as HTML), so nothing an administrator types can run as markup.
/// </summary>
public sealed class GuideBlock
{
    public const int TextMax = 4000;

    private GuideBlock()
    {
    }

    public GuideBlock(Guid guideId, int sortOrder, GuideBlockKind kind, string text, GuideDirection? direction, GuideAlign align,
        GuideSize size, GuideTone tone, bool bold, bool italic)
    {
        Id = Guid.NewGuid();
        GuideId = guideId;
        SortOrder = sortOrder;
        Set(kind, text, direction, align, size, tone, bold, italic);
    }

    public Guid Id { get; private set; }
    public Guid GuideId { get; private set; }
    public int SortOrder { get; private set; }
    public GuideBlockKind Kind { get; private set; }
    public string Text { get; private set; } = "";
    /// <summary>Empty = the direction of the guide.</summary>
    public GuideDirection? Direction { get; private set; }
    public GuideAlign Align { get; private set; }
    public GuideSize Size { get; private set; }
    public GuideTone Tone { get; private set; }
    public bool Bold { get; private set; }
    public bool Italic { get; private set; }

    public void Set(GuideBlockKind kind, string text, GuideDirection? direction, GuideAlign align, GuideSize size, GuideTone tone, bool bold, bool italic)
    {
        text = (text ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0 || text.Length > TextMax)
            throw new ArgumentException($"Block text is required and at most {TextMax} characters.", nameof(text));
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown block kind.", nameof(kind));
        if (direction is GuideDirection d && !Enum.IsDefined(d)) throw new ArgumentException("Unknown direction.", nameof(direction));
        if (!Enum.IsDefined(align)) throw new ArgumentException("Unknown alignment.", nameof(align));
        if (!Enum.IsDefined(size)) throw new ArgumentException("Unknown size.", nameof(size));
        if (!Enum.IsDefined(tone)) throw new ArgumentException("Unknown tone.", nameof(tone));

        Kind = kind;
        Text = text;
        Direction = direction;
        Align = align;
        Size = size;
        Tone = tone;
        Bold = bold;
        Italic = italic;
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    /// <summary>The non-empty lines of a list block's text.</summary>
    public IReadOnlyList<string> Lines() =>
        Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
