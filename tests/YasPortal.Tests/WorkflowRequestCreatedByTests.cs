using Xunit;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowRequestCreatedByTests
{
    private static readonly WorkflowResolvedStep[] Steps = { new(Guid.NewGuid(), 1, "مدیر", Guid.NewGuid()) };

    [Fact]
    public void Request_submitted_by_the_requester_has_no_creator_marker()
    {
        var requester = Guid.NewGuid();
        var r = new WorkflowRequest(WorkflowTypeCode.Purchase, requester, Guid.NewGuid(), "{}", Steps);
        Assert.Null(r.CreatedByEmployeeId);
        Assert.Equal(requester, r.RequesterActions.Single().EmployeeId);
    }

    [Fact]
    public void Request_submitted_by_an_admin_keeps_the_requester_and_records_the_admin()
    {
        var requester = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var r = new WorkflowRequest(WorkflowTypeCode.Purchase, requester, Guid.NewGuid(), "{}", Steps, createdByEmployeeId: admin);
        Assert.Equal(requester, r.RequesterEmployeeId);
        Assert.Equal(admin, r.CreatedByEmployeeId);
        Assert.Equal(admin, r.RequesterActions.Single().EmployeeId);
    }

    [Fact]
    public void Creator_equal_to_requester_is_not_recorded()
    {
        var requester = Guid.NewGuid();
        var r = new WorkflowRequest(WorkflowTypeCode.Purchase, requester, Guid.NewGuid(), "{}", Steps, createdByEmployeeId: requester);
        Assert.Null(r.CreatedByEmployeeId);
    }

    [Fact]
    public void Admin_created_request_also_notifies_the_requester()
    {
        var requester = Guid.NewGuid();
        var byAdmin = new WorkflowRequest(WorkflowTypeCode.Purchase, requester, Guid.NewGuid(), "{}", Steps, createdByEmployeeId: Guid.NewGuid());
        var targets = WorkflowNotificationPlanner.Plan(byAdmin, WorkflowNotificationEvent.Submitted);
        Assert.Contains(targets, t => t.IsRequester && t.Kind == NotificationKind.RequestCreatedForYou);
        Assert.Contains(targets, t => !t.IsRequester && t.Kind == NotificationKind.StepReached);

        var bySelf = new WorkflowRequest(WorkflowTypeCode.Purchase, requester, Guid.NewGuid(), "{}", Steps);
        Assert.DoesNotContain(WorkflowNotificationPlanner.Plan(bySelf, WorkflowNotificationEvent.Submitted), t => t.IsRequester);
    }

    [Fact]
    public void Filter_can_restrict_to_admin_created_and_by_position()
    {
        var f = new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter { CreatedByAdminOnly = true, RequesterPositionId = Guid.NewGuid() };
        Assert.False(f.IsEmpty);
        f.Clear();
        Assert.True(f.IsEmpty);
    }

    [Fact]
    public void Filter_copy_carries_every_field_and_a_picked_requester_counts_as_a_filter()
    {
        var source = new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter
        {
            RequesterEmployeeId = Guid.NewGuid(), StepName = "مدیر", IdText = "ab12", CreatedByAdminOnly = true, Type = WorkflowTypeCode.Purchase,
        };
        var copy = new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter();
        Assert.True(copy.IsEmpty);
        copy.CopyFrom(source);
        Assert.False(copy.IsEmpty);
        Assert.Equal(source.RequesterEmployeeId, copy.RequesterEmployeeId);
        Assert.Equal("مدیر", copy.StepName);
        Assert.Equal("ab12", copy.IdText);
        Assert.True(copy.CreatedByAdminOnly);
        Assert.Equal(WorkflowTypeCode.Purchase, copy.Type);
        Assert.True(new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter { RequesterEmployeeId = Guid.NewGuid() }.IsEmpty == false);
    }

    [Fact]
    public void Advanced_filters_count_as_filters_and_survive_copy_and_clear()
    {
        var f = new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter
        {
            RequesterOrganizationId = Guid.NewGuid(), WaitingAtPositionId = Guid.NewGuid(), ActedByEmployeeId = Guid.NewGuid(), Text = "لپ‌تاپ",
        };
        Assert.False(f.IsEmpty);
        var copy = new YasPortal.Infrastructure.Persistence.WorkflowRequestFilter();
        copy.CopyFrom(f);
        Assert.Equal(f.RequesterOrganizationId, copy.RequesterOrganizationId);
        Assert.Equal(f.WaitingAtPositionId, copy.WaitingAtPositionId);
        Assert.Equal(f.ActedByEmployeeId, copy.ActedByEmployeeId);
        Assert.Equal("لپ‌تاپ", copy.Text);
        copy.Clear();
        Assert.True(copy.IsEmpty);
    }
}
