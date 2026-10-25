namespace YasPortal.Web.Services;

/// <summary>A label with its value, as printed in the summary boxes.</summary>
public sealed record ReportField(string Label, string Value);

/// <summary>A titled block: label/value boxes and/or free lines of text.</summary>
public sealed class ReportSection(string title)
{
    public string Title { get; set; } = title;
    public List<ReportField> Fields { get; } = [];
    public List<string> Lines { get; } = [];
}

/// <summary>One row of the approval-history table.</summary>
public sealed record ReportHistoryRow(string Round, string Step, string Position, string Person, string Result, string Time, string Comment);

/// <summary>One box of the signature block.</summary>
public sealed record ReportSigner(string Role, string Person, string Position, string Result = "", string Date = "");

/// <summary>
/// Everything the request PDF prints, already formatted as text. <see cref="RequestReportBuilder"/> fills it from a request;
/// code (or an <see cref="IRequestReportCustomizer"/>) may change or add anything before it is rendered:
/// the organization name, the title, extra calculated values, more sections, other signers.
/// </summary>
public sealed class RequestReportModel
{
    /// <summary>Printed large in the page header, next to the logo.</summary>
    public string OrganizationName { get; set; } = "";
    /// <summary>The document title under the organization name, e.g. «برگه درخواست».</summary>
    public string Title { get; set; } = "برگه درخواست";
    /// <summary>Top right of the header, e.g. the request type.</summary>
    public string HeaderLine1 { get; set; } = "";
    /// <summary>Under <see cref="HeaderLine1"/>, e.g. «شناسه: #ab12cd34».</summary>
    public string HeaderLine2 { get; set; } = "";
    /// <summary>PNG/JPEG bytes of the logo; when empty <see cref="LogoSvg"/> is used.</summary>
    public byte[]? LogoImage { get; set; }
    public string? LogoSvg { get; set; }

    public string PrintedBy { get; set; } = "";
    public DateTime PrintedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>The downloaded file's name without extension.</summary>
    public string FileName { get; set; } = "request";

    /// <summary>The first box: requester, position, status, dates.</summary>
    public List<ReportField> Summary { get; } = [];
    /// <summary>Printed after the summary: the form data (one section per part) and anything you add.</summary>
    public List<ReportSection> Sections { get; } = [];
    /// <summary>Computed figures (durations, counts, totals); printed as a box after the sections. Empty = not printed.</summary>
    public List<ReportField> Calculated { get; } = [];
    public string CalculatedTitle { get; set; } = "محاسبات و آمار";
    public List<ReportHistoryRow> History { get; } = [];
    /// <summary>Printed after the history table (field changes, reopenings, your own notes).</summary>
    public List<ReportSection> Appendix { get; } = [];
    /// <summary>The signature boxes; the builder adds the requester and every approver of the current round.</summary>
    public List<ReportSigner> Signers { get; } = [];
    public string SignaturesTitle { get; set; } = "امضاها";

    /// <summary>The form values by field key, already formatted for print (Persian dates and digits). Used by the per-type forms.</summary>
    public Dictionary<string, string> Values { get; } = [];
    /// <summary>For a multi-part request: who filled each part after the first (step name, person, position, date). Missing = not filled.</summary>
    public Dictionary<int, ReportSigner> PartFillers { get; } = [];
    /// <summary>The request's number as printed (the first eight characters of its id).</summary>
    public string RequestNumber { get; set; } = "";
    /// <summary>When the request got its final decision, formatted; empty while it is still open.</summary>
    public string FinalDate { get; set; } = "";
}
