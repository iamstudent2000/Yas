namespace YasPortal.Domain.Workflows;

/// <summary>The time rules behind the "waiting too long" reminders (see <c>WaitingReminderSetting</c>).</summary>
public static class WaitingReminders
{
    /// <summary>
    /// Since when a pending request has been waiting at its current step: the later of the start of the current round and the
    /// last decision made in it (the moment the previous step passed it on). Needs <see cref="WorkflowRequest.RequesterActions"/>
    /// and <see cref="WorkflowRequest.StepDecisions"/> loaded; without them it falls back to the creation time.
    /// </summary>
    public static DateTime WaitingSince(WorkflowRequest request)
    {
        var roundStart = request.RequesterActions
            .Where(a => a.Round == request.CurrentRound && a.Kind is WorkflowRequesterActionKind.Submitted or WorkflowRequesterActionKind.Resubmitted)
            .Select(a => (DateTime?)a.AtUtc)
            .Max() ?? request.CreatedAtUtc;
        var lastDecision = request.StepDecisions
            .Where(d => d.Round == request.CurrentRound)
            .Select(d => (DateTime?)d.ActedAtUtc)
            .Max();
        return lastDecision is DateTime decided && decided > roundStart ? decided : roundStart;
    }

    /// <summary>Whether a request that has waited since <paramref name="sinceUtc"/> has reached the person's threshold.</summary>
    public static bool IsDue(DateTime sinceUtc, int afterHours, DateTime nowUtc) => nowUtc - sinceUtc >= TimeSpan.FromHours(afterHours);
}
