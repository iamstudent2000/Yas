using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace YasPortal.Web.Services;

/// <summary>
/// Turns any list (a title, column headings and rows of text) into a PDF: A4 landscape, right-to-left, Vazirmatn font,
/// headings repeated on every page and page numbers in the footer. The rows are the same text as in the CSV file, unchanged
/// (digits are not converted, so codes, user names and numbers read exactly as they do in the CSV).
/// </summary>
public static class ListPdf
{
    private const string FontFamily = "Vazirmatn";

    /// <param name="columnWeights">How wide each column is compared with the others; when empty the width follows how long the text in the column usually is.</param>
    public static byte[] Render(string title, string? subtitle, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlyList<float>? columnWeights = null)
    {
        RequestPdfService.EnsureInitialized();   // registers the license and the Persian fonts once
        var weights = columnWeights is { Count: > 0 } && columnWeights.Count == headers.Count ? columnWeights : AutoWeights(headers, rows);
        // many columns need smaller text to fit on the page
        var fontSize = headers.Count <= 7 ? 9f : headers.Count <= 10 ? 8f : 7f;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(fontSize).LineHeight(1.35f));

            page.Header().PaddingBottom(6).Column(column =>
            {
                column.Item().Text(title).Bold().FontSize(14);
                if (!string.IsNullOrWhiteSpace(subtitle)) column.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken2);
                column.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Darken1);
            });

            page.Content().PaddingVertical(4).Table(table =>
            {
                table.ColumnsDefinition(columns => { foreach (var w in weights) columns.RelativeColumn(w); });
                table.Header(header =>
                {
                    foreach (var h in headers)
                        header.Cell().Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(h).Bold();
                });
                foreach (var row in rows)
                    for (var i = 0; i < headers.Count; i++)   // a short row is padded so the table never loses its shape
                        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(i < row.Count ? row[i] : "");
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

    /// <summary>A column is as wide as its text usually is (looking at the first 200 rows), but never narrower than a short word or wider than a long sentence.</summary>
    private static float[] AutoWeights(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var sample = rows.Take(200).ToList();
        return headers.Select((h, i) =>
        {
            var typical = sample.Count == 0 ? 0 : (int)sample.Average(r => i < r.Count ? Math.Min(r[i].Length, 80) : 0);
            return Math.Clamp(Math.Max(h.Length, typical) / 9f, 1f, 6f);
        }).ToArray();
    }
}
