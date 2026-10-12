using Xunit;
using YasPortal.Domain.Delegations;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>Position delegation (spec §15–16): validation, dates, status, and recorded authority.</summary>
public class DelegationTests
{
    private static readonly Guid Position = Guid.NewGuid();
    private static readonly Guid Holder = Guid.NewGuid();
    private static readonly Guid DelegatePosition = Guid.NewGuid();
    private static readonly Guid DelegateEmployee = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 10, 10);
    private static readonly DateTime End = new(2026, 10, 20);

    private static PositionDelegation Normal() => new(Position, Holder, DelegatePosition, Start, End, Holder, false);

    [Fact]
    public void Dates_are_inclusive_on_both_ends()
    {
        var d = Normal();
        Assert.False(d.IsInEffectOn(Start.AddDays(-1)));
        Assert.True(d.IsInEffectOn(Start));
        Assert.True(d.IsInEffectOn(End.AddHours(23)));
        Assert.False(d.IsInEffectOn(End.AddDays(1)));
    }

    [Fact]
    public void Status_follows_dates_and_revocation()
    {
        var d = Normal();
        Assert.Equal(DelegationStatus.Upcoming, d.StatusOn(Start.AddDays(-1)));
        Assert.Equal(DelegationStatus.Active, d.StatusOn(Start));
        Assert.Equal(DelegationStatus.Expired, d.StatusOn(End.AddDays(1)));
        d.Revoke(Holder);
        Assert.Equal(DelegationStatus.Revoked, d.StatusOn(Start));
        Assert.False(d.IsInEffectOn(Start));
    }

    [Fact]
    public void Revoking_twice_is_rejected()
    {
        var d = Normal();
        d.Revoke(Holder);
        Assert.Throws<InvalidOperationException>(() => d.Revoke(Holder));
    }

    [Fact]
    public void A_position_cannot_be_delegated_to_itself()
        => Assert.Throws<ArgumentException>(() => new PositionDelegation(Position, Holder, Position, Start, End, Holder, false));

    [Fact]
    public void End_before_start_is_rejected()
        => Assert.Throws<ArgumentException>(() => new PositionDelegation(Position, Holder, DelegatePosition, End, Start, Holder, false));

    [Fact]
    public void A_normal_delegation_needs_a_holder_but_an_admin_arrangement_does_not()
    {
        Assert.Throws<ArgumentException>(() => new PositionDelegation(Position, null, DelegatePosition, Start, End, Holder, false));
        var admin = new PositionDelegation(Position, null, DelegatePosition, Start, End, Guid.NewGuid(), true);
        Assert.Null(admin.DelegatorEmployeeId);
    }

    [Fact]
    public void Normal_delegation_respects_the_step_setting_but_admin_arrangement_ignores_it()
    {
        Assert.True(Normal().AppliesToStep(true));
        Assert.False(Normal().AppliesToStep(false));
        var admin = new PositionDelegation(Position, null, DelegatePosition, Start, End, Guid.NewGuid(), true);
        Assert.True(admin.AppliesToStep(false));
    }

    [Fact]
    public void Step_definitions_allow_delegation_by_default_and_can_forbid_it()
    {
        var step = new WorkflowStepDefinition(WorkflowTypeCode.Purchase, 1, "مدیر", Position);
        Assert.True(step.AllowDelegation);
        step.SetAllowDelegation(false);
        Assert.False(step.AllowDelegation);
    }

    [Fact]
    public void A_decision_made_by_a_delegate_records_who_they_acted_for()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { (Guid.NewGuid(), 1, "مدیر", Position) });
        var delegation = Normal();

        request.Approve(request.CurrentStep.Id, DelegateEmployee, "تایید", new WorkflowActingAuthority(Holder, delegation.Id));

        var decision = request.StepDecisions.Single();
        Assert.Equal(DelegateEmployee, decision.ActedByEmployeeId);
        Assert.Equal(Holder, decision.OnBehalfOfEmployeeId);
        Assert.Equal(delegation.Id, decision.DelegationId);
    }

    [Fact]
    public void A_decision_made_by_the_holder_has_no_delegation_recorded()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { (Guid.NewGuid(), 1, "مدیر", Position) });
        request.Approve(request.CurrentStep.Id, Holder, "تایید");
        var decision = request.StepDecisions.Single();
        Assert.Null(decision.OnBehalfOfEmployeeId);
        Assert.Null(decision.DelegationId);
    }
}
