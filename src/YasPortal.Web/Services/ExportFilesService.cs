using Microsoft.JSInterop;

namespace YasPortal.Web.Services;

/// <summary>
/// Which file the person asked for. The ⋯ menu of a list sets <see cref="Pdf"/> just before it runs the page's export, so every page
/// can offer a PDF without its own PDF code: the page builds its rows as always and hands them to <see cref="ExportFilesService"/>.
/// </summary>
public sealed class ExportOptions
{
    public bool Pdf { get; set; }
}

/// <summary>
/// The one place a list becomes a downloadable file: a CSV (opens in Excel) or, when the person chose «خروجی PDF», a PDF of the same rows.
/// </summary>
public sealed class ExportFilesService(IJSRuntime js, ExportOptions options, AppState appState)
{
    /// <summary>A PDF holds at most this many rows (a bigger one gets slow and unwieldy); the CSV is the choice for a really long list.</summary>
    public const int PdfMaxRows = 3000;

    /// <summary>A CSV holds at most this many rows; if more match, the first rows are exported and the person is told. Every export of a list uses this one number.</summary>
    public const int CsvMaxRows = 20000;

    /// <summary>True when the export that is running was started with «خروجی PDF».</summary>
    public bool WantsPdf => options.Pdf;

    /// <summary>Creates the file and hands it to the browser. Returns how many rows it holds.</summary>
    /// <param name="title">The heading printed at the top of the PDF.</param>
    /// <param name="fileBaseName">The file name without date and extension.</param>
    /// <param name="pdf">Forces the format: true = PDF, false = CSV. Left empty, the choice made in the ⋯ menu of the list decides (a page with its own two buttons passes it).</param>
    public async Task<int> SaveAsync(string title, string fileBaseName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, string? subtitle = null, bool? pdf = null)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        if (!(pdf ?? options.Pdf))
        {
            await js.InvokeVoidAsync("yasDownloadFile", $"{fileBaseName}-{stamp}.csv", "text/csv;charset=utf-8", ReportExportService.BuildCsv(headers, rows));
            return rows.Count;
        }
        var cut = rows.Count > PdfMaxRows;
        var used = cut ? rows.Take(PdfMaxRows).ToList() : rows;
        await js.InvokeVoidAsync("yasDownloadFile", $"{fileBaseName}-{stamp}.pdf", "application/pdf", ListPdf.Render(title, subtitle, headers, used));
        if (cut) appState.AddToast($"فایل PDF فقط {PdfMaxRows:N0} ردیف اول را دارد؛ برای همهٔ ردیف‌ها «خروجی CSV» بگیرید یا فیلتر را محدودتر کنید.", ToastLevel.Warning);
        return used.Count;
    }
}
