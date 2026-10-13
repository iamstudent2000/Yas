namespace YasPortal.Domain.Workflows;

/// <summary>
/// The fixed set of workflow types the system supports. Types and their fields are
/// fixed in code (see <see cref="WorkflowFieldCatalog"/>); only the approval
/// <see cref="WorkflowStepDefinition"/> steps for each type are admin-configurable.
/// </summary>
public enum WorkflowTypeCode
{
    Leave,
    Purchase,
    Access,
    Loan,
    Helpdesk,
    WorkReport,
}

/// <summary>How a single fixed field on a workflow type should be presented and validated.</summary>
public enum WorkflowFieldType
{
    Text,
    TextArea,
    Number,
    Date,
    Select,
}

/// <summary>How a workflow step's approver is resolved when a request reaches that step.</summary>
public enum ApproverRuleKind
{
    /// <summary>N levels up the requester's own position hierarchy (1 = their direct manager's position).</summary>
    RequesterManagerLevel,

    /// <summary>Always the same fixed position, regardless of who the requester is.</summary>
    SpecificPosition,

    /// <summary>
    /// Escalates up the requester's own chain to a fixed depth counted from the top of the
    /// organization tree (the top position is level 1, its direct children level 2, and so on).
    /// Expands into one sequential approval per position on the way up — the requester's direct
    /// manager, any middle managers, and finally the position at that level — before the
    /// workflow moves on to its next step.
    /// </summary>
    EscalateToTreeLevel,
}

/// <summary>
/// How a group of parallel steps (sibling steps sharing one <c>Order</c>, spec §12.2/§12.4/§14) is
/// satisfied. A group with a single step behaves the same either way.
/// </summary>
public enum ParallelApprovalMode
{
    /// <summary>All positions in the group must approve (spec §14.2).</summary>
    All,

    /// <summary>The first approval completes the group; the positions that have not answered yet are no longer needed (spec §14.1).</summary>
    Any,
}

/// <summary>What a rejection inside a parallel group does (spec §14.3).</summary>
public enum ParallelRejectPolicy
{
    /// <summary>The first rejection closes the request at once, even if other positions have not answered.</summary>
    Immediate,

    /// <summary>
    /// A rejection is recorded but the request stays open until every position in the group has
    /// answered. It is rejected only if the group ends up unsatisfied; with <see cref="ParallelApprovalMode.Any"/>
    /// a later approval still completes the group.
    /// </summary>
    WaitForAll,
}

/// <summary>Overall lifecycle of a workflow request.</summary>
public enum WorkflowRequestStatus
{
    PendingApproval,
    Approved,
    Rejected,
    ReturnedToRequester,
    Cancelled,
}

/// <summary>Outcome of a single step in a request's approval trail.</summary>
public enum WorkflowStepStatus
{
    Pending,
    Approved,
    Rejected,
    ReturnedToRequester,
    ReturnedToPreviousStep,

    /// <summary>The approver sent the request back to a chosen earlier step (spec §13.2).</summary>
    ReturnedToSpecificStep,

    /// <summary>
    /// The step was never reached: its round ended first — by a resubmission after a return, a
    /// rejection, a cancellation or a force-close. Not an outcome anyone chose — it only marks
    /// the step as moot rather than leaving it looking eternally Pending.
    /// </summary>
    Superseded,
}

/// <summary>Lifecycle actions the requester themselves take on a request (shown in the approval timeline).</summary>
public enum WorkflowRequesterActionKind
{
    /// <summary>Initial submission that opened round 1.</summary>
    Submitted,

    /// <summary>Resubmission that opened a new round after a return.</summary>
    Resubmitted,

    /// <summary>Requester cancelled the request.</summary>
    Cancelled,

    /// <summary>
    /// Someone holding the reopen permission (SuperAdmin by default) reopened a rejected request
    /// (spec §13.4). Not a requester action as such — it is recorded here because it is a lifecycle
    /// event outside the approvers' decisions; the reason is stored on the record.
    /// </summary>
    Reopened,
}
