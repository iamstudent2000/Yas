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
    public bool IsActive { get; private set; } = true;

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
        SetManagerLevel(managerLevel);
    }

    public void UseSpecificPosition(Guid positionId)
    {
        ApproverRuleKind = ApproverRuleKind.SpecificPosition;
        ManagerLevel = null;
        SetApproverPosition(positionId);
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    private void SetManagerLevel(int managerLevel)
    {
        if (managerLevel < 1)
            throw new ArgumentOutOfRangeException(nameof(managerLevel), "Manager level must be at least 1.");
        ManagerLevel = managerLevel;
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
