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
        WorkflowTypeCode.VehicleRequest => "bx-car",
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
        WorkflowStepStatus.ReturnedToSpecificStep => "بازگشت به مرحله دلخواه",
        WorkflowStepStatus.Superseded => "کنار گذاشته شد (درخواست پیش از رسیدن به این مرحله بسته یا به مرحله‌ای قبل‌تر بازگردانده شد)",
        _ => status.ToString(),
    };

    /// <summary>
    /// Label for a step in the live timeline. A step whose approval was voided by a return to an
    /// earlier step still carries who approved it, so it says that instead of "never reached".
    /// </summary>
    public static string StepLabel(WorkflowRequestStep step, bool isCurrent) =>
        step.Status == WorkflowStepStatus.Superseded && step.ActedAtUtc is not null
            ? "تایید قبلی باطل شد (درخواست به مرحله‌ای قبل‌تر بازگردانده شد؛ تایید دوباره لازم است)"
            : StepLabel(step.Status, isCurrent);

    /// <summary>
    /// Label for a step that may sit in a parallel group (spec §12.2). A step in a group that never
    /// got its turn to decide says <i>why</i> — which sibling approved (ANY mode: one was enough),
    /// rejected, or sent the request back — instead of the generic "set aside" text. Everything else
    /// is labelled exactly as <see cref="StepLabel(WorkflowRequestStep, bool)"/>.
    /// </summary>
    public static string StepLabel(WorkflowRequestStep step, bool isCurrent, IEnumerable<WorkflowRequestStep> groupSiblings)
    {
        if (step.Status != WorkflowStepStatus.Superseded || step.ActedAtUtc is not null)
            return StepLabel(step, isCurrent);

        var siblings = groupSiblings.Where(x => x.Round == step.Round && x.Order == step.Order && x.Id != step.Id).ToList();
        var approved = siblings.FirstOrDefault(x => x.Status == WorkflowStepStatus.Approved);
        if (step.ApprovalMode == ParallelApprovalMode.Any && approved is not null)
            return $"نیازی به تصمیم نبود — «{approved.Name}» از همین گروه موازی تایید کرد و تایید یکی کافی بود";

        var rejected = siblings.FirstOrDefault(x => x.Status == WorkflowStepStatus.Rejected);
        if (rejected is not null)
            return $"تصمیمی ثبت نشد — «{rejected.Name}» از همین گروه موازی رد کرد و درخواست بسته شد";

        var returned = siblings.FirstOrDefault(x => x.Status is WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep or WorkflowStepStatus.ReturnedToSpecificStep);
        if (returned is not null)
            return returned.Status == WorkflowStepStatus.ReturnedToRequester
                ? $"تصمیمی ثبت نشد — «{returned.Name}» از همین گروه موازی درخواست را به درخواست‌کننده بازگرداند"
                : $"تصمیمی ثبت نشد — «{returned.Name}» از همین گروه موازی درخواست را به مرحله‌ای قبل‌تر بازگرداند";

        return StepLabel(step, isCurrent);
    }

    /// <summary>CSS state class for <c>.approval-step-icon</c> — controls its background/color.</summary>
    public static string StepIconClass(WorkflowStepStatus status, bool isCurrent) => status switch
    {
        WorkflowStepStatus.Pending when isCurrent => "is-pending",
        WorkflowStepStatus.Pending => "is-queued",
        WorkflowStepStatus.Approved => "is-approved",
        WorkflowStepStatus.Rejected => "is-rejected",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep or WorkflowStepStatus.ReturnedToSpecificStep => "is-returned",
        WorkflowStepStatus.Superseded => "is-queued",
        _ => "",
    };

    public static string StepIcon(WorkflowStepStatus status, bool isCurrent) => status switch
    {
        WorkflowStepStatus.Pending when isCurrent => "bx-time-five",
        WorkflowStepStatus.Pending => "bx-time",
        WorkflowStepStatus.Approved => "bx-check",
        WorkflowStepStatus.Rejected => "bx-x",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep or WorkflowStepStatus.ReturnedToSpecificStep => "bx-undo",
        WorkflowStepStatus.Superseded => "bx-minus-circle",
        _ => "bx-circle",
    };

    /// <summary>Badge color class for a completed step's own outcome (history views, not the live timeline).</summary>
    public static string StepBadgeClass(WorkflowStepStatus status) => status switch
    {
        WorkflowStepStatus.Approved => "badge-success",
        WorkflowStepStatus.Rejected => "badge-danger",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep or WorkflowStepStatus.ReturnedToSpecificStep => "badge-warning",
        _ => "badge-muted",
    };

    // ---- whole-stage display (a stage = one step, or a parallel group) ----

    /// <summary>CSS state modifier for a stage card / icon, e.g. <c>is-awaiting</c>.</summary>
    public static string StageStateClass(WorkflowStageState state) => "is-" + state.ToString().ToLowerInvariant();

    public static string StageStateLabel(WorkflowStageState state) => state switch
    {
        WorkflowStageState.Awaiting => "در انتظار تصمیم",
        WorkflowStageState.Approved => "تایید شد",
        WorkflowStageState.Rejected => "رد شد",
        WorkflowStageState.Returned => "بازگردانده شد",
        WorkflowStageState.Skipped => "بدون تصمیم",
        _ => "در صف",
    };

    public static string StageStateBadgeClass(WorkflowStageState state) => state switch
    {
        WorkflowStageState.Awaiting => "badge-info",
        WorkflowStageState.Approved => "badge-success",
        WorkflowStageState.Rejected => "badge-danger",
        WorkflowStageState.Returned => "badge-warning",
        _ => "badge-muted",
    };

    public static string StageStateIcon(WorkflowStageState state) => state switch
    {
        WorkflowStageState.Awaiting => "bx-time-five",
        WorkflowStageState.Approved => "bx-check",
        WorkflowStageState.Rejected => "bx-x",
        WorkflowStageState.Returned => "bx-undo",
        WorkflowStageState.Skipped => "bx-minus",
        _ => "bx-time",
    };
}
