namespace YasPortal.Domain.Workflows;

/// <summary>
/// An employee's submission of a fixed <see cref="WorkflowTypeCode"/>, carrying its
/// field values (validated against <see cref="WorkflowFieldCatalog"/> before creation)
/// and an ordered approval trail resolved from that type's <see cref="WorkflowStepDefinition"/>s
/// at submission time.
/// </summary>
public sealed class WorkflowRequest
{
    private WorkflowRequest()
    {
    }

    /// <param name="fieldValuesJson">The submitted field values, serialized as JSON.</param>
    /// <param name="resolvedSteps">
    /// The request's approval path, in order, already resolved from the type's step
    /// definitions (see <see cref="Organization.PositionHierarchy"/> for resolving
    /// "N levels up" steps to a concrete position at submission time).
    /// </param>
    public WorkflowRequest(
        WorkflowTypeCode workflowType,
        Guid requesterEmployeeId,
        Guid requesterPositionId,
        string fieldValuesJson,
        IReadOnlyList<(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId)> resolvedSteps)
    {
        if (requesterEmployeeId == Guid.Empty)
            throw new ArgumentException("Requester is required.", nameof(requesterEmployeeId));
        if (requesterPositionId == Guid.Empty)
            throw new ArgumentException("Requester's active position is required.", nameof(requesterPositionId));
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            throw new ArgumentException("Field values are required.", nameof(fieldValuesJson));
        if (resolvedSteps is null || resolvedSteps.Count == 0)
            throw new ArgumentException("A workflow request needs at least one approval step.", nameof(resolvedSteps));

        WorkflowType = workflowType;
        RequesterEmployeeId = requesterEmployeeId;
        RequesterPositionId = requesterPositionId;
        FieldValuesJson = fieldValuesJson;
        CreatedAtUtc = DateTime.UtcNow;
        Status = WorkflowRequestStatus.PendingApproval;

        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, step.Order, step.Name, step.ApproverPositionId));

        CurrentStepOrder = Steps.First().Order;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public WorkflowTypeCode WorkflowType { get; private set; }
    public Guid RequesterEmployeeId { get; private set; }
    public Guid RequesterPositionId { get; private set; }
    public string FieldValuesJson { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public WorkflowRequestStatus Status { get; private set; }
    public int CurrentStepOrder { get; private set; }
    public ICollection<WorkflowRequestStep> Steps { get; private set; } = new List<WorkflowRequestStep>();

    /// <summary>
    /// False whenever a step decision has happened that the requester hasn't looked at yet —
    /// drives the "you have updates" badge. Starts true (the requester just submitted it
    /// themselves, so there's nothing new to tell them), flips false on any step action, and
    /// back to true via <see cref="MarkSeenByRequester"/>.
    /// </summary>
    public bool RequesterHasSeenLatestUpdate { get; private set; } = true;

    public void MarkSeenByRequester() => RequesterHasSeenLatestUpdate = true;

    public WorkflowRequestStep CurrentStep =>
        Steps.SingleOrDefault(x => x.Order == CurrentStepOrder)
        ?? throw new InvalidOperationException("The request has no current step.");

    public void Approve(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.Approve(actingEmployeeId, comment);

        var next = Steps.Where(x => x.Order > step.Order).OrderBy(x => x.Order).FirstOrDefault();
        if (next is null)
        {
            Status = WorkflowRequestStatus.Approved;
        }
        else
        {
            // The step ahead can be sitting in a non-Pending state here (e.g. it previously
            // sent the request back via ReturnToPreviousStep) if this is a second pass through
            // it. It must become actionable again for its approver, not stay stuck.
            if (next.Status != WorkflowStepStatus.Pending)
                next.Reopen();
            CurrentStepOrder = next.Order;
        }
        RequesterHasSeenLatestUpdate = false;
    }

    public void Reject(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.Reject(actingEmployeeId, comment);
        Status = WorkflowRequestStatus.Rejected;
        RequesterHasSeenLatestUpdate = false;
    }

    public void ReturnToRequester(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.ReturnToRequester(actingEmployeeId, comment);
        Status = WorkflowRequestStatus.ReturnedToRequester;
        RequesterHasSeenLatestUpdate = false;
    }

    public void ReturnToPreviousStep(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        var previous = Steps.Where(x => x.Order < step.Order).OrderByDescending(x => x.Order).FirstOrDefault();

        if (previous is null)
        {
            // There is no approval step earlier than the first one — the only meaningful
            // "previous" stop from here is the requester themselves, so this falls back to
            // exactly the same outcome as ReturnToRequester rather than failing.
            step.ReturnToRequester(actingEmployeeId, comment);
            Status = WorkflowRequestStatus.ReturnedToRequester;
        }
        else
        {
            step.ReturnToPreviousStep(actingEmployeeId, comment);
            previous.Reopen();
            CurrentStepOrder = previous.Order;
        }
        RequesterHasSeenLatestUpdate = false;
    }

    public void Cancel()
    {
        if (Status is not (WorkflowRequestStatus.PendingApproval or WorkflowRequestStatus.ReturnedToRequester))
            throw new InvalidOperationException("Only a request that is pending approval or returned to the requester can be cancelled.");
        Status = WorkflowRequestStatus.Cancelled;
    }

    /// <summary>
    /// Puts a request that was sent back to the requester (<see cref="WorkflowRequestStatus.ReturnedToRequester"/>)
    /// back into the approval flow, resuming at the step that returned it — earlier steps
    /// that already approved it are left untouched, since only the step that flagged a
    /// problem needs to look at it again.
    /// </summary>
    public void Resubmit(string fieldValuesJson)
    {
        if (Status != WorkflowRequestStatus.ReturnedToRequester)
            throw new InvalidOperationException("Only a request returned to the requester can be resubmitted.");
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            throw new ArgumentException("Field values are required.", nameof(fieldValuesJson));

        var returnedStep = Steps.SingleOrDefault(x => x.Status == WorkflowStepStatus.ReturnedToRequester)
            ?? throw new InvalidOperationException("No step is currently marked as returned to the requester.");

        FieldValuesJson = fieldValuesJson;
        returnedStep.Reopen();
        CurrentStepOrder = returnedStep.Order;
        Status = WorkflowRequestStatus.PendingApproval;
    }

    private WorkflowRequestStep RequireActionableCurrentStep(Guid stepId)
    {
        if (Status != WorkflowRequestStatus.PendingApproval)
            throw new InvalidOperationException("This request is no longer pending approval.");
        var step = CurrentStep;
        if (step.Id != stepId)
            throw new InvalidOperationException("Only the request's current step can be acted on.");
        return step;
    }
}

