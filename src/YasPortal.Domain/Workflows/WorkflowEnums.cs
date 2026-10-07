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

    /// <summary>
    /// This step's round was closed out by a resubmission before the step was ever reached
    /// (an earlier step in the same round already returned the request). Not an outcome
    /// anyone chose — just marks it as moot rather than leaving it looking eternally Pending.
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
}
