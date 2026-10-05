namespace YasPortal.Domain.Workflows;

/// <summary>Shared query definitions for <see cref="WorkflowRequest"/>, so "active" means one thing everywhere.</summary>
public static class WorkflowRequestQueries
{
    /// <summary>
    /// Requests that can still move: waiting on an approver, or sent back to the requester. This is
    /// the set that blocks workflow and position changes (spec §18, §19, §22.2) and the set the
    /// SuperAdmin can force-close. Approved, Rejected and Cancelled requests are terminal and never
    /// block anything.
    /// </summary>
    public static IQueryable<WorkflowRequest> Active(this IQueryable<WorkflowRequest> requests) =>
        requests.Where(x => x.Status == WorkflowRequestStatus.PendingApproval
                            || x.Status == WorkflowRequestStatus.ReturnedToRequester);
}
