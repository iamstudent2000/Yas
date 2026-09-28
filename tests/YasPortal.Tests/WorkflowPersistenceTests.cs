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

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
