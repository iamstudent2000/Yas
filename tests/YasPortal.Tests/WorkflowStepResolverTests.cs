using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowStepResolverTests
{
    // ceo
    //  └─ unitManager
    //       └─ employee
    private sealed record Org(Guid Ceo, Guid UnitManager, Guid Employee, Guid Hr, Dictionary<Guid, Guid?> Parents);

    private static Org BuildOrg()
    {
        var ceo = Guid.NewGuid();
        var unitManager = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var hr = Guid.NewGuid();
        return new Org(ceo, unitManager, employee, hr, new Dictionary<Guid, Guid?>
        {
            [ceo] = null,
            [unitManager] = ceo,
            [employee] = unitManager,
            [hr] = ceo,
        });
    }

    private static WorkflowStepDefinition Level(int order, string name, int level) => new(WorkflowTypeCode.Leave, order, name, level);

    private static WorkflowStepDefinition Fixed(int order, string name, Guid position) => new(WorkflowTypeCode.Leave, order, name, position);

    [Fact]
    public void Resolves_manager_levels_and_fixed_positions_in_order()
    {
        var org = BuildOrg();
        var result = WorkflowStepResolver.Resolve(
            [Level(1, "مدیر مستقیم", 1), Fixed(2, "منابع انسانی", org.Hr)], org.Employee, org.Parents);

        Assert.True(result.Success);
        Assert.Equal(new[] { org.UnitManager, org.Hr }, result.Steps.Select(s => s.ApproverPositionId));
        Assert.Equal(new[] { 1, 2 }, result.Steps.Select(s => s.Order));
        Assert.Equal(new[] { "مدیر مستقیم", "منابع انسانی" }, result.Steps.Select(s => s.Name));
    }

    [Fact]
    public void Steps_are_ordered_by_their_configured_order_not_input_order()
    {
        var org = BuildOrg();
        var result = WorkflowStepResolver.Resolve(
            [Fixed(2, "منابع انسانی", org.Hr), Level(1, "مدیر مستقیم", 1)], org.Employee, org.Parents);

        Assert.True(result.Success);
        Assert.Equal(new[] { "مدیر مستقیم", "منابع انسانی" }, result.Steps.Select(s => s.Name));
    }

    [Fact]
    public void A_manager_level_beyond_the_hierarchy_is_an_error_and_is_never_clamped()
    {
        var org = BuildOrg();
        // employee -> unitManager (1) -> ceo (2) -> nobody (3). The old behaviour silently used the CEO here.
        var result = WorkflowStepResolver.Resolve([Level(1, "سه سطح بالاتر", 3)], org.Employee, org.Parents);

        Assert.False(result.Success);
        Assert.Empty(result.Steps);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowResolutionErrorKind.ManagerLevelUnavailable, error.Kind);
        Assert.Equal("سه سطح بالاتر", error.StepName);
        Assert.Equal(3, error.ManagerLevel);
    }

    [Fact]
    public void A_requester_with_no_manager_is_an_error_and_the_step_is_never_skipped()
    {
        var org = BuildOrg();
        // The CEO has no parent. The old behaviour skipped this step, shortening the approval chain.
        var result = WorkflowStepResolver.Resolve([Level(1, "مدیر مستقیم", 1), Fixed(2, "منابع انسانی", org.Hr)], org.Ceo, org.Parents);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowResolutionErrorKind.ManagerLevelUnavailable, error.Kind);
        Assert.Equal("مدیر مستقیم", error.StepName);
    }

    [Fact]
    public void A_step_that_resolves_to_the_requesters_own_position_is_an_error()
    {
        var org = BuildOrg();
        // HR submitting a request whose fixed approver is HR: they would approve their own request.
        var result = WorkflowStepResolver.Resolve([Fixed(1, "منابع انسانی", org.Hr)], org.Hr, org.Parents);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowResolutionErrorKind.ApproverIsRequesterPosition, error.Kind);
        Assert.Equal("منابع انسانی", error.StepName);
    }

    [Fact]
    public void Two_steps_resolving_to_the_same_position_is_an_error()
    {
        var org = BuildOrg();
        // Employee's manager is the unit manager, and a fixed step names the unit manager too.
        var result = WorkflowStepResolver.Resolve(
            [Level(1, "مدیر مستقیم", 1), Fixed(2, "تایید مجدد مدیر", org.UnitManager)], org.Employee, org.Parents);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Equal(WorkflowResolutionErrorKind.DuplicateApprover, error.Kind);
        Assert.Equal("تایید مجدد مدیر", error.StepName);
    }

    [Fact]
    public void Every_problem_is_reported_at_once_not_just_the_first()
    {
        var org = BuildOrg();
        var result = WorkflowStepResolver.Resolve(
            [Level(1, "سه سطح بالاتر", 3), Fixed(2, "خودِ درخواست‌کننده", org.Employee)], org.Employee, org.Parents);

        Assert.False(result.Success);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Kind == WorkflowResolutionErrorKind.ManagerLevelUnavailable);
        Assert.Contains(result.Errors, e => e.Kind == WorkflowResolutionErrorKind.ApproverIsRequesterPosition);
    }

    [Fact]
    public void No_steps_at_all_is_an_error()
    {
        var org = BuildOrg();
        var result = WorkflowStepResolver.Resolve([], org.Employee, org.Parents);

        Assert.False(result.Success);
        Assert.Equal(WorkflowResolutionErrorKind.NoActiveSteps, Assert.Single(result.Errors).Kind);
    }

    [Fact]
    public void Inactive_steps_are_ignored_and_the_live_trail_is_renumbered()
    {
        var org = BuildOrg();
        var middle = Fixed(2, "منابع انسانی", org.Hr);
        middle.Deactivate();

        var result = WorkflowStepResolver.Resolve(
            [Level(1, "مدیر مستقیم", 1), middle, Level(3, "مدیر ارشد", 2)], org.Employee, org.Parents);

        Assert.True(result.Success);
        Assert.Equal(new[] { "مدیر مستقیم", "مدیر ارشد" }, result.Steps.Select(s => s.Name));
        Assert.Equal(new[] { 1, 2 }, result.Steps.Select(s => s.Order));
    }

    [Fact]
    public void Only_inactive_steps_counts_as_no_steps()
    {
        var org = BuildOrg();
        var only = Level(1, "مدیر مستقیم", 1);
        only.Deactivate();

        var result = WorkflowStepResolver.Resolve([only], org.Employee, org.Parents);

        Assert.Equal(WorkflowResolutionErrorKind.NoActiveSteps, Assert.Single(result.Errors).Kind);
    }
}
