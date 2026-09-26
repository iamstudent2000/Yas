namespace YasPortal.Domain.Workflows;

/// <summary>
/// An employee's submission of a fixed <see cref="WorkflowTypeCode"/>, carrying its
/// field values (validated against <see cref="WorkflowFieldCatalog"/> before creation)
/// and an ordered approval trail resolved from that type's <see cref="WorkflowStepDefinition"/>s
/// at submission time.
///
/// A request can go through multiple "rounds": if it is returned to the requester and they
/// resubmit, the whole path restarts from the first step (<see cref="Resubmit"/>) as a new
/// round, with every approver signing off again — but every earlier round's steps are left
/// exactly as they were, standing as a permanent historical record rather than being reused
/// or overwritten.
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
        CurrentRound = 1;

        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, CurrentRound, step.Order, step.Name, step.ApproverPositionId));

        CurrentStepOrder = Steps.Min(x => x.Order);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public WorkflowTypeCode WorkflowType { get; private set; }
    public Guid RequesterEmployeeId { get; private set; }
    public Guid RequesterPositionId { get; private set; }
    public string FieldValuesJson { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public WorkflowRequestStatus Status { get; private set; }
    public int CurrentStepOrder { get; private set; }

    /// <summary>Which round of the approval path is currently live. Starts at 1 and increments by one on every <see cref="Resubmit"/>.</summary>
    public int CurrentRound { get; private set; } = 1;

    /// <summary>
    /// Every step from every round, current and historical alike. Use <see cref="CurrentStep"/>
    /// for the one live step; group by <see cref="WorkflowRequestStep.Round"/> to present past
    /// rounds as history.
    /// </summary>
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
        Steps.SingleOrDefault(x => x.Round == CurrentRound && x.Order == CurrentStepOrder)
        ?? throw new InvalidOperationException("The request has no current step.");

    /// <summary>
    /// Whether the requester can still cancel this request. Only true while it is still
    /// pending (or bounced straight back before anyone downstream acted) and — critically —
    /// no step in the current round has approved it yet: once some approver has already
    /// signed off this round, cancelling would silently throw away their decision.
    /// </summary>
    public bool CanBeCancelled =>
        Status is WorkflowRequestStatus.PendingApproval or WorkflowRequestStatus.ReturnedToRequester
        && Steps.Where(s => s.Round == CurrentRound).All(s => s.Status != WorkflowStepStatus.Approved);

    public void Approve(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.Approve(actingEmployeeId, comment);

        var next = Steps.Where(x => x.Round == CurrentRound && x.Order > step.Order).OrderBy(x => x.Order).FirstOrDefault();
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
        var previous = Steps.Where(x => x.Round == CurrentRound && x.Order < step.Order).OrderByDescending(x => x.Order).FirstOrDefault();

        if (previous is null)
        {
            // There is no approval step earlier than the first one in this round — the only
            // meaningful "previous" stop from here is the requester themselves, so this falls
            // back to exactly the same outcome as ReturnToRequester rather than failing.
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
        if (!CanBeCancelled)
            throw new InvalidOperationException("This request can no longer be cancelled — a later step has already approved it, or it is not in a cancellable state.");
        Status = WorkflowRequestStatus.Cancelled;
    }

    /// <summary>
    /// Puts a request that was sent back to the requester (<see cref="WorkflowRequestStatus.ReturnedToRequester"/>)
    /// back into the approval flow as a brand new round, starting from the first step again —
    /// every approver signs off again, since the requester may have changed anything. The
    /// round just closed (including any step in it that was never reached, now marked
    /// <see cref="WorkflowStepStatus.Superseded"/>) is left completely untouched as history.
    /// </summary>
    /// <param name="resolvedSteps">
    /// A freshly resolved approval path for the new round (see <see cref="Organization.PositionHierarchy"/>) —
    /// resolved again rather than reusing the original round's, since the org hierarchy or a
    /// fixed position's holder may have changed since the request was first submitted.
    /// </param>
    public void Resubmit(string fieldValuesJson, IReadOnlyList<(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId)> resolvedSteps)
    {
        if (Status != WorkflowRequestStatus.ReturnedToRequester)
            throw new InvalidOperationException("Only a request returned to the requester can be resubmitted.");
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            throw new ArgumentException("Field values are required.", nameof(fieldValuesJson));
        if (resolvedSteps is null || resolvedSteps.Count == 0)
            throw new ArgumentException("A workflow request needs at least one approval step.", nameof(resolvedSteps));

        // Any step in the round being closed that was never reached (because an earlier step
        // already returned the request) is now moot — mark it Superseded so it doesn't sit
        // there looking like it's still awaiting action forever.
        foreach (var step in Steps.Where(x => x.Round == CurrentRound && x.Status == WorkflowStepStatus.Pending))
            step.Supersede();

        FieldValuesJson = fieldValuesJson;
        CurrentRound++;
        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, CurrentRound, step.Order, step.Name, step.ApproverPositionId));
        CurrentStepOrder = resolvedSteps.Min(x => x.Order);
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

/// <summary>One entry in a <see cref="WorkflowRequest"/>'s approval trail, scoped to a single <see cref="Round"/>.</summary>
public sealed class WorkflowRequestStep
{
    private WorkflowRequestStep()
    {
    }

    internal WorkflowRequestStep(Guid requestId, Guid stepDefinitionId, int round, int order, string name, Guid approverPositionId)
    {
        RequestId = requestId;
        StepDefinitionId = stepDefinitionId;
        Round = round;
        Order = order;
        Name = name;
        ApproverPositionId = approverPositionId;
        Status = WorkflowStepStatus.Pending;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }
    public Guid StepDefinitionId { get; private set; }

    /// <summary>Which resubmission round this step belongs to — see <see cref="WorkflowRequest.CurrentRound"/>.</summary>
    public int Round { get; private set; }
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

    /// <summary>Marks a step that was never reached in its round as moot, once that round is closed out by a resubmission.</summary>
    internal void Supersede()
    {
        if (Status != WorkflowStepStatus.Pending)
            return;
        Status = WorkflowStepStatus.Superseded;
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
