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
