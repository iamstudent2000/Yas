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
}
