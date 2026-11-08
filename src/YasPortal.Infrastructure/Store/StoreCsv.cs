using System.Globalization;
using System.Text;

namespace YasPortal.Infrastructure.Store;

/// <summary>Builds a CSV file that opens correctly in Excel (UTF-8 with a byte-order mark keeps the Persian text intact).</summary>
public static class StoreCsv
{
    public static byte[] Build(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Join(',', headers.Select(Cell)));
        foreach (var row in rows) text.AppendLine(string.Join(',', row.Select(Cell)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray();
    }

    /// <summary>Quotes a cell, and defuses text that a spreadsheet would run as a formula (a leading = + - or @ that is not a plain number).</summary>
    public static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
