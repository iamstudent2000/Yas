namespace YasPortal.Domain.Workflows;

/// <summary>
/// One step in a <see cref="WorkflowTypeCode"/>'s approval path. Steps are ordered
/// (<see cref="Order"/> starting at 1) and admin-managed: an admin decides, for example,
/// that "Leave" goes to the requester's direct manager (level 1) and then to a fixed
/// HR Manager position, while "Helpdesk" goes straight to a fixed Helpdesk position.
/// </summary>
public sealed class WorkflowStepDefinition
{
    private WorkflowStepDefinition()
    {
    }

    public WorkflowStepDefinition(WorkflowTypeCode workflowType, int order, string name, int managerLevel)
    {
        WorkflowType = workflowType;
        Name = RequireName(name);
        ApproverRuleKind = ApproverRuleKind.RequesterManagerLevel;
        SetOrder(order);
        SetManagerLevel(managerLevel);
    }

    public WorkflowStepDefinition(WorkflowTypeCode workflowType, int order, string name, Guid approverPositionId)
    {
        WorkflowType = workflowType;
        Name = RequireName(name);
        ApproverRuleKind = ApproverRuleKind.SpecificPosition;
        SetOrder(order);
        SetApproverPosition(approverPositionId);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public WorkflowTypeCode WorkflowType { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; } = null!;
    public ApproverRuleKind ApproverRuleKind { get; private set; }

    /// <summary>Set when <see cref="ApproverRuleKind"/> is <see cref="ApproverRuleKind.RequesterManagerLevel"/>.</summary>
    public int? ManagerLevel { get; private set; }

    /// <summary>Set when <see cref="ApproverRuleKind"/> is <see cref="ApproverRuleKind.SpecificPosition"/>.</summary>
    public Guid? ApproverPositionId { get; private set; }

    /// <summary>
    /// Set when <see cref="ApproverRuleKind"/> is <see cref="ApproverRuleKind.EscalateToTreeLevel"/>:
    /// the depth to escalate to, counted from the top of the organization tree (1 = the top position,
    /// 2 = its direct children, ...).
    /// </summary>
    public int? TreeLevel { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether the approver of this step may hand their authority to a delegate (spec §15.1). Allowed
    /// unless the SuperAdmin switches it off; a SuperAdmin-arranged replacement still works either way (§16.2).
    /// </summary>
    public bool AllowDelegation { get; private set; } = true;

    public void SetAllowDelegation(bool allow) => AllowDelegation = allow;

    /// <summary>
    /// The part of the type's multi-part form (see <see cref="WorkflowPartDefinition"/>) the approver at
    /// this step fills in before approving, or null when the step fills nothing. Part 1 is always the
    /// requester's, so only parts 2 and up can be set. A part needs exactly one single-approver step
    /// (not a parallel group, not an escalation) — enforced when the editor saves and again when a
    /// request is submitted.
    /// </summary>
    public int? FillsPart { get; private set; }

    public void SetFillsPart(int? part)
    {
        if (part is not null && !WorkflowFieldCatalog.GetParts(WorkflowType).Any(p => p.Part == part && p.Part > 1))
            throw new ArgumentOutOfRangeException(nameof(part), "This workflow type has no such form part to fill at a step.");
        FillsPart = part;
    }

    /// <summary>
    /// How this step's group is satisfied when it shares its <see cref="Order"/> with sibling
    /// definitions (parallel step, spec §12.2/§14). Every definition in a group must carry the same
    /// value; a definition alone at its order ignores it. Defaults to <see cref="ParallelApprovalMode.All"/>.
    /// </summary>
    public ParallelApprovalMode ApprovalMode { get; private set; } = ParallelApprovalMode.All;

    /// <summary>What a rejection inside the group does (spec §14.3). Same rule as <see cref="ApprovalMode"/>: identical across the group.</summary>
    public ParallelRejectPolicy RejectPolicy { get; private set; } = ParallelRejectPolicy.Immediate;

    public void SetParallelPolicy(ParallelApprovalMode approvalMode, ParallelRejectPolicy rejectPolicy)
    {
        if (!Enum.IsDefined(approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        if (!Enum.IsDefined(rejectPolicy))
            throw new ArgumentOutOfRangeException(nameof(rejectPolicy));
        ApprovalMode = approvalMode;
        RejectPolicy = rejectPolicy;
    }

    /// <summary>
    /// Creates a step that escalates up the requester's chain to <paramref name="treeLevel"/> from
    /// the top of the tree (see <see cref="ApproverRuleKind.EscalateToTreeLevel"/>). A factory rather
    /// than a constructor because its parameters would clash with the manager-level constructor.
    /// </summary>
    public static WorkflowStepDefinition EscalatingToTreeLevel(WorkflowTypeCode workflowType, int order, string name, int treeLevel)
    {
        var step = new WorkflowStepDefinition
        {
            WorkflowType = workflowType,
            Name = RequireName(name),
            ApproverRuleKind = ApproverRuleKind.EscalateToTreeLevel,
        };
        step.SetOrder(order);
        step.SetTreeLevel(treeLevel);
        return step;
    }

    public void Rename(string name) => Name = RequireName(name);

    public void SetOrder(int order)
    {
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order), "Step order must start at 1.");
        Order = order;
    }

    public void UseManagerLevel(int managerLevel)
    {
        ApproverRuleKind = ApproverRuleKind.RequesterManagerLevel;
        ApproverPositionId = null;
        TreeLevel = null;
        SetManagerLevel(managerLevel);
    }

    public void UseSpecificPosition(Guid positionId)
    {
        ApproverRuleKind = ApproverRuleKind.SpecificPosition;
        ManagerLevel = null;
        TreeLevel = null;
        SetApproverPosition(positionId);
    }

    public void UseTreeLevel(int treeLevel)
    {
        ApproverRuleKind = ApproverRuleKind.EscalateToTreeLevel;
        ManagerLevel = null;
        ApproverPositionId = null;
        SetTreeLevel(treeLevel);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private void SetManagerLevel(int managerLevel)
    {
        if (managerLevel < 1)
            throw new ArgumentOutOfRangeException(nameof(managerLevel), "Manager level must be at least 1.");
        ManagerLevel = managerLevel;
    }

    private void SetTreeLevel(int treeLevel)
    {
        if (treeLevel < 1)
            throw new ArgumentOutOfRangeException(nameof(treeLevel), "Tree level must be at least 1.");
        TreeLevel = treeLevel;
    }

    private void SetApproverPosition(Guid positionId)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Approver position is required.", nameof(positionId));
        ApproverPositionId = positionId;
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Step name is required.", nameof(name));
        return name.Trim();
    }
}
