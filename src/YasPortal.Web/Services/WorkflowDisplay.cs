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

    public static string StepLabel(WorkflowStepStatus status) => status switch
    {
        WorkflowStepStatus.Pending => "در انتظار",
        WorkflowStepStatus.Approved => "تایید شد",
        WorkflowStepStatus.Rejected => "رد شد",
        WorkflowStepStatus.ReturnedToRequester => "بازگشت به درخواست‌کننده",
        WorkflowStepStatus.ReturnedToPreviousStep => "بازگشت به مرحله قبل",
        _ => status.ToString(),
    };

    /// <summary>CSS state class for <c>.approval-step-icon</c> — controls its background/color.</summary>
    public static string StepIconClass(WorkflowStepStatus status) => status switch
    {
        WorkflowStepStatus.Pending => "is-pending",
        WorkflowStepStatus.Approved => "is-approved",
        WorkflowStepStatus.Rejected => "is-rejected",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep => "is-returned",
        _ => "",
    };

    public static string StepIcon(WorkflowStepStatus status) => status switch
    {
        WorkflowStepStatus.Pending => "bx-time-five",
        WorkflowStepStatus.Approved => "bx-check",
        WorkflowStepStatus.Rejected => "bx-x",
        WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep => "bx-undo",
        _ => "bx-circle",
    };
}
