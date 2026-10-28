using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Application.Authorization;
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

    // ---- hand-written notifications ----

    [Fact]
    public void A_hand_written_notification_records_who_sent_it_and_who_it_is_for()
    {
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();

        var item = Notification.FromSender(sender, recipient, "علی رضایی", "  لطفا گزارش را ارسال کنید  ", "/notifications");

        Assert.Equal(NotificationKind.Manual, item.Kind);
        Assert.Equal(sender, item.SenderEmployeeId);
        Assert.Equal(recipient, item.RecipientEmployeeId);
        Assert.Null(item.RequestId);
        Assert.Equal("لطفا گزارش را ارسال کنید", item.Message);
        Assert.Equal("پیام از «علی رضایی»", item.RequestType);
        Assert.False(item.IsRead);
    }

    [Fact]
    public void Notifications_the_system_produces_have_no_sender()
    {
        var item = new Notification(Guid.NewGuid(), NotificationKind.StepReached, Guid.NewGuid(), "مرخصی", "رسید", "/requests/inbox");

        Assert.Null(item.SenderEmployeeId);
    }

    [Fact]
    public void A_hand_written_notification_cannot_be_sent_to_yourself()
    {
        var me = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Notification.FromSender(me, me, "من", "سلام", "/notifications"));
    }

    [Fact]
    public void A_hand_written_notification_needs_a_sender_and_some_text()
    {
        Assert.Throws<ArgumentException>(() => Notification.FromSender(Guid.Empty, Guid.NewGuid(), "x", "سلام", "/notifications"));
        Assert.Throws<ArgumentException>(() => Notification.FromSender(Guid.NewGuid(), Guid.NewGuid(), "x", "   ", "/notifications"));
    }

    [Fact]
    public void A_hand_written_notification_has_a_length_limit()
    {
        var limit = new string('ا', Notification.MaxMessageLength);
        var ok = Notification.FromSender(Guid.NewGuid(), Guid.NewGuid(), "x", limit, "/notifications");
        Assert.Equal(Notification.MaxMessageLength, ok.Message.Length);

        Assert.Throws<ArgumentException>(() => Notification.FromSender(Guid.NewGuid(), Guid.NewGuid(), "x", limit + "ا", "/notifications"));
    }

    [Fact]
    public async Task The_sender_survives_a_round_trip_through_the_database()
    {
        var name = Guid.NewGuid().ToString();
        var sender = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        await using (var write = Create(name))
        {
            write.Notifications.Add(Notification.FromSender(sender, recipient, "علی", "سلام", "/notifications"));
            await write.SaveChangesAsync();
        }

        await using var read = Create(name);
        var saved = await read.Notifications.SingleAsync(x => x.SenderEmployeeId == sender);
        Assert.Equal(recipient, saved.RecipientEmployeeId);
        Assert.Equal(NotificationKind.Manual, saved.Kind);
    }

    [Fact]
    public void A_message_sent_to_many_carries_the_batch_and_audience_and_can_be_removed_and_restored()
    {
        var sender = Guid.NewGuid();
        var batch = Guid.NewGuid();
        var n = Notification.FromSender(sender, Guid.NewGuid(), "علی", "سلام", "/notifications", batch, "همه کارکنان");
        Assert.Equal(batch, n.BatchId);
        Assert.Equal("همه کارکنان", n.AudienceLabel);
        Assert.False(n.IsRemoved);

        var admin = Guid.NewGuid();
        n.Remove(admin);
        var firstRemoval = n.RemovedAtUtc;
        n.Remove(Guid.NewGuid());
        Assert.True(n.IsRemoved);
        Assert.Equal(admin, n.RemovedByEmployeeId);
        Assert.Equal(firstRemoval, n.RemovedAtUtc);
        Assert.Throws<ArgumentException>(() => Notification.FromSender(sender, Guid.NewGuid(), "x", "y", "/n").Remove(Guid.Empty));

        n.Restore();
        Assert.False(n.IsRemoved);
        Assert.Null(n.RemovedByEmployeeId);
    }

    [Fact]
    public void A_very_long_audience_label_is_truncated()
    {
        var n = Notification.FromSender(Guid.NewGuid(), Guid.NewGuid(), "علی", "سلام", "/notifications", Guid.NewGuid(), new string('م', 1000));
        Assert.Equal(Notification.MaxAudienceLabelLength, n.AudienceLabel!.Length);
    }

    [Fact]
    public void The_notification_permissions_are_in_the_catalog_exactly_once()
    {
        foreach (var code in new[] { Perm.NotificationsMessageSend, Perm.NotificationsBroadcastSend, Perm.AdminNotificationsView, Perm.AdminNotificationsManage, ReportPermissions.Notifications, ReportPermissions.MyNotifications })
            Assert.Single(PermissionCatalog.All, x => x.Code == code);

        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.Codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(PermissionCatalog.All, x => Assert.DoesNotContain(string.Empty, x.OldCodes));
    }

    private static ApplicationDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options);
}
