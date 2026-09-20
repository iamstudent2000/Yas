using System.Globalization;
using YasPortal.Domain.Workflows;

namespace YasPortal.Web.Services;

/// <summary>Presentation-only mappings shared by the request pages (icons, labels, badge classes).</summary>
public static class WorkflowDisplay
{
    public static string IconFor(WorkflowTypeCode type) => type switch
    {
        WorkflowTypeCode.Leave => "bx-calendar-x",
        WorkflowTypeCode.Purchase => "bx-cart",
        WorkflowTypeCode.Access => "bx-key",
        WorkflowTypeCode.Loan => "bx-money",
        WorkflowTypeCode.Helpdesk => "bx-support",
        WorkflowTypeCode.WorkReport => "bx-file-blank",
        _ => "bx-file",
    };

    /// <summary>Formats a submitted field value for display, converting Date fields to the Persian calendar.</summary>
    public static string FormatFieldValue(WorkflowFieldDefinition field, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return "";
        if (field.FieldType == WorkflowFieldType.Date
            && DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return ToPersianDate(date);
        return rawValue;
    }

    public static string ToPersianDate(DateTime date)
    {
        var calendar = new PersianCalendar();
        var text = $"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}";
        return ToPersianDigits(text);
    }

    private static string ToPersianDigits(string value)
    {
        const string digits = "۰۱۲۳۴۵۶۷۸۹";
        var chars = value.Select(c => c is >= '0' and <= '9' ? digits[c - '0'] : c).ToArray();
        return new string(chars);
    }

    public static string StatusLabel(WorkflowRequestStatus status) => status switch
    {
        WorkflowRequestStatus.PendingApproval => "در انتظار تایید",
        WorkflowRequestStatus.Approved => "تایید شده",
        WorkflowRequestStatus.Rejected => "رد شده",
        WorkflowRequestStatus.ReturnedToRequester => "بازگشت به درخواست‌کننده",
        WorkflowRequestStatus.Cancelled => "لغو شده",
        _ => status.ToString(),
    };

    public static string StatusBadgeClass(WorkflowRequestStatus status) => status switch
    {
        WorkflowRequestStatus.Approved => "badge-success",
        WorkflowRequestStatus.Rejected => "badge-danger",
        WorkflowRequestStatus.ReturnedToRequester => "badge-warning",
        WorkflowRequestStatus.Cancelled => "badge-muted",
        _ => "badge-info",
    };

    /// <summary>
    /// A step's label/icon depend on whether it's <paramref name="isCurrent"/> (the step a
    /// request is actually sitting at right now) as well as its own status — two Pending
    /// steps can mean "awaiting action now" vs "queued, not reached yet".
    /// </summary>
    public static string StepLabel(WorkflowStepStatus status, bool isCurrent) => status switch
    {
        WorkflowStepStatus.Pending when isCurrent => "در انتظار تایید",
        WorkflowStepStatus.Pending => "در صف، هنوز به این مرحله نرسیده",
        WorkflowStepStatus.Approved => "تایید شد",
        WorkflowStepStatus.Rejected => "رد شد",
        WorkflowStepStatus.ReturnedToRequester => "بازگشت به درخواست‌کننده",
        WorkflowStepStatus.ReturnedToPreviousStep => "بازگشت به مرحله قبل",
        _ => status.ToString(),
    };

    /// <summary>CSS state class for <c>.approval-step-icon</c> — controls its background/color.</summary>
    public static string StepIconClass(WorkflowStepStatus status, bool isCurrent) => status switch
    {
        WorkflowStepStatus.Pending when isCurrent => "is-pending",
        WorkflowStepStatus.Pending => "is-queued",
        WorkflowStepStatus.Approved => "is-approved",
        WorkflowStepStatus.Rejected => "is-rejected",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep => "is-returned",
        _ => "",
    };

    public static string StepIcon(WorkflowStepStatus status, bool isCurrent) => status switch
    {
        WorkflowStepStatus.Pending when isCurrent => "bx-time-five",
        WorkflowStepStatus.Pending => "bx-time",
        WorkflowStepStatus.Approved => "bx-check",
        WorkflowStepStatus.Rejected => "bx-x",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep => "bx-undo",
        _ => "bx-circle",
    };

    /// <summary>Badge color class for a completed step's own outcome (history views, not the live timeline).</summary>
    public static string StepBadgeClass(WorkflowStepStatus status) => status switch
    {
        WorkflowStepStatus.Approved => "badge-success",
        WorkflowStepStatus.Rejected => "badge-danger",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep => "badge-warning",
        _ => "badge-muted",
    };
}
