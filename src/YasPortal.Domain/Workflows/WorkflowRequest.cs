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
        : this(workflowType, requesterEmployeeId, requesterPositionId, fieldValuesJson, ToResolvedSteps(resolvedSteps))
    {
    }

    /// <summary>
    /// Same as above, for a path that may contain parallel groups: resolved steps sharing one
    /// <see cref="WorkflowResolvedStep.Order"/> are approved side by side (spec §12.2, §14) under the
    /// group's mode and reject policy.
    /// </summary>
    public WorkflowRequest(
        WorkflowTypeCode workflowType,
        Guid requesterEmployeeId,
        Guid requesterPositionId,
        string fieldValuesJson,
        IReadOnlyList<WorkflowResolvedStep> resolvedSteps,
        Guid? createdByEmployeeId = null)
    {
        if (requesterEmployeeId == Guid.Empty)
            throw new ArgumentException("Requester is required.", nameof(requesterEmployeeId));
        if (requesterPositionId == Guid.Empty)
            throw new ArgumentException("Requester's active position is required.", nameof(requesterPositionId));
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            throw new ArgumentException("Field values are required.", nameof(fieldValuesJson));
        if (resolvedSteps is null || resolvedSteps.Count == 0)
            throw new ArgumentException("A workflow request needs at least one approval step.", nameof(resolvedSteps));
        ValidateParallelGroups(resolvedSteps);

        WorkflowType = workflowType;
        RequesterEmployeeId = requesterEmployeeId;
        RequesterPositionId = requesterPositionId;
        // Set only when an administrator submitted the request for the requester; null when the requester submitted it.
        CreatedByEmployeeId = createdByEmployeeId is { } by && by != requesterEmployeeId ? by : null;
        FieldValuesJson = fieldValuesJson;
        CreatedAtUtc = DateTime.UtcNow;
        Status = WorkflowRequestStatus.PendingApproval;
        CurrentRound = 1;

        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, CurrentRound, step.Order, step.Name, step.ApproverPositionId, step.Mode, step.RejectPolicy, step.FillsPart));

        CurrentStepOrder = Steps.Min(x => x.Order);
        RequesterActions.Add(new WorkflowRequesterAction(Id, CurrentRound, WorkflowRequesterActionKind.Submitted, CreatedByEmployeeId ?? requesterEmployeeId));
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public WorkflowTypeCode WorkflowType { get; private set; }
    public Guid RequesterEmployeeId { get; private set; }
    public Guid RequesterPositionId { get; private set; }
    /// <summary>The administrator who submitted this request on the requester's behalf, or null when the requester submitted it themselves.</summary>
    public Guid? CreatedByEmployeeId { get; private set; }
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

    /// <summary>
    /// The live step of a sequential request. When the current step is a parallel group (several
    /// sibling steps sharing <see cref="CurrentStepOrder"/>) there is no single "current step" — use
    /// <see cref="CurrentSteps"/> — and this throws rather than quietly picking one of them.
    /// </summary>
    public WorkflowRequestStep CurrentStep
    {
        get
        {
            var current = CurrentSteps;
            if (current.Count == 0)
                throw new InvalidOperationException("The request has no current step.");
            if (current.Count > 1)
                throw new InvalidOperationException("The request's current step is a parallel group; use CurrentSteps.");
            return current[0];
        }
    }

    /// <summary>
    /// Every step of the live group in the current round: one step for an ordinary step, several for a
    /// parallel group (spec §12.2). In a stable order (by name, then id) so screens do not reshuffle.
    /// </summary>
    public IReadOnlyList<WorkflowRequestStep> CurrentSteps => StepsAt(CurrentStepOrder);

    /// <summary>The steps of the current round at one order — the whole parallel group, or the single step.</summary>
    public IReadOnlyList<WorkflowRequestStep> StepsAt(int order) =>
        Steps.Where(x => x.Round == CurrentRound && x.Order == order)
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .ToList();

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
        SupersedePendingSteps();
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    /// <summary>
    /// Reopens a rejected request (spec §13.4). The request goes back to <see cref="WorkflowRequestStatus.PendingApproval"/>
    /// at the step that rejected it: that step becomes actionable again and the steps after it, which
    /// were marked <see cref="WorkflowStepStatus.Superseded"/> when the request was rejected, wait
    /// for it again. Steps before it keep their approvals — they do not have to be repeated. Same
    /// round: nothing is deleted, the rejection stays in <see cref="StepDecisions"/>, and the
    /// reopen itself is recorded with who did it, when and why. Force-closed and cancelled
    /// requests (status Cancelled) cannot be reopened.
    /// </summary>
    public void Reopen(Guid actingEmployeeId, string reason)
    {
        if (actingEmployeeId == Guid.Empty)
            throw new ArgumentException("Acting employee is required.", nameof(actingEmployeeId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to reopen a request.", nameof(reason));
        if (Status != WorkflowRequestStatus.Rejected)
            throw new InvalidOperationException("Only a rejected request can be reopened.");
        if (Steps.Count == 0)
            throw new InvalidOperationException("The request's steps must be loaded before it can be reopened.");

        // The rejection happened in the live group. In an ordinary step that is the whole step; in a
        // parallel group only the position(s) that rejected must answer again (§13.4) — approvals the
        // others already gave stay as they are. Positions that never answered because the rejection
        // closed the request first (Superseded) wait for their turn again.
        var rejecting = CurrentSteps.Where(x => x.Status == WorkflowStepStatus.Rejected).ToList();
        if (rejecting.Count == 0)
            throw new InvalidOperationException("The rejecting step could not be identified.");

        foreach (var step in rejecting)
            step.Reopen();
        foreach (var other in Steps.Where(x => x.Round == CurrentRound && x.Order >= CurrentStepOrder))
            other.RestoreAfterReopen();

        Status = WorkflowRequestStatus.PendingApproval;
        RequesterActions.Add(new WorkflowRequesterAction(Id, CurrentRound, WorkflowRequesterActionKind.Reopened, actingEmployeeId, reason));
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    /// <param name="partValues">
    /// The values the approver filled in for the form part their step owns (see <see cref="WorkflowPartDefinition"/>).
    /// Required — and validated — when the step owns a part; refused when it does not. The step is the
    /// live, pending one, so a part is only ever editable by its own step while that step is waiting:
    /// once it has approved the part is locked, and it opens again only when the request is returned
    /// or reopened to that step. Every changed value is appended to <see cref="FieldChanges"/>.
    /// </param>
    public void Approve(Guid stepId, Guid actingEmployeeId, string? comment, WorkflowActingAuthority? authority = null, IReadOnlyDictionary<string, string?>? partValues = null)
    {
        var step = RequireActionableStep(stepId);
        // Before anything is marked, so a refused part leaves the request exactly as it was.
        ApplyStepPart(step, actingEmployeeId, partValues);
        step.Approve(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.Approved, actingEmployeeId, comment, authority: authority);

        var group = StepsAt(step.Order);
        if (IsSatisfied(group))
            CompleteGroupAndAdvance(step.Order);
        else if (EveryoneResponded(group) && group.Any(x => x.Status == WorkflowStepStatus.Rejected))
            CloseAsRejected();
        // Otherwise the group is still waiting on other positions (ALL mode, or a wait-for-all rejection pending).
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    public void Reject(Guid stepId, Guid actingEmployeeId, string? comment, WorkflowActingAuthority? authority = null)
    {
        var step = RequireActionableStep(stepId);
        step.Reject(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.Rejected, actingEmployeeId, comment, authority: authority);

        // Immediate: the first rejection closes the request. Wait-for-all (§14.3): it closes only once
        // every position in the group has answered — and only if the group did not get satisfied,
        // which it cannot have, or it would already have moved on.
        if (step.RejectPolicy == ParallelRejectPolicy.Immediate || EveryoneResponded(StepsAt(step.Order)))
            CloseAsRejected();
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    /// <summary>
    /// Saves the form part owned by <paramref name="step"/> (if any) into <see cref="FieldValuesJson"/>
    /// and appends a <see cref="WorkflowFieldChange"/> for every value that changed, attributed to
    /// the person who actually filled it and to the step's position (§11.3). Only the fields of that
    /// part are touched; the requester's values and other parts stay as they are.
    /// </summary>
    private void ApplyStepPart(WorkflowRequestStep step, Guid actingEmployeeId, IReadOnlyDictionary<string, string?>? partValues)
    {
        var part = WorkflowFieldCatalog.GetStepPart(WorkflowType, step.FillsPart);
        if (part is null)
        {
            if (partValues is { Count: > 0 })
                throw new InvalidOperationException("This step does not own a part of the form.");
            return;
        }

        var errors = WorkflowFieldCatalog.ValidatePartValues(WorkflowType, part.Part, partValues ?? new Dictionary<string, string?>());
        if (errors.Count > 0)
            throw new ArgumentException("The form part of this step is not valid: " + string.Join("; ", errors.Select(e => $"{e.Key} — {e.Message}")), nameof(partValues));

        var before = ParseFieldValues(FieldValuesJson);
        var after = new Dictionary<string, string?>(before, StringComparer.Ordinal);
        foreach (var field in WorkflowFieldCatalog.GetFields(WorkflowType, part.Part))
        {
            string? raw = null;
            partValues?.TryGetValue(field.Key, out raw);
            after[field.Key] = Normalize(raw);
        }

        RecordFieldChanges(before, after, actingEmployeeId, step.ApproverPositionId);
        FieldValuesJson = JsonSerializer.Serialize(after);
    }

    /// <summary>Whether the group has all the approvals its mode asks for (§14.1 ANY, §14.2 ALL).</summary>
    private static bool IsSatisfied(IReadOnlyList<WorkflowRequestStep> group) =>
        group[0].ApprovalMode == ParallelApprovalMode.Any
            ? group.Any(x => x.Status == WorkflowStepStatus.Approved)
            : group.All(x => x.Status == WorkflowStepStatus.Approved);

    private static bool EveryoneResponded(IReadOnlyList<WorkflowRequestStep> group) =>
        group.All(x => x.Status != WorkflowStepStatus.Pending);

    private void CloseAsRejected()
    {
        Status = WorkflowRequestStatus.Rejected;
        SupersedePendingSteps();
    }

    /// <summary>
    /// The group at <paramref name="order"/> is satisfied: positions in it that have not answered
    /// (possible only in ANY mode) are no longer needed, and the request moves to the next group — or
    /// is approved when there is none. Entering a group resets any step in it that is not Pending
    /// (for example after a return to an earlier step), so the whole group decides afresh.
    /// </summary>
    private void CompleteGroupAndAdvance(int order)
    {
        foreach (var waiting in StepsAt(order))
            waiting.Supersede();

        var later = Steps.Where(x => x.Round == CurrentRound && x.Order > order).Select(x => x.Order).ToList();
        if (later.Count == 0)
        {
            Status = WorkflowRequestStatus.Approved;
            return;
        }

        var nextOrder = later.Min();
        // A step ahead can be sitting in a non-Pending state here (e.g. it previously sent the request
        // back via ReturnToPreviousStep) if this is a second pass through it. It must become actionable
        // again for its approver, not stay stuck.
        foreach (var next in StepsAt(nextOrder).Where(x => x.Status != WorkflowStepStatus.Pending))
            next.Reopen();
        CurrentStepOrder = nextOrder;
    }

    /// <summary>
    /// The acting step sends the request back to an earlier step, so approvals its parallel siblings
    /// already gave no longer count — like the steps in between, they decide again once the request
    /// returns. Siblings that have not answered stay as they are.
    /// </summary>
    private void SupersedeSiblingApprovals(WorkflowRequestStep acting)
    {
        foreach (var sibling in StepsAt(acting.Order).Where(x => x.Id != acting.Id))
            sibling.SupersedeApproval();
    }

    /// <summary>
    /// Once a round is closed for good (rejected, cancelled, force-closed) any step in it still
    /// Pending will never be acted on — mark it Superseded so the trail does not show it as
    /// forever awaiting someone. Decisions already recorded are untouched.
    /// </summary>
    private void SupersedePendingSteps()
    {
        foreach (var step in Steps.Where(x => x.Round == CurrentRound && x.Status == WorkflowStepStatus.Pending))
            step.Supersede();
    }

    public void ReturnToRequester(Guid stepId, Guid actingEmployeeId, string? comment, WorkflowActingAuthority? authority = null)
    {
        var step = RequireActionableStep(stepId);
        step.ReturnToRequester(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment, authority: authority);
        Status = WorkflowRequestStatus.ReturnedToRequester;
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    public void ReturnToPreviousStep(Guid stepId, Guid actingEmployeeId, string? comment, WorkflowActingAuthority? authority = null)
    {
        var step = RequireActionableStep(stepId);
        var earlier = Steps.Where(x => x.Round == CurrentRound && x.Order < step.Order).Select(x => x.Order).ToList();

        if (earlier.Count == 0)
        {
            // There is no approval step earlier than the first one in this round — the only
            // meaningful "previous" stop from here is the requester themselves, so this falls
            // back to exactly the same outcome as ReturnToRequester rather than failing.
            step.ReturnToRequester(actingEmployeeId, comment);
            RecordDecision(step, WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment, authority: authority);
            Status = WorkflowRequestStatus.ReturnedToRequester;
        }
        else
        {
            step.ReturnToPreviousStep(actingEmployeeId, comment);
            RecordDecision(step, WorkflowStepStatus.ReturnedToPreviousStep, actingEmployeeId, comment, authority: authority);
            // Reopen() below resets the target step's own ActedBy/ActedAt/Comment so it can be
            // decided on again — but its original decision is never lost, because it was already
            // appended to StepDecisions the first time it was acted on. When the step before is a
            // parallel group, the whole group decides again.
            var previousOrder = earlier.Max();
            SupersedeSiblingApprovals(step);
            foreach (var previous in StepsAt(previousOrder))
                previous.Reopen();
            CurrentStepOrder = previousOrder;
        }
        RequesterHasSeenLatestUpdate = false;
        Revision++;
    }

    /// <summary>
    /// Sends the request back to a chosen earlier step of the current round (spec §13.2). That step
    /// becomes actionable again; every step between it and the acting step that had already
    /// approved is marked <see cref="WorkflowStepStatus.Superseded"/> — their approvals no longer
    /// count and new ones are required as the request moves forward again. The decision log keeps
    /// every earlier approval, and this decision records which step it returned to.
    /// </summary>
    public void ReturnToSpecificStep(Guid stepId, Guid actingEmployeeId, int targetOrder, string? comment, WorkflowActingAuthority? authority = null)
    {
        var step = RequireActionableStep(stepId);
        var target = StepsAt(targetOrder);
        if (target.Count == 0)
            throw new InvalidOperationException("The chosen step does not exist in this round.");
        if (targetOrder >= step.Order)
            throw new InvalidOperationException("A request can only be returned to an earlier step.");
        if (!target.Any(x => x.Status == WorkflowStepStatus.Approved))
            throw new InvalidOperationException("The chosen step has not approved yet, so there is nothing to return to.");

        step.ReturnToSpecificStep(actingEmployeeId, comment);
        RecordDecision(step, WorkflowStepStatus.ReturnedToSpecificStep, actingEmployeeId, comment, targetOrder, authority);

        foreach (var between in Steps.Where(x => x.Round == CurrentRound && x.Order > targetOrder && x.Order < step.Order))
            between.SupersedeApproval();
        SupersedeSiblingApprovals(step);

        // Like ReturnToPreviousStep: the target's own live outcome is reset for a fresh decision,
        // while its original approval is already safe in StepDecisions. A parallel group decides again as a whole.
        foreach (var reopened in target)
            reopened.Reopen();
        CurrentStepOrder = targetOrder;
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
        SupersedePendingSteps();
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
    public void Resubmit(string fieldValuesJson, IReadOnlyList<(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId)> resolvedSteps) =>
        Resubmit(fieldValuesJson, ToResolvedSteps(resolvedSteps));

    /// <summary>Resubmits with a path that may contain parallel groups — see the constructor taking <see cref="WorkflowResolvedStep"/>s.</summary>
    public void Resubmit(string fieldValuesJson, IReadOnlyList<WorkflowResolvedStep> resolvedSteps)
    {
        if (Status != WorkflowRequestStatus.ReturnedToRequester)
            throw new InvalidOperationException("Only a request returned to the requester can be resubmitted.");
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            throw new ArgumentException("Field values are required.", nameof(fieldValuesJson));
        if (resolvedSteps is null || resolvedSteps.Count == 0)
            throw new ArgumentException("A workflow request needs at least one approval step.", nameof(resolvedSteps));
        ValidateParallelGroups(resolvedSteps);

        var previousValues = ParseFieldValues(FieldValuesJson);
        var newValues = ParseFieldValues(fieldValuesJson);
        EnsureOnlyRequesterPartChanged(previousValues, newValues);

        // Any step in the round being closed that was never reached (because an earlier step
        // already returned the request) is now moot — mark it Superseded so it doesn't sit
        // there looking like it's still awaiting action forever.
        foreach (var step in Steps.Where(x => x.Round == CurrentRound && x.Status == WorkflowStepStatus.Pending))
            step.Supersede();

        FieldValuesJson = fieldValuesJson;
        CurrentRound++;
        RecordFieldChanges(previousValues, newValues, RequesterEmployeeId, RequesterPositionId);
        foreach (var step in resolvedSteps.OrderBy(x => x.Order))
            Steps.Add(new WorkflowRequestStep(Id, step.StepDefinitionId, CurrentRound, step.Order, step.Name, step.ApproverPositionId, step.Mode, step.RejectPolicy, step.FillsPart));
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
    private void RecordFieldChanges(IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after, Guid changedByEmployeeId, Guid changedByPositionId)
    {
        foreach (var key in before.Keys.Union(after.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            var oldValue = Normalize(before.TryGetValue(key, out var o) ? o : null);
            var newValue = Normalize(after.TryGetValue(key, out var n) ? n : null);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                continue;
            FieldChanges.Add(new WorkflowFieldChange(Id, CurrentRound, key, oldValue, newValue, changedByEmployeeId, changedByPositionId));
        }
    }

    /// <summary>
    /// A requester resubmitting a returned request may change only their own part of the form (part 1).
    /// Values filled by approvers at later steps are not theirs to edit — they stay as the approvers
    /// left them and the steps of the new round confirm or change them.
    /// </summary>
    private void EnsureOnlyRequesterPartChanged(IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after)
    {
        foreach (var field in WorkflowFieldCatalog.GetFields(WorkflowType).Where(f => f.Part > 1))
        {
            var oldValue = Normalize(before.TryGetValue(field.Key, out var o) ? o : null);
            var newValue = Normalize(after.TryGetValue(field.Key, out var n) ? n : null);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                throw new InvalidOperationException("Only the requester's part of the form can be changed when resubmitting.");
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

    private void RecordDecision(WorkflowRequestStep step, WorkflowStepStatus outcome, Guid actingEmployeeId, string? comment, int? returnTargetOrder = null, WorkflowActingAuthority? authority = null) =>
        StepDecisions.Add(new WorkflowStepDecision(Id, step.Id, step.Round, step.Order, step.ApproverPositionId, outcome, actingEmployeeId, comment, returnTargetOrder, authority));

    /// <summary>
    /// The step to act on: it must belong to the live group of a request that is still pending. In a
    /// parallel group each position acts on its own step; a step already answered is refused by the step itself.
    /// </summary>
    private WorkflowRequestStep RequireActionableStep(Guid stepId)
    {
        if (Status != WorkflowRequestStatus.PendingApproval)
            throw new InvalidOperationException("This request is no longer pending approval.");
        return CurrentSteps.SingleOrDefault(x => x.Id == stepId)
            ?? throw new InvalidOperationException("Only the request's current step can be acted on.");
    }

    /// <summary>Null becomes an empty path, which the main constructor/Resubmit then refuse ("needs at least one approval step"), as a null always was.</summary>
    private static IReadOnlyList<WorkflowResolvedStep> ToResolvedSteps(IReadOnlyList<(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId)>? steps) =>
        steps is null
            ? []
            : steps.Select(x => new WorkflowResolvedStep(x.StepDefinitionId, x.Order, x.Name, x.ApproverPositionId)).ToList();

    /// <summary>
    /// Steps sharing an order are one parallel group: they must agree on mode and reject policy and
    /// name distinct positions (a position cannot be asked to approve twice side by side).
    /// </summary>
    private static void ValidateParallelGroups(IReadOnlyList<WorkflowResolvedStep> resolvedSteps)
    {
        foreach (var group in resolvedSteps.GroupBy(x => x.Order).Where(g => g.Count() > 1))
        {
            var first = group.First();
            if (group.Any(x => x.Mode != first.Mode || x.RejectPolicy != first.RejectPolicy))
                throw new ArgumentException($"The parallel steps at order {group.Key} must share one approval mode and reject policy.", nameof(resolvedSteps));
            if (group.Select(x => x.ApproverPositionId).Distinct().Count() != group.Count())
                throw new ArgumentException($"The parallel steps at order {group.Key} must name different positions.", nameof(resolvedSteps));
        }
    }
}

/// <summary>One entry in a <see cref="WorkflowRequest"/>'s approval trail, scoped to a single <see cref="Round"/>.</summary>
public sealed class WorkflowRequestStep
{
    private WorkflowRequestStep()
    {
    }

    internal WorkflowRequestStep(Guid requestId, Guid stepDefinitionId, int round, int order, string name, Guid approverPositionId,
        ParallelApprovalMode approvalMode = ParallelApprovalMode.All, ParallelRejectPolicy rejectPolicy = ParallelRejectPolicy.Immediate, int? fillsPart = null)
    {
        RequestId = requestId;
        StepDefinitionId = stepDefinitionId;
        Round = round;
        Order = order;
        Name = name;
        ApproverPositionId = approverPositionId;
        ApprovalMode = approvalMode;
        RejectPolicy = rejectPolicy;
        FillsPart = fillsPart;
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

    /// <summary>
    /// How this step's group is satisfied, copied from the workflow when the request was submitted so
    /// later edits to the workflow cannot change a request in flight. Identical across the group;
    /// meaningless for a step alone at its order.
    /// </summary>
    public ParallelApprovalMode ApprovalMode { get; private set; } = ParallelApprovalMode.All;

    /// <summary>What a rejection in this step's group does (§14.3) — a submission-time copy, like <see cref="ApprovalMode"/>.</summary>
    public ParallelRejectPolicy RejectPolicy { get; private set; } = ParallelRejectPolicy.Immediate;

    /// <summary>
    /// The form part the approver at this step fills in before approving — a submission-time copy of the
    /// workflow step's setting, like <see cref="ApprovalMode"/>, so later edits to the workflow cannot
    /// change a request in flight. Null when the step fills nothing.
    /// </summary>
    public int? FillsPart { get; private set; }
    public WorkflowStepStatus Status { get; private set; }
    public Guid? ActedByEmployeeId { get; private set; }
    public DateTime? ActedAtUtc { get; private set; }
    public string? Comment { get; private set; }

    public void Approve(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.Approved, actingEmployeeId, comment);
    public void Reject(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.Rejected, actingEmployeeId, comment);
    public void ReturnToRequester(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.ReturnedToRequester, actingEmployeeId, comment);
    public void ReturnToPreviousStep(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.ReturnedToPreviousStep, actingEmployeeId, comment);
    public void ReturnToSpecificStep(Guid actingEmployeeId, string? comment) => Act(WorkflowStepStatus.ReturnedToSpecificStep, actingEmployeeId, comment);

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

    /// <summary>
    /// A step that had approved, but whose approval no longer counts because the request was sent
    /// back to an earlier step. Who approved and when stay on the step so the trail still shows it.
    /// </summary>
    internal void SupersedeApproval()
    {
        if (Status == WorkflowStepStatus.Approved)
            Status = WorkflowStepStatus.Superseded;
    }

    /// <summary>A step that was moot only because the request was rejected waits for its turn again.</summary>
    internal void RestoreAfterReopen()
    {
        if (Status == WorkflowStepStatus.Superseded)
            Status = WorkflowStepStatus.Pending;
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

/// <summary>
/// Whose authority an approver was using when they decided (spec §15.2). Absent when they acted
/// for themselves as the holder of the step's position.
/// </summary>
/// <param name="OnBehalfOfEmployeeId">The delegator, when there is one.</param>
/// <param name="DelegationId">The delegation that gave the actor their authority.</param>
public sealed record WorkflowActingAuthority(Guid? OnBehalfOfEmployeeId, Guid DelegationId);
