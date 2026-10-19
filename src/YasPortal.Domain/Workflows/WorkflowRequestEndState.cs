namespace YasPortal.Domain.Workflows;

/// <summary>
/// What a list row should say for a request that is no longer waiting at an approval step — where it ended up and who
/// is responsible — so the "current step" and "with" columns are never just a dash. Pure, so it can be tested.
/// </summary>
/// <param name="StageLabel">Persian text for the "current step" column.</param>
/// <param name="DeciderEmployeeId">The employee whose decision (or force-close) ended it; null when it is with the requester.</param>
/// <param name="WithRequester">True when the request is back with, or was cancelled by, the requester.</param>
public sealed record WorkflowRequestEndState(string StageLabel, Guid? DeciderEmployeeId, bool WithRequester)
{
    /// <summary>Null while the request is pending approval (the live step is shown instead).</summary>
    public static WorkflowRequestEndState? Of(WorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid? LastDecider(WorkflowStepStatus outcome, out WorkflowStepDecision? decision)
        {
            decision = request.StepDecisions
                .Where(d => d.Round == request.CurrentRound && d.Outcome == outcome)
                .OrderByDescending(d => d.ActedAtUtc)
                .FirstOrDefault();
            return decision?.ActedByEmployeeId;
        }

        switch (request.Status)
        {
            case WorkflowRequestStatus.PendingApproval:
                return null;

            case WorkflowRequestStatus.ReturnedToRequester:
                return new WorkflowRequestEndState("بازگشت‌خورده به درخواست‌کننده", null, true);

            case WorkflowRequestStatus.Approved:
                return new WorkflowRequestEndState("تایید نهایی شد", LastDecider(WorkflowStepStatus.Approved, out _), false);

            case WorkflowRequestStatus.Rejected:
            {
                var by = LastDecider(WorkflowStepStatus.Rejected, out var decision);
                var stepName = decision is null ? null : request.Steps.FirstOrDefault(s => s.Id == decision.StepId)?.Name;
                return new WorkflowRequestEndState(stepName is null ? "رد شد" : $"رد شد در مرحله «{stepName}»", by, false);
            }

            case WorkflowRequestStatus.Cancelled:
                return request.ForceClosedByEmployeeId is Guid admin
                    ? new WorkflowRequestEndState("بسته‌شده توسط مدیر", admin, false)
                    : new WorkflowRequestEndState("لغو شد", null, true);

            default:
                return null;
        }
    }
}
