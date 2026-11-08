namespace YasPortal.Web.Services;

/// <summary>Shows a store request-period moment (local time, kept to the minute) as a Persian date and clock.</summary>
public static class StoreWhen
{
    public static string Format(DateTime local) =>
        WorkflowDisplay.ToPersianDate(local) + " " + WorkflowDisplay.ToPersianDigits(local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Joins a picked date and an "HH:mm" text; an empty or wrong time falls back to <paramref name="fallback"/>.</summary>
    public static DateTime? Combine(DateTime? date, string? time, TimeSpan fallback)
    {
        if (date is not DateTime d) return null;
        return d.Date + (TryParseTime(time, out var t) ? t : fallback);
    }

    /// <summary>Reads "HH:mm" or "H:mm", also with Persian or Arabic digits; seconds are ignored.</summary>
    public static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        var s = new string((text ?? "").Trim().Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());
        var parts = s.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }
}
