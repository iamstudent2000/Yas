using Xunit;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>
/// The "escalate to tree level N" step: the request climbs from the requester's direct manager up
/// through every middle manager to the position at level N counted from the top of the tree
/// (top = level 1), then carries on to the workflow's next step.
/// </summary>
public class WorkflowTreeLevelStepTests
{
    // root      (level 1)
    //  └─ director (level 2)
    //       └─ head   (level 3)
    //            └─ lead  (level 4)
    //                 └─ staff (level 5)
    private sealed record Org(Guid Root, Guid Director, Guid Head, Guid Lead, Guid Staff, Guid Hr, Dictionary<Guid, Guid?> Parents);

    private static Org BuildOrg()
    {
        var root = Guid.NewGuid();
        var director = Guid.NewGuid();
        var head = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var staff = Guid.NewGuid();
        var hr = Guid.NewGuid();
        return new Org(root, director, head, lead, staff, hr, new Dictionary<Guid, Guid?>
        {
            [root] = null,
            [director] = root,
            [head] = director,
            [lead] = head,
            [staff] = lead,
            [hr] = root,
        });
    }

    private static WorkflowStepDefinition ToLevel(int order, string name, int level) =>
        WorkflowStepDefinition.EscalatingToTreeLevel(WorkflowTypeCode.Leave, order, name, level);

    private static WorkflowStepDefinition Fixed(int order, string name, Guid position) => new(WorkflowTypeCode.Leave, order, name, position);

    private static WorkflowStepDefinition Manager(int order, string name, int level) => new(WorkflowTypeCode.Leave, order, name, level);

    // ---- resolver ----

    [Fact]
    public void Escalation_passes_every_middle_manager_and_ends_at_the_target_level()
    {
        var org = BuildOrg();
        var definition = ToLevel(1, "تایید مدیران", 2);

        var result = WorkflowStepResolver.Resolve([definition], org.Staff, org.Parents);

        Assert.True(result.Success);
        // staff -> lead -> head -> director: direct manager first, level-2 position last.
        Assert.Equal(new[] { org.Lead, org.Head, org.Director }, result.Steps.Select(s => s.ApproverPositionId));
        Assert.Equal(new[] { 1, 2, 3 }, result.Steps.Select(s => s.Order));
        Assert.All(result.Steps, s => Assert.Equal(definition.Id, s.StepDefinitionId));
    }

    [Fact]
    public void Expanded_steps_are_numbered_so_they_can_be_told_apart()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve([ToLevel(1, "تایید مدیران", 2)], org.Staff, org.Parents);

