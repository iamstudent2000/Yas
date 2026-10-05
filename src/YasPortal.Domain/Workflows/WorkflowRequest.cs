using System.Text.Json;

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
        RequesterActions.Add(new WorkflowRequesterAction(Id, CurrentRound, WorkflowRequesterActionKind.Submitted, requesterEmployeeId));
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
    /// Append-only record of what the requester themselves did (submit, resubmit, cancel), each with
    /// the round and a real timestamp. Nothing here is ever updated or removed, so the timeline can
    /// show requester events from stored facts rather than inferring them from the request's state.
    /// </summary>
    public ICollection<WorkflowRequesterAction> RequesterActions { get; private set; } = new List<WorkflowRequesterAction>();

    /// <summary>
    /// Every decision ever made on any step, across every pass and every round — see
    /// <see cref="WorkflowStepDecision"/>. This is the authoritative approval history;
    /// <see cref="WorkflowRequestStep"/>'s own ActedBy/ActedAt/Comment only ever reflect its
    /// current, possibly-since-reopened state.
    /// </summary>
    public ICollection<WorkflowStepDecision> StepDecisions { get; private set; } = new List<WorkflowStepDecision>();

    /// <summary>
    /// Append-only history of every field value change (spec §11.3). <see cref="FieldValuesJson"/>
    /// only ever holds the latest values; this is where what a field used to say lives, so an
    /// edit made while resubmitting a returned request never silently destroys the old value.
    /// </summary>
    public ICollection<WorkflowFieldChange> FieldChanges { get; private set; } = new List<WorkflowFieldChange>();

    /// <summary>Set once, permanently, if an admin ever force-closes this request — see <see cref="ForceClose"/>.</summary>
    public Guid? ForceClosedByEmployeeId { get; private set; }
    public DateTime? ForceClosedAtUtc { get; private set; }
    public string? ForceCloseReason { get; private set; }

    /// <summary>
    /// False whenever a step decision has happened that the requester hasn't looked at yet —
    /// drives the "you have updates" badge. Starts true (the requester just submitted it
    /// themselves, so there's nothing new to tell them), flips false on any step action, and
    /// back to true via <see cref="MarkSeenByRequester"/>.
    /// </summary>
    public bool RequesterHasSeenLatestUpdate { get; private set; } = true;

    /// <summary>
    /// Optimistic-concurrency token. Incremented by every state-changing action (approve, reject,
    /// return, cancel, resubmit) and mapped as a concurrency token, so two people acting on the
    /// same request from stale copies can never both succeed — the second save fails with a
    /// <c>DbUpdateConcurrencyException</c> instead of silently overwriting the first decision.
    /// Deliberately not bumped by <see cref="MarkSeenByRequester"/>.
    /// </summary>
    public int Revision { get; private set; }

    public void MarkSeenByRequester() => RequesterHasSeenLatestUpdate = true;

    public WorkflowRequestStep CurrentStep =>
        Steps.SingleOrDefault(x => x.Round == CurrentRound && x.Order == CurrentStepOrder)
        ?? throw new InvalidOperationException("The request has no current step.");

    /// <summary>Terminal statuses can never be reopened or acted on again by anyone.</summary>
    public bool IsTerminal => Status is WorkflowRequestStatus.Approved or WorkflowRequestStatus.Rejected or WorkflowRequestStatus.Cancelled;

    /// <summary>
    /// Whether the requester can still cancel this request themselves.
    /// <list type="bullet">
    /// <item>Always allowed when the request was returned to the requester (they own the next move).</item>
    /// <item>While still pending approval, only allowed if no step in the current round has ever
    /// approved — including a step that approved once and was later reset to Pending by
    /// <see cref="WorkflowRequestStep.Reopen"/> after a return-to-previous-step. Looking only at the
    /// live step status is not enough here: Reopen() intentionally resets it for a fresh decision,
    /// which would otherwise make a request that a manager already approved look, to this check,
    /// exactly like one nobody has touched yet. <see cref="StepDecisions"/> is the permanent record
    /// that catches that case, so once real progress has been made the requester can no longer
    /// unilaterally withdraw it — from that point on, only <see cref="ForceClose"/> can close it.</item>
    /// </list>
    /// </summary>
    public bool CanBeCancelled =>
        Status == WorkflowRequestStatus.ReturnedToRequester
        || (Status == WorkflowRequestStatus.PendingApproval
            // Fails closed: a request always has at least one step, so an empty collection means
            // the steps were not loaded (missing .Include) — never treat that as "nobody approved yet".
            && Steps.Count > 0
            && Steps.Where(s => s.Round == CurrentRound).All(s => s.Status != WorkflowStepStatus.Approved)
            && StepDecisions.Where(d => d.Round == CurrentRound).All(d => d.Outcome != WorkflowStepStatus.Approved));

    /// <summary>
    /// SuperAdmin-only escape hatch (§22.1): force-closes a request stuck for any reason — most
    /// commonly a vacant approver position (§5.4) that no requester action can resolve — from
    /// whatever step it is currently on. Unlike <see cref="Cancel"/>, this works even after a step
    /// has already approved, and requires no cooperation from anyone in the approval chain. The
    /// reason is mandatory and, like the rest of this record, permanent: force-close cannot be undone.
    /// </summary>
    public void ForceClose(Guid adminEmployeeId, string reason)
    {
        if (adminEmployeeId == Guid.Empty)
            throw new ArgumentException("Admin employee is required.", nameof(adminEmployeeId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to force-close a request.", nameof(reason));
        if (IsTerminal)
            throw new InvalidOperationException("This request is already closed.");

        Status = WorkflowRequestStatus.Cancelled;
        ForceClosedByEmployeeId = adminEmployeeId;
        ForceClosedAtUtc = DateTime.UtcNow;
        ForceCloseReason = reason.Trim();
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    public void Approve(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.Approve(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.Approved, actingEmployeeId, comment);

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
        Revision++;
    }

    public void Reject(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.Reject(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.Rejected, actingEmployeeId, comment);
        Status = WorkflowRequestStatus.Rejected;
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    public void ReturnToRequester(Guid stepId, Guid actingEmployeeId, string? comment)
    {
        var step = RequireActionableCurrentStep(stepId);
        step.ReturnToRequester(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment);
        Status = WorkflowRequestStatus.ReturnedToRequester;
        RequesterHasSeenLatestUpdate = false;
        Revision++;
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
            RecordDecision(step, WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment);
            Status = WorkflowRequestStatus.ReturnedToRequester;
        }
        else
        {
            step.ReturnToPreviousStep(actingEmployeeId, comment);
            RecordDecision(step, WorkflowStepStatus.ReturnedToPreviousStep, actingEmployeeId, comment);
            // previous.Reopen() below resets the target step's own ActedBy/ActedAt/Comment so it
            // can be decided on again — but its original decision is never lost, because it was
            // already appended to StepDecisions the first time it was acted on.
            previous.Reopen();
            CurrentStepOrder = previous.Order;
        }
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    public void Cancel()
    {
        if (Steps.Count == 0)
            throw new InvalidOperationException("The request's steps must be loaded before it can be cancelled.");
        if (!CanBeCancelled)
            throw new InvalidOperationException("This request can no longer be cancelled — a later step has already approved it, or it is not in a cancellable state.");
        Status = WorkflowRequestStatus.Cancelled;
        RequesterActions.Add(new WorkflowRequesterAction(Id, CurrentRound, WorkflowRequesterActionKind.Cancelled, RequesterEmployeeId));
        Revision++;
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

        var previousValues = ParseFieldValues(FieldValuesJson);
        var newValues = ParseFieldValues(fieldValuesJson);

        FieldValuesJson = fieldValuesJson;
        CurrentRound++;
        RecordFieldChanges(previousValues, newValues);
        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, CurrentRound, step.Order, step.Name, step.ApproverPositionId));
        CurrentStepOrder = resolvedSteps.Min(x => x.Order);
        RequesterActions.Add(new WorkflowRequesterAction(Id, CurrentRound, WorkflowRequesterActionKind.Resubmitted, RequesterEmployeeId));
        Status = WorkflowRequestStatus.PendingApproval;
        // The requester just performed the resubmit themselves — there is nothing new for them to notice.
        RequesterHasSeenLatestUpdate = true;
        Revision++;
    }

    /// <summary>
    /// Appends one <see cref="WorkflowFieldChange"/> per field whose value differs between the old
    /// and new submission. The editor is always the requester (only they can edit a returned
    /// request), acting from the position fixed on the request at creation (§10.1). A blank value
    /// and a missing value count as the same thing (null), so re-saving an untouched empty
    /// optional field never produces noise.
    /// </summary>
    private void RecordFieldChanges(IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after)
    {
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            var oldValue = Normalize(before.TryGetValue(key, out var o) ? o : null);
            var newValue = Normalize(after.TryGetValue(key, out var n) ? n : null);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                continue;
            FieldChanges.Add(new WorkflowFieldChange(Id, CurrentRound, key, oldValue, newValue, RequesterEmployeeId, RequesterPositionId));
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string?> ParseFieldValues(string json)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            // Unparseable stored values can't be diffed; treat as "no previous values" rather than failing the resubmit.
        }
        return result;
    }

    private void RecordDecision(WorkflowRequestStep step, WorkflowStepStatus outcome, Guid actingEmployeeId, string? comment) =>
        StepDecisions.Add(new WorkflowStepDecision(Id, step.Id, step.Round, step.Order, step.ApproverPositionId, outcome, actingEmployeeId, comment));

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
