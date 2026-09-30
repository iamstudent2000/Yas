namespace YasPortal.Domain.Workflows;

/// <summary>
/// A permanent record of one decision made on a <see cref="WorkflowRequestStep"/> — approve,
/// reject, return to requester, or return to a previous step.
///
/// <see cref="WorkflowRequestStep"/> only ever shows its *current* outcome: when
/// <see cref="WorkflowRequestStep.Reopen"/> re-activates a step for a second pass (because a
/// later step returned the request to it), the step's live <c>ActedByEmployeeId</c>,
/// <c>ActedAtUtc</c> and <c>Comment</c> are reset so the step can be acted on again — but the
/// decision that was just overwritten is never lost, because it was already appended here first.
/// A step acted on twice within the same round therefore has two decisions here, in order,
/// while the step itself only ever shows the most recent one.
/// </summary>
public sealed class WorkflowStepDecision
{
    private WorkflowStepDecision()
    {
    }

    internal WorkflowStepDecision(Guid requestId, Guid stepId, int round, int order, Guid approverPositionId, WorkflowStepStatus outcome, Guid actingEmployeeId, string? comment)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request is required.", nameof(requestId));
        if (stepId == Guid.Empty)
            throw new ArgumentException("Step is required.", nameof(stepId));
        if (round < 1)
            throw new ArgumentOutOfRangeException(nameof(round), "Round must start at 1.");
        if (approverPositionId == Guid.Empty)
            throw new ArgumentException("Approver position is required.", nameof(approverPositionId));
        if (actingEmployeeId == Guid.Empty)
            throw new ArgumentException("Acting employee is required.", nameof(actingEmployeeId));

        RequestId = requestId;
        StepId = stepId;
        Round = round;
        Order = order;
        ApproverPositionId = approverPositionId;
        Outcome = outcome;
        ActedByEmployeeId = actingEmployeeId;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ActedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }
    public Guid StepId { get; private set; }
    public int Round { get; private set; }
    public int Order { get; private set; }

    /// <summary>The position that was asked to decide. The acting employee held it at the time, even if reassigned since.</summary>
    public Guid ApproverPositionId { get; private set; }
    public WorkflowStepStatus Outcome { get; private set; }
    public Guid ActedByEmployeeId { get; private set; }
    public DateTime ActedAtUtc { get; private set; }
    public string? Comment { get; private set; }
}
