namespace YasPortal.Domain.Workflows;

// Time rules for the optional reminders that nudge a person when a request has been waiting for them too
// long.
// No database access here: the rules only compare times, which keeps them easy to explain and to test.
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
        // Start of the current round: the latest submit or resubmit (a returned-and-resent request starts a
        // new round).
        var roundStart = request.RequesterActions
            .Where(a => a.Round == request.CurrentRound && a.Kind is WorkflowRequesterActionKind.Submitted or WorkflowRequesterActionKind.Resubmitted)
            .Select(a => (DateTime?)a.AtUtc)
            .Max() ?? request.CreatedAtUtc;
        // The last decision in this round is when the previous step passed the request on to the current
        // step.
        var lastDecision = request.StepDecisions
            .Where(d => d.Round == request.CurrentRound)
            .Select(d => (DateTime?)d.ActedAtUtc)
            .Max();
        // Whichever is later wins, so the clock restarts every time the request moves to a new step.
        return lastDecision is DateTime decided && decided > roundStart ? decided : roundStart;
    }

    /// <summary>
    /// Whether a reminder should be sent now. Never before the threshold. The first reminder of a waiting period (none sent since it
    /// started) is always sent; later ones only when the person chose to repeat and the repeat interval (default: the threshold) has passed since the last one.
    /// </summary>
    public static bool ShouldSend(DateTime sinceUtc, DateTime? lastReminderUtc, int afterHours, bool repeat, DateTime nowUtc, int? repeatEveryHours = null)
    {
        // Not yet past the threshold: no reminder.
        if (!IsDue(sinceUtc, afterHours, nowUtc))
            return false;
        // No reminder sent during this waiting period yet: send the first one. Otherwise repeat only if the
        // person asked for repeats.
        if (lastReminderUtc is not DateTime last || last < sinceUtc)
            return true;
        return repeat && nowUtc - last >= TimeSpan.FromHours(repeatEveryHours ?? afterHours);
    }

    /// <summary>Whether a request that has waited since <paramref name="sinceUtc"/> has reached the person's threshold.</summary>
    public static bool IsDue(DateTime sinceUtc, int afterHours, DateTime nowUtc) => nowUtc - sinceUtc >= TimeSpan.FromHours(afterHours);
}
