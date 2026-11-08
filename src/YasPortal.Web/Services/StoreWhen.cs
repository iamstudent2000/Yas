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
        var t = TimeSpan.TryParseExact((time ?? "").Trim(), @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
        return d.Date + t;
    }
}
