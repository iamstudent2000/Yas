using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public class WorkflowRequestCancelKindFilterTests
{
    [Fact]
    public void CancelKind_counts_as_a_filter_and_is_cleared()
    {
        var f = new WorkflowRequestFilter { CancelledBy = WorkflowRequestFilter.CancelKind.ByAdmin };
        Assert.False(f.IsEmpty);
        f.Clear();
        Assert.True(f.IsEmpty);
        Assert.Null(f.CancelledBy);
    }

    [Fact]
    public void CancelKind_is_copied()
    {
        var copy = new WorkflowRequestFilter();
        copy.CopyFrom(new WorkflowRequestFilter { CancelledBy = WorkflowRequestFilter.CancelKind.ByRequester });
        Assert.Equal(WorkflowRequestFilter.CancelKind.ByRequester, copy.CancelledBy);
    }
}
