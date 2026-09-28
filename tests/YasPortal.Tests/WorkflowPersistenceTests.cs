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

    private static ApplicationDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options);
    }
}
