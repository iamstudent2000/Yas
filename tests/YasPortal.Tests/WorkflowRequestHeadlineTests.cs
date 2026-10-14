using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowRequestHeadlineTests
{
    private static readonly Guid Pa = Guid.NewGuid();
    private static readonly Guid Pb = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();

    private static WorkflowResolvedStep Step(int order, string name, Guid position) => new(Guid.NewGuid(), order, name, position);

    private static WorkflowRequest Make() =>
        new(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "مدیر", Pa), Step(2, "مالی", Pb), Step(2, "حقوقی", Pc) });

    [Fact]
    public void First_stage_is_one_of_two_and_names_who_is_awaited()
    {
        var r = Make();
        var h = WorkflowRequestHeadline.Of(r)!;
        Assert.Equal((1, 2), (h.StageNumber, h.StageCount));
        Assert.Equal(new[] { "مدیر" }, h.WaitingNames);
    }

    [Fact]
    public void Parallel_group_counts_once_and_lists_only_those_still_pending()
    {
        var r = Make();
        r.Approve(r.Steps.Single(s => s.ApproverPositionId == Pa).Id, Guid.NewGuid(), null);
        r.Approve(r.Steps.Single(s => s.ApproverPositionId == Pb).Id, Guid.NewGuid(), null);

        var h = WorkflowRequestHeadline.Of(r)!;
        Assert.Equal((2, 2), (h.StageNumber, h.StageCount));
        Assert.Equal(new[] { "حقوقی" }, h.WaitingNames);
    }

    [Fact]
    public void Waiting_since_moves_forward_after_the_previous_stage_decides()
    {
        var r = Make();
        var first = WorkflowRequestHeadline.Of(r)!.WaitingSinceUtc;
        r.Approve(r.Steps.Single(s => s.ApproverPositionId == Pa).Id, Guid.NewGuid(), null);
        Assert.True(WorkflowRequestHeadline.Of(r)!.WaitingSinceUtc >= first);
    }

    [Fact]
    public void Finished_request_has_no_headline()
    {
        var r = Make();
        r.Reject(r.Steps.Single(s => s.ApproverPositionId == Pa).Id, Guid.NewGuid(), null);
        Assert.Null(WorkflowRequestHeadline.Of(r));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(36, 1)]
    [InlineData(24 * 4 + 3, 4)]
    public void Whole_days_floor_and_never_go_negative(int hoursAgo, int expected)
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, WorkflowRequestHeadline.WholeDaysWaiting(now.AddHours(-hoursAgo), now));
        Assert.Equal(0, WorkflowRequestHeadline.WholeDaysWaiting(now.AddHours(3), now));
    }
}
