using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

/// <summary>Who gets notified for each workflow event (spec §17.2), and the notification record itself (§17.3, §17.4).</summary>
public class NotificationTests
{
    private static readonly Guid Position1 = Guid.NewGuid();
    private static readonly Guid Position2 = Guid.NewGuid();

    private static WorkflowRequest NewRequest() =>
        new(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[]
            {
                (Guid.NewGuid(), 1, "مدیر مستقیم", Position1),
                (Guid.NewGuid(), 2, "منابع انسانی", Position2),
            });

    private static NotificationTarget Single(WorkflowRequest request, WorkflowNotificationEvent evt) =>
        Assert.Single(WorkflowNotificationPlanner.Plan(request, evt));

    // ---- planner ----

    [Fact]
    public void Submitting_notifies_the_first_steps_position()
    {
        var request = NewRequest();

        var target = Single(request, WorkflowNotificationEvent.Submitted);

        Assert.False(target.IsRequester);
        Assert.Equal(Position1, target.PositionId);
        Assert.Equal("مدیر مستقیم", target.StepName);
        Assert.Equal(NotificationKind.StepReached, target.Kind);
    }

    [Fact]
    public void An_approval_that_is_not_the_last_notifies_the_next_steps_position()
    {
        var request = NewRequest();
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);

        var target = Single(request, WorkflowNotificationEvent.StepApproved);

        Assert.Equal(Position2, target.PositionId);
        Assert.Equal(NotificationKind.StepReached, target.Kind);
    }

    [Fact]
    public void The_final_approval_notifies_the_requester()
    {
        var request = NewRequest();
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);

        var target = Single(request, WorkflowNotificationEvent.StepApproved);

        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestApproved, target.Kind);
    }

    [Fact]
    public void Rejecting_notifies_the_requester()
    {
        var request = NewRequest();
        request.Reject(request.CurrentStep.Id, Guid.NewGuid(), null);

        var target = Single(request, WorkflowNotificationEvent.Rejected);

        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestRejected, target.Kind);
    }

    [Fact]
    public void Returning_to_the_requester_notifies_the_requester()
    {
        var request = NewRequest();
        request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), "ناقص");

        var target = Single(request, WorkflowNotificationEvent.ReturnedToRequester);

        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestReturned, target.Kind);
    }

    [Fact]
    public void Returning_to_a_previous_step_notifies_that_steps_position()
    {
        var request = NewRequest();
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.ReturnToPreviousStep(request.CurrentStep.Id, Guid.NewGuid(), "دوباره");

        var target = Single(request, WorkflowNotificationEvent.ReturnedToPreviousStep);

        Assert.False(target.IsRequester);
        Assert.Equal(Position1, target.PositionId);
        Assert.Equal(NotificationKind.ReturnedToStep, target.Kind);
    }

    [Fact]
    public void Returning_to_previous_from_the_first_step_falls_back_to_notifying_the_requester()
    {
        var request = NewRequest();
        request.ReturnToPreviousStep(request.CurrentStep.Id, Guid.NewGuid(), null);

        var target = Single(request, WorkflowNotificationEvent.ReturnedToPreviousStep);

        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestReturned, target.Kind);
    }

    [Fact]
    public void Resubmitting_notifies_the_first_steps_position_again()
    {
        var request = NewRequest();
        request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.Resubmit("{\"a\":\"b\"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Position1) });

        var target = Single(request, WorkflowNotificationEvent.Resubmitted);

        Assert.Equal(Position1, target.PositionId);
        Assert.Equal(NotificationKind.StepReached, target.Kind);
    }

    [Fact]
    public void Force_closing_notifies_the_requester()
    {
        var request = NewRequest();
        request.ForceClose(Guid.NewGuid(), "دلیل");

        var target = Single(request, WorkflowNotificationEvent.ForceClosed);

        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestForceClosed, target.Kind);
    }

    // ---- the record ----

    [Fact]
    public void A_notification_starts_unread_and_can_be_marked_read_once()
    {
        var item = new Notification(Guid.NewGuid(), NotificationKind.StepReached, Guid.NewGuid(), "مرخصی", "پیام", "/requests/inbox");

        Assert.False(item.IsRead);
        Assert.Null(item.ReadAtUtc);

        item.MarkRead();
        var readAt = item.ReadAtUtc;
        item.MarkRead();

        Assert.True(item.IsRead);
        Assert.NotNull(readAt);
        Assert.Equal(readAt, item.ReadAtUtc);
    }

    [Fact]
    public void A_notification_requires_a_recipient_a_message_and_a_link()
    {
        Assert.Throws<ArgumentException>(() => new Notification(Guid.Empty, NotificationKind.StepReached, null, "x", "m", "/l"));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationKind.StepReached, null, "x", " ", "/l"));
        Assert.Throws<ArgumentException>(() => new Notification(Guid.NewGuid(), NotificationKind.StepReached, null, "x", "m", ""));
    }

    [Fact]
    public async Task Notifications_round_trip_through_the_database_and_are_not_audited()
    {
        var name = Guid.NewGuid().ToString();
        var recipient = Guid.NewGuid();
        await using (var write = Create(name))
        {
            write.Notifications.Add(new Notification(recipient, NotificationKind.VacantPosition, Guid.NewGuid(), "خرید", "سمت خالی است", "/admin/active-requests"));
            await write.SaveChangesAsync();
        }

        await using var read = Create(name);
        var saved = await read.Notifications.SingleAsync(x => x.RecipientEmployeeId == recipient);
        Assert.Equal(NotificationKind.VacantPosition, saved.Kind);
        Assert.False(saved.IsRead);
        Assert.DoesNotContain(read.AuditEntries, a => a.EntityType == nameof(Notification));
    }

    private static ApplicationDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options);
}
