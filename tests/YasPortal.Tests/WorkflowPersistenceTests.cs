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

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
