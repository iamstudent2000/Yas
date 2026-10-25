using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using YasPortal.Domain.Workflows;

namespace YasPortal.Web.Services;

/// <summary>
/// The printable form of one request type. The page header (logo, organization, title) and the footer (page numbers)
/// are drawn for every form by <see cref="RequestPdfService"/>; a design lays out what is between them from the
/// <see cref="RequestReportModel"/> (use <see cref="RequestReportModel.Values"/> by field key, <see cref="RequestReportModel.PartFillers"/>,
/// <see cref="RequestReportModel.Signers"/>). Add a class per type and register it in Program.cs:
/// <c>builder.Services.AddScoped&lt;IRequestFormDesign, MyTypeFormDesign&gt;()</c>.
/// </summary>
public interface IRequestFormDesign
{
    WorkflowTypeCode Type { get; }
    /// <summary>Printed under the organization name in the header.</summary>
    string Title { get; }
    void Compose(IContainer container, RequestReportModel model);
}

/// <summary>Layout pieces shared by the form designs.</summary>
internal static class FormParts
{
    private static readonly string Border = Colors.Grey.Lighten1;

    /// <summary>A bordered block with a shaded title bar.</summary>
    public static void Box(IContainer container, string title, Action<ColumnDescriptor> content) =>
        container.Border(0.7f).BorderColor(Border).Column(column =>
        {
            column.Item().Element(c => RequestPdfService.SectionTitle(c, title));
            column.Item().Padding(6).Column(content);
        });

    /// <summary>A label above a (possibly long) value in a box that is at least <paramref name="minHeight"/> tall.</summary>
    public static void TextBlock(IContainer container, string label, string? value, float minHeight = 40) =>
        container.Border(0.5f).BorderColor(Border).Padding(4).MinHeight(minHeight).Column(cell =>
        {
            cell.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken2);
            cell.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).Bold();
        });

    /// <summary>"ثبت‌شده توسط: name — position — date" for a part filled by an approver, or a dash.</summary>
    public static void FilledBy(IContainer container, ReportSigner? signer) =>
        container.PaddingTop(2).Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(Colors.Grey.Darken2));
            text.Span("ثبت‌شده توسط: ");
            text.Span(signer is null ? "—" : $"{signer.Person} — {signer.Position}" + (signer.Date.Length > 0 ? " — " + signer.Date : "")).Bold();
        });

    /// <summary>The request number on one side and the «تایید نهایی» stamp with its date on the other.</summary>
    public static void Stamp(IContainer container, RequestReportModel model) =>
        container.Row(row =>
        {
            row.RelativeItem().AlignMiddle().Text(text =>
            {
                text.Span("شماره درخواست: ").FontColor(Colors.Grey.Darken2);
                text.Span("#" + model.RequestNumber).Bold();
            });
            row.AutoItem().Border(1.2f).BorderColor(Colors.Green.Darken2).PaddingVertical(3).PaddingHorizontal(10).Column(stamp =>
            {
                stamp.Item().AlignCenter().Text("تایید نهایی").Bold().FontSize(11).FontColor(Colors.Green.Darken2);
                if (model.FinalDate.Length > 0)
                    stamp.Item().AlignCenter().Text(model.FinalDate).FontSize(8).FontColor(Colors.Green.Darken2);
            });
        });
}

/// <summary>The form of «درخواست خودرو»: trip details (requester), vehicle assignment and security gate pass (approvers), then signatures.</summary>
public sealed class VehicleRequestFormDesign : IRequestFormDesign
{
    public WorkflowTypeCode Type => WorkflowTypeCode.VehicleRequest;
    public string Title => "فرم درخواست خودرو";

    public void Compose(IContainer container, RequestReportModel model)
    {
        string V(string key) => model.Values.GetValueOrDefault(key, "—");
        ReportSigner? Filler(int part) => model.PartFillers.GetValueOrDefault(part);

        container.Column(column =>
        {
            column.Spacing(10);
            column.Item().Element(c => FormParts.Stamp(c, model));

            column.Item().Element(c => FormParts.Box(c, "مشخصات درخواست‌کننده", box =>
                box.Item().Element(g => RequestPdfService.KeyValueGrid(g, model.Summary.Take(3).ToList(), columns: 3))));

            column.Item().Element(c => FormParts.Box(c, "بخش ۱ — اطلاعات سفر", box =>
            {
                box.Item().Element(g => RequestPdfService.KeyValueGrid(g,
                [
                    new ReportField("مقصد", V("destination")),
                    new ReportField("تاریخ رفت", V("tripDate")),
                    new ReportField("تاریخ برگشت", V("returnDate")),
                    new ReportField("تعداد سرنشین", V("passengers")),
                ], columns: 4));
                box.Item().PaddingTop(4).Element(t => FormParts.TextBlock(t, "هدف از سفر", V("purpose"), 46));
            }));

            column.Item().Element(c => FormParts.Box(c, "بخش ۲ — تخصیص خودرو", box =>
            {
                box.Item().Element(g => RequestPdfService.KeyValueGrid(g,
                [
                    new ReportField("نوع خودرو", V("vehicleType")),
                    new ReportField("شماره پلاک", V("plateNumber")),
                    new ReportField("نام راننده", V("driverName")),
                ], columns: 3));
                box.Item().Element(f => FormParts.FilledBy(f, Filler(2)));
            }));

            column.Item().Element(c => FormParts.Box(c, "بخش ۳ — مجوز خروج (حراست)", box =>
            {
                box.Item().Element(g => RequestPdfService.KeyValueGrid(g, [new ReportField("شماره مجوز خروج", V("gatePassNumber"))], columns: 3));
                box.Item().PaddingTop(4).Element(t => FormParts.TextBlock(t, "ملاحظات حراست", model.Values.GetValueOrDefault("securityNote"), 36));
                box.Item().Element(f => FormParts.FilledBy(f, Filler(3)));
            }));

            column.Item().Element(c => RequestPdfService.RenderSignatures(c, model));
        });
    }
}
