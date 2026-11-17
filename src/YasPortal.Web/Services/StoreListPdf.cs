using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace YasPortal.Web.Services;

/// <summary>
/// Turns a store list (a title, column headings and rows of text) into a PDF: A4 landscape, right-to-left, Vazirmatn font,
/// the headings repeated on every page and page numbers in the footer. The same rows that go into the CSV go into the PDF.
/// </summary>
public static class StoreListPdf
{
    private const string FontFamily = "Vazirmatn";

    /// <param name="columnWeights">How wide each column is compared with the others (the items column is usually the widest).</param>
    public static byte[] Render(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<float> columnWeights, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        RequestPdfService.EnsureInitialized();   // registers the license and the Persian fonts once
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(9).LineHeight(1.35f));

            page.Header().PaddingBottom(6).Column(column =>
            {
                column.Item().Text(title).Bold().FontSize(14);
                if (!string.IsNullOrWhiteSpace(subtitle)) column.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(4).Table(table =>
            {
                table.ColumnsDefinition(columns => { foreach (var w in columnWeights) columns.RelativeColumn(w); });
                table.Header(header =>
                {
                    foreach (var h in headers)
                        header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(h).Bold();
                });
                foreach (var row in rows)
                    foreach (var cell in row)
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4).Text(WorkflowDisplay.ToPersianDigits(cell));
            });

            page.Footer().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"چاپ‌شده در {WorkflowDisplay.ToPersianDateTime(DateTime.UtcNow)} · {WorkflowDisplay.ToPersianDigits(rows.Count.ToString())} ردیف")
                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                row.RelativeItem().AlignLeft().Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken2));
                    text.Span("صفحه ");
                    text.CurrentPageNumber().Format(n => WorkflowDisplay.ToPersianDigits(n?.ToString() ?? ""));
                    text.Span(" از ");
                    text.TotalPages().Format(n => WorkflowDisplay.ToPersianDigits(n?.ToString() ?? ""));
                });
            });
        })).GeneratePdf();
    }
}
