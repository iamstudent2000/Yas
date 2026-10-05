using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

public class WorkflowPersistenceTests
{
    [Fact]
    public async Task Resubmitting_a_returned_request_inserts_the_new_rounds_steps_instead_of_updating_them()
    {
        // Regression: new steps added to an already-tracked request carry a client-generated
        // Guid key. Without ValueGeneratedNever, EF treats them as existing rows (Modified),
        // issues an UPDATE that matches 0 rows, and throws DbUpdateConcurrencyException.
        var databaseName = Guid.NewGuid().ToString();
        Guid requestId;

        await using (var setup = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
                new[]
                {
                    (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()),
                    (Guid.NewGuid(), 2, "منابع انسانی", Guid.NewGuid()),
                });
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
        }

        await using (var returning = CreateContext(databaseName))
        {
            var request = await returning.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), "اصلاح شود");
            await returning.SaveChangesAsync();
        }

        await using (var resubmitting = CreateContext(databaseName))
        {
            var request = await resubmitting.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });
            await resubmitting.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName);
        var saved = await verify.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
        Assert.Equal(2, saved.CurrentRound);
        Assert.Equal(3, saved.Steps.Count);
        Assert.Equal(WorkflowStepStatus.Pending, saved.CurrentStep.Status);
    }

    [Fact]
    public async Task A_stale_decision_is_rejected_instead_of_overwriting_the_first_one()
    {
        // Two sessions load the same pending request. The first approves it; the second (stale)
        // tries to reject it. Without the Revision concurrency token the second write would win.
        var databaseName = Guid.NewGuid().ToString();
        Guid requestId;

        await using (var setup = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
                new[]
                {
                    (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()),
                    (Guid.NewGuid(), 2, "منابع انسانی", Guid.NewGuid()),
                });
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
        }

        await using var first = CreateContext(databaseName);
        await using var second = CreateContext(databaseName);
        var a = await first.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
        var b = await second.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);

        a.Approve(a.CurrentStep.Id, Guid.NewGuid(), null);
        await first.SaveChangesAsync();

        b.Reject(b.CurrentStep.Id, Guid.NewGuid(), null);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verify = CreateContext(databaseName);
        var saved = await verify.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, saved.Status);
        Assert.Equal(2, saved.CurrentStepOrder);
    }

    [Fact]
    public async Task Requester_actions_are_inserted_on_submit_resubmit_and_cancel_without_loading_them()
    {
        // Each step runs in a fresh context that loads only Steps (like the pages do), so the new
        // actions are added to an unloaded collection. They must be INSERTed, never UPDATEd.
        var databaseName = Guid.NewGuid().ToString();
        var requester = Guid.NewGuid();
        Guid requestId;

        await using (var submit = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, requester, Guid.NewGuid(), "{}",
                new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()), (Guid.NewGuid(), 2, "منابع انسانی", Guid.NewGuid()) });
            submit.WorkflowRequests.Add(request);
            await submit.SaveChangesAsync();
            requestId = request.Id;
        }

        await using (var returning = CreateContext(databaseName))
        {
            var request = await returning.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), "اصلاح شود");
            await returning.SaveChangesAsync();
        }

        await using (var resubmitting = CreateContext(databaseName))
        {
            var request = await resubmitting.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });
            await resubmitting.SaveChangesAsync();
        }

        await using (var cancelling = CreateContext(databaseName))
        {
            var request = await cancelling.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Cancel();
            await cancelling.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName);
        var saved = await verify.WorkflowRequests.Include(x => x.RequesterActions).SingleAsync(x => x.Id == requestId);
        var actions = saved.RequesterActions.OrderBy(x => x.AtUtc).ToList();
        Assert.Equal(new[] { WorkflowRequesterActionKind.Submitted, WorkflowRequesterActionKind.Resubmitted, WorkflowRequesterActionKind.Cancelled }, actions.Select(x => x.Kind).ToArray());
        Assert.Equal(new[] { 1, 2, 2 }, actions.Select(x => x.Round).ToArray());
        Assert.All(actions, a => Assert.Equal(requester, a.EmployeeId));
    }

    [Fact]
    public async Task Step_decisions_survive_a_reopen_even_when_loaded_in_separate_contexts()
    {
        // Round-trips through the database in separate contexts (like the pages do), covering
        // the exact case Reopen() used to destroy: an approval, then a return-to-previous-step
        // that resets the step, then a second approval. Both decisions must still be there.
        var databaseName = Guid.NewGuid().ToString();
        var firstApprover = Guid.NewGuid();
        var secondApprover = Guid.NewGuid();
        Guid requestId, step1Id, step2Id;

        await using (var setup = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
                new[]
                {
                    (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()),
                    (Guid.NewGuid(), 2, "منابع انسانی", Guid.NewGuid()),
                });
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
            step1Id = request.Steps.Single(x => x.Order == 1).Id;
            step2Id = request.Steps.Single(x => x.Order == 2).Id;
        }

        await using (var approving1 = CreateContext(databaseName))
        {
            var request = await approving1.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Approve(step1Id, firstApprover, "بار اول");
            await approving1.SaveChangesAsync();
        }

        await using (var returning = CreateContext(databaseName))
        {
            var request = await returning.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), "برگشت");
            await returning.SaveChangesAsync();
        }

        await using (var approving2 = CreateContext(databaseName))
        {
            var request = await approving2.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Approve(step1Id, secondApprover, "بار دوم");
            await approving2.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName);
        var saved = await verify.WorkflowRequests.Include(x => x.Steps).Include(x => x.StepDecisions).SingleAsync(x => x.Id == requestId);
        var decisions = saved.StepDecisions.Where(d => d.StepId == step1Id).OrderBy(d => d.ActedAtUtc).ToList();
        Assert.Equal(2, decisions.Count);
        Assert.Equal(firstApprover, decisions[0].ActedByEmployeeId);
        Assert.Equal(secondApprover, decisions[1].ActedByEmployeeId);
        // The live step only shows the latest pass; the log shows both.
        Assert.Equal(secondApprover, saved.Steps.Single(x => x.Id == step1Id).ActedByEmployeeId);
    }

    [Fact]
    public async Task Cancel_is_refused_after_a_reopen_even_when_fetched_fresh_with_Steps_and_StepDecisions_loaded()
    {
        var databaseName = Guid.NewGuid().ToString();
        var requester = Guid.NewGuid();
        Guid requestId, step1Id, step2Id;

        await using (var setup = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, requester, Guid.NewGuid(), "{}",
                new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()), (Guid.NewGuid(), 2, "منابع انسانی", Guid.NewGuid()) });
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
            step1Id = request.Steps.Single(x => x.Order == 1).Id;
            step2Id = request.Steps.Single(x => x.Order == 2).Id;
        }

        await using (var approving = CreateContext(databaseName))
        {
            var request = await approving.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Approve(step1Id, Guid.NewGuid(), null);
            await approving.SaveChangesAsync();
        }

        await using (var returning = CreateContext(databaseName))
        {
            var request = await returning.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), null);
            await returning.SaveChangesAsync();
        }

        // Exactly what MyRequests.razor's CancelAsync does: fetch with both Includes, then cancel.
        await using var cancelling = CreateContext(databaseName);
        var tracked = await cancelling.WorkflowRequests.Include(x => x.Steps).Include(x => x.StepDecisions)
            .SingleOrDefaultAsync(x => x.Id == requestId && x.RequesterEmployeeId == requester);
        Assert.NotNull(tracked);
        Assert.False(tracked!.CanBeCancelled);
        Assert.Throws<InvalidOperationException>(() => tracked.Cancel());
    }

    [Fact]
    public async Task Resubmitting_with_edited_fields_inserts_field_change_rows()
    {
        // Same regression class as the steps test above: FieldChange rows carry client-generated
        // Guid keys, so without ValueGeneratedNever EF would try to UPDATE them and fail.
        var databaseName = Guid.NewGuid().ToString();
        var requester = Guid.NewGuid();
        var requesterPosition = Guid.NewGuid();
        Guid requestId;

        await using (var setup = CreateContext(databaseName))
        {
            var request = new WorkflowRequest(
                WorkflowTypeCode.Leave, requester, requesterPosition, "{\"reason\":\"قبلی\"}",
                new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            requestId = request.Id;
        }

        await using (var returning = CreateContext(databaseName))
        {
            var request = await returning.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), null);
            await returning.SaveChangesAsync();
        }

        // Exactly what MyRequests.razor's ResubmitAsync does: fetch with Steps only, then resubmit.
        await using (var resubmitting = CreateContext(databaseName))
        {
            var request = await resubmitting.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == requestId);
            request.Resubmit("{\"reason\":\"جدید\"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });
            await resubmitting.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName);
        var change = await verify.WorkflowFieldChanges.SingleAsync(x => x.RequestId == requestId);
        Assert.Equal("reason", change.FieldKey);
        Assert.Equal("قبلی", change.OldValue);
        Assert.Equal("جدید", change.NewValue);
        Assert.Equal(2, change.Round);
        Assert.Equal(requester, change.ChangedByEmployeeId);
        Assert.Equal(requesterPosition, change.ChangedByPositionId);
    }

    [Fact]
    public async Task Active_covers_exactly_the_requests_that_can_still_move()
    {
        // "Active" drives the §18 workflow-change block, the §22.2 blocking list and the set the
        // SuperAdmin can force-close, so pending and returned count and every terminal status doesn't.
        var databaseName = Guid.NewGuid().ToString();

        static WorkflowRequest NewRequest() => new(
            WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        var pending = NewRequest();
        var returned = NewRequest();
        returned.ReturnToRequester(returned.CurrentStep.Id, Guid.NewGuid(), null);
        var approved = NewRequest();
        approved.Approve(approved.CurrentStep.Id, Guid.NewGuid(), null);
        var rejected = NewRequest();
        rejected.Reject(rejected.CurrentStep.Id, Guid.NewGuid(), null);
        var cancelled = NewRequest();
        cancelled.Cancel();
        var forceClosed = NewRequest();
        forceClosed.ForceClose(Guid.NewGuid(), "بسته شدن دستی");

        await using (var setup = CreateContext(databaseName))
        {
            setup.WorkflowRequests.AddRange(pending, returned, approved, rejected, cancelled, forceClosed);
            await setup.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName);
        var activeIds = await verify.WorkflowRequests.Active().Select(x => x.Id).ToListAsync();

        Assert.Equal(2, activeIds.Count);
        Assert.Contains(pending.Id, activeIds);
        Assert.Contains(returned.Id, activeIds);
    }

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
