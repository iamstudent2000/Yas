namespace YasPortal.Domain.Workflows;

/// <summary>
/// The one-glance facts about a request that is waiting for approval — which stage it is at, who it is
/// waiting for and since when — so a list row can say it without opening the request. Pure, so it can be tested.
/// </summary>
/// <param name="StageNumber">1-based position of the live stage among the stages of the current round.</param>
/// <param name="StageCount">How many stages the current round has (a parallel group counts once).</param>
/// <param name="WaitingNames">Names of the steps still waiting for a decision in the live stage.</param>
/// <param name="WaitingSinceUtc">When the live stage started waiting: the latest of the round's start and the previous stage's last decision.</param>
public sealed record WorkflowRequestHeadline(int StageNumber, int StageCount, IReadOnlyList<string> WaitingNames, DateTime WaitingSinceUtc)
{
    /// <summary>Null unless the request is pending approval at a stage that still has someone to decide.</summary>
    public static WorkflowRequestHeadline? Of(WorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Status != WorkflowRequestStatus.PendingApproval)
            return null;

        var roundSteps = request.Steps.Where(s => s.Round == request.CurrentRound).ToList();
        var orders = roundSteps.Select(s => s.Order).Distinct().OrderBy(o => o).ToList();
        var index = orders.IndexOf(request.CurrentStepOrder);
        if (index < 0)
            return null;

        var waiting = roundSteps
            .Where(s => s.Order == request.CurrentStepOrder && s.Status == WorkflowStepStatus.Pending)
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (waiting.Count == 0)
            return null;

        var since = request.CreatedAtUtc;
        foreach (var action in request.RequesterActions.Where(a => a.Round == request.CurrentRound))
            if (action.AtUtc > since)
                since = action.AtUtc;
        foreach (var step in roundSteps.Where(s => s.Order < request.CurrentStepOrder && s.ActedAtUtc is not null))
            if (step.ActedAtUtc!.Value > since)
                since = step.ActedAtUtc.Value;

        return new WorkflowRequestHeadline(index + 1, orders.Count, waiting, since);
    }

    /// <summary>Whole days between <paramref name="sinceUtc"/> and <paramref name="nowUtc"/>; never negative.</summary>
    public static int WholeDaysWaiting(DateTime sinceUtc, DateTime nowUtc) =>
        nowUtc <= sinceUtc ? 0 : (int)Math.Floor((nowUtc - sinceUtc).TotalDays);
}