        Assert.Equal(
            new[] { "تایید مدیران (1/3)", "تایید مدیران (2/3)", "تایید مدیران (3/3)" },
            result.Steps.Select(s => s.Name));
    }

    [Fact]
    public void The_workflow_moves_on_to_its_next_step_after_the_escalation()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve(
            [ToLevel(1, "تایید مدیران", 2), Fixed(2, "منابع انسانی", org.Hr)], org.Staff, org.Parents);

        Assert.True(result.Success);
        Assert.Equal(new[] { org.Lead, org.Head, org.Director, org.Hr }, result.Steps.Select(s => s.ApproverPositionId));
        // Renumbered 1..4 across the expansion, so the next step is not left with a gap or a clash.
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Steps.Select(s => s.Order));
    }

    [Fact]
    public void Escalation_can_sit_between_other_steps()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve(
            [Manager(1, "مدیر مستقیم", 1), ToLevel(2, "تایید مدیران", 3), Fixed(3, "منابع انسانی", org.Hr)], org.Staff, org.Parents);

        Assert.True(result.Success);
        // Direct manager, then lead's chain up to level 3 (lead, head), then HR.
        Assert.Equal(new[] { org.Lead, org.Lead, org.Head, org.Hr }, result.Steps.Select(s => s.ApproverPositionId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Steps.Select(s => s.Order));
    }

    [Fact]
    public void A_requester_one_level_below_the_target_gets_a_single_unnumbered_step()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve([ToLevel(1, "تایید مدیران", 2)], org.Head, org.Parents);

        Assert.True(result.Success);
        var step = Assert.Single(result.Steps);
        Assert.Equal(org.Director, step.ApproverPositionId);
        Assert.Equal("تایید مدیران", step.Name);
    }

    [Fact]
    public void A_requester_already_at_the_target_level_approves_at_their_own_position()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve([ToLevel(1, "تایید مدیران", 2)], org.Director, org.Parents);

        Assert.True(result.Success);
        Assert.Equal(org.Director, Assert.Single(result.Steps).ApproverPositionId);
    }

    [Fact]
    public void A_requester_above_the_target_level_is_an_error_and_the_step_is_not_skipped()
    {
        var org = BuildOrg();

        // The top position is level 1, so there is no level 2 above it to escalate to.
        var result = WorkflowStepResolver.Resolve([ToLevel(1, "تایید مدیران", 2), Fixed(2, "منابع انسانی", org.Hr)], org.Root, org.Parents);

        Assert.False(result.Success);
        Assert.Empty(result.Steps);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowResolutionErrorKind.TreeLevelUnavailable, error.Kind);
        Assert.Equal("تایید مدیران", error.StepName);
        Assert.Equal(2, error.TreeLevel);
    }

    [Fact]
    public void Level_one_escalates_all_the_way_to_the_top_of_the_tree()
    {
        var org = BuildOrg();

        var result = WorkflowStepResolver.Resolve([ToLevel(1, "تایید تا بالاترین", 1)], org.Staff, org.Parents);

        Assert.Equal(new[] { org.Lead, org.Head, org.Director, org.Root }, result.Steps.Select(s => s.ApproverPositionId));
    }

    [Fact]
    public void Different_requesters_get_different_chain_lengths_from_the_same_definition()
    {
        var org = BuildOrg();
        var definition = ToLevel(1, "تایید مدیران", 2);

        var fromStaff = WorkflowStepResolver.Resolve([definition], org.Staff, org.Parents);
        var fromLead = WorkflowStepResolver.Resolve([definition], org.Lead, org.Parents);

        Assert.Equal(3, fromStaff.Steps.Count);
        Assert.Equal(2, fromLead.Steps.Count);
        Assert.Equal(new[] { org.Head, org.Director }, fromLead.Steps.Select(s => s.ApproverPositionId));
    }

    [Fact]
    public void An_inactive_escalation_step_is_ignored()
    {
        var org = BuildOrg();
        var escalation = ToLevel(1, "تایید مدیران", 2);
        escalation.Deactivate();

        var result = WorkflowStepResolver.Resolve([escalation, Fixed(2, "منابع انسانی", org.Hr)], org.Staff, org.Parents);

        Assert.Equal(new[] { org.Hr }, result.Steps.Select(s => s.ApproverPositionId));
    }

    // ---- ancestor chain helper ----

    [Fact]
    public void Ancestor_chain_lists_managers_nearest_first_up_to_the_root()
    {
        var org = BuildOrg();

        Assert.Equal(new[] { org.Lead, org.Head, org.Director, org.Root }, PositionHierarchy.GetAncestorChain(org.Staff, org.Parents));
    }

    [Fact]
    public void Ancestor_chain_of_the_root_is_empty()
    {
        var org = BuildOrg();

        Assert.Empty(PositionHierarchy.GetAncestorChain(org.Root, org.Parents));
    }

    [Fact]
    public void Ancestor_chain_stops_at_a_parent_cycle_instead_of_looping_forever()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };

        var chain = PositionHierarchy.GetAncestorChain(a, parents);

        Assert.Equal(new[] { b }, chain);
    }

    // ---- step definition ----

    [Fact]
    public void A_tree_level_step_records_its_level_and_no_other_approver_data()
    {
        var step = ToLevel(3, "تایید مدیران", 2);

        Assert.Equal(ApproverRuleKind.EscalateToTreeLevel, step.ApproverRuleKind);
        Assert.Equal(2, step.TreeLevel);
        Assert.Equal(3, step.Order);
        Assert.Null(step.ManagerLevel);
        Assert.Null(step.ApproverPositionId);
        Assert.True(step.IsActive);
    }

    [Fact]
    public void A_tree_level_step_rejects_invalid_levels_orders_and_names()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ToLevel(1, "x", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToLevel(0, "x", 2));
        Assert.Throws<ArgumentException>(() => ToLevel(1, "  ", 2));
    }

    [Fact]
    public void Switching_between_rule_kinds_clears_the_other_kinds_data()
    {
        var step = Manager(1, "تایید", 1);

        step.UseTreeLevel(2);
        Assert.Equal(ApproverRuleKind.EscalateToTreeLevel, step.ApproverRuleKind);
        Assert.Equal(2, step.TreeLevel);
        Assert.Null(step.ManagerLevel);
        Assert.Null(step.ApproverPositionId);

        var position = Guid.NewGuid();
        step.UseSpecificPosition(position);
        Assert.Equal(ApproverRuleKind.SpecificPosition, step.ApproverRuleKind);
        Assert.Equal(position, step.ApproverPositionId);
        Assert.Null(step.TreeLevel);
        Assert.Null(step.ManagerLevel);

        step.UseTreeLevel(3);
        step.UseManagerLevel(2);
        Assert.Equal(ApproverRuleKind.RequesterManagerLevel, step.ApproverRuleKind);
        Assert.Equal(2, step.ManagerLevel);
        Assert.Null(step.TreeLevel);
        Assert.Null(step.ApproverPositionId);
    }
}
