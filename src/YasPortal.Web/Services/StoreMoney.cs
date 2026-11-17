using System.Globalization;

namespace YasPortal.Web.Services;

/// <summary>Shows a store amount (Toman) the same way on every page: thousands separated, Persian digits, followed by «تومان».</summary>
public static class StoreMoney
{
    /// <summary>«50,000» in Latin digits (used inside texts that are stored, such as the request summary the approvers read).</summary>
    public static string Plain(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>«۵۰,۰۰۰ تومان».</summary>
    public static string Format(decimal amount) => WorkflowDisplay.ToPersianDigits(Plain(amount)) + " تومان";

    /// <summary>The line total for the screen, or a dash when the item has no price.</summary>
    public static string FormatOrDash(decimal? amount) => amount is decimal a ? Format(a) : "—";
}