/// <summary>One entry in a <see cref="WorkflowRequest"/>'s approval trail.</summary>
public sealed class WorkflowRequestStep
{
    private WorkflowRequestStep()
    {
    }

    internal WorkflowRequestStep(Guid requestId, Guid stepDefinitionId, int order, string name, Guid approverPositionId)
    {
        RequestId = requestId;
        StepDefinitionId = stepDefinitionId;
        Order = order;
        Name = name;
        ApproverPositionId = approverPositionId;
        Status = WorkflowStepStatus.Pending;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }
    public Guid StepDefinitionId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>
    /// The position resolved for this step at submission time. Whoever *currently*
    /// holds this position may act on the step — this is not a snapshot of a specific
    /// person, so a mid-flight position reassignment is picked up automatically.
    /// </summary>
    public Guid ApproverPositionId { get; private set; }
    public WorkflowStepStatus Status { get; private set; }
    public Guid? ActedByEmployeeId { get; private set; }
    public DateTime? ActedAtUtc { get; private set; }
    public string? Comment { get; private set; }

    public void Approve(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.Approved, actingEmployeeId, comment);
    public void Reject(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.Rejected, actingEmployeeId, comment);
    public void ReturnToRequester(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment);
    public void ReturnToPreviousStep(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.ReturnedToPreviousStep, actingEmployeeId, comment);

    /// <summary>Re-opens a previously approved step when a later step sends the request back to it.</summary>
    internal void Reopen()
    {
        Status = WorkflowStepStatus.Pending;
        ActedByEmployeeId = null;
        ActedAtUtc = null;
        Comment = null;
    }

    private void Act(WorkflowStepStatus status, Guid actingEmployeeId, string? comment)
    {
        if (Status != WorkflowStepStatus.Pending)
            throw new InvalidOperationException("This step has already been acted on.");
        if (actingEmployeeId == Guid.Empty)
            throw new ArgumentException("Acting employee is required.", nameof(actingEmployeeId));
        Status = status;
        ActedByEmployeeId = actingEmployeeId;
        ActedAtUtc = DateTime.UtcNow;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }
}
