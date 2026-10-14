namespace YasPortal.Domain.Workflows;

/// <summary>Where one stage of a request's approval path stands, for display (a stage is one step, or a parallel group).</summary>
public enum WorkflowStageState
{
    /// <summary>Not reached yet.</summary>
    Queued,

    /// <summary>Waiting for a decision right now.</summary>
    Awaiting,

    /// <summary>Done: every approval it needed was given (ANY: one was enough).</summary>
    Approved,

    /// <summary>Every position answered and the stage did not get its approvals.</summary>
    Rejected,

    /// <summary>A position in the stage sent the request back (to the requester or to an earlier step).</summary>
    Returned,

    /// <summary>Never decided: the request closed or went back before this stage answered, or its approvals were voided.</summary>
    Skipped,
}

/// <summary>
/// The overall state of one stage — a single step or a parallel group — with the counts a screen shows
/// ("2 of 3 approved"). Pure: the same rules the engine uses to decide whether a group is satisfied
/// (<see cref="ParallelApprovalMode"/>), so the screen cannot say "approved" when the engine says otherwise.
/// </summary>
public sealed record WorkflowStageSummary(WorkflowStageState State, int Total, int Approved, int Rejected, int Pending)
{
    /// <param name="stage">The steps of one round sharing one order.</param>
    /// <param name="isLive">Whether the request is pending approval and this is the stage it is waiting at.</param>
    public static WorkflowStageSummary Of(IReadOnlyCollection<WorkflowRequestStep> stage, bool isLive)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage.Count == 0)
            throw new ArgumentException("A stage has at least one step.", nameof(stage));

        var total = stage.Count;
        var approved = stage.Count(x => x.Status == WorkflowStepStatus.Approved);
        var rejected = stage.Count(x => x.Status == WorkflowStepStatus.Rejected);
        var pending = stage.Count(x => x.Status == WorkflowStepStatus.Pending);
        var returned = stage.Any(x => x.Status is WorkflowStepStatus.ReturnedToRequester or WorkflowStepStatus.ReturnedToPreviousStep or WorkflowStepStatus.ReturnedToSpecificStep);

        var any = stage.First().ApprovalMode == ParallelApprovalMode.Any;
        var satisfied = any ? approved > 0 : approved == total;

        var state =
            returned ? WorkflowStageState.Returned
            : satisfied ? WorkflowStageState.Approved
            : rejected > 0 && pending == 0 ? WorkflowStageState.Rejected
            : pending > 0 && isLive ? WorkflowStageState.Awaiting
            : pending > 0 ? WorkflowStageState.Queued
            : WorkflowStageState.Skipped;
        return new WorkflowStageSummary(state, total, approved, rejected, pending);
    }
}
