using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Resolves a workflow request's approval path and applies approver actions to it.</summary>
public sealed class WorkflowService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public sealed record ResolvedStep(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId);
    public sealed record ResolveResult(bool Success, string? Error, IReadOnlyList<ResolvedStep> Steps);

    /// <summary>
    /// Loads the active step definitions for <paramref name="type"/> and resolves each one's
    /// approver position for <paramref name="requesterPositionId"/> using the strict
    /// <see cref="WorkflowStepResolver"/>: the configured path is followed exactly or the
    /// submission is refused with every reason listed. It never clamps an unreachable manager
    /// level to a lower manager, never skips a step, and never lets the requester (or the same
    /// position twice) approve — see the resolver for why (spec §23/§24).
    /// </summary>
    public async Task<ResolveResult> ResolveStepsAsync(WorkflowTypeCode type, Guid requesterPositionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var definitions = await db.WorkflowStepDefinitions.AsNoTracking()
            .Where(x => x.WorkflowType == type)
            .ToListAsync(ct);
        var parents = await db.Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionaryAsync(x => x.Id, x => x.ParentPositionId, ct);

        var resolution = WorkflowStepResolver.Resolve(definitions, requesterPositionId, parents);
        if (!resolution.Success)
            return new ResolveResult(false, DescribeErrors(resolution.Errors), []);

        return new ResolveResult(true, null,
            resolution.Steps.Select(x => new ResolvedStep(x.StepDefinitionId, x.Order, x.Name, x.ApproverPositionId)).ToList());
    }

    /// <summary>User-facing (Persian) explanation of why a path could not be resolved, one sentence per problem.</summary>
    internal static string DescribeErrors(IReadOnlyList<WorkflowResolutionError> errors)
    {
        var lines = errors.Select(e => e.Kind switch
        {
            WorkflowResolutionErrorKind.NoActiveSteps =>
                "برای این نوع گردش‌کار هیچ مرحله تاییدی فعال تعریف نشده است. با مدیر سامانه تماس بگیرید.",
            WorkflowResolutionErrorKind.ManagerLevelUnavailable =>
                $"مرحله «{e.StepName}» به مدیری {e.ManagerLevel} سطح بالاتر از سمت شما نیاز دارد، اما در ساختار سازمانی چنین سمتی وجود ندارد. مدیر سامانه باید مسیر گردش‌کار یا ساختار سازمانی را اصلاح کند.",
            WorkflowResolutionErrorKind.ApproverIsRequesterPosition =>
                $"مرحله «{e.StepName}» به سمت خودِ شما می‌رسد و نمی‌توانید درخواست خودتان را تایید کنید. مدیر سامانه باید مسیر گردش‌کار را اصلاح کند.",
            WorkflowResolutionErrorKind.DuplicateApprover =>
                $"مرحله «{e.StepName}» به همان سمتی می‌رسد که مرحله‌ای قبل‌تر نیز به آن می‌رسد. مدیر سامانه باید مسیر گردش‌کار را اصلاح کند.",
            _ => "مسیر تایید این درخواست قابل تعیین نیست.",
        });
        return string.Join(" ", lines);
    }

    /// <summary>
    /// Verifies (fresh from the database) that <paramref name="employeeId"/> currently holds
    /// <paramref name="positionId"/> as an active assignment. Used to re-check the acting
    /// employee's authority to act on a step right before applying the action, rather than
    /// trusting a cookie claim.
    /// </summary>
    public async Task<bool> HoldsActivePositionAsync(Guid employeeId, Guid? positionId, CancellationToken ct = default)
    {
        if (positionId is not Guid pid)
            return false;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.EmployeePositions.AsNoTracking().AnyAsync(x => x.EmployeeId == employeeId && x.PositionId == pid && x.EndedAt == null, ct);
    }

    /// <summary>
    /// Maps each of the given positions to the full name of whoever currently, actively
    /// holds it — a position with nobody currently assigned is simply absent from the result,
    /// so callers can show "position is vacant" for anything missing from the dictionary.
    /// </summary>
    public async Task<Dictionary<Guid, string>> GetCurrentHolderNamesAsync(IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default)
    {
        if (positionIds.Count == 0)
            return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.EmployeePositions.AsNoTracking()
            .Where(x => x.EndedAt == null && positionIds.Contains(x.PositionId))
            .Join(db.Employees.AsNoTracking(), ep => ep.EmployeeId, e => e.Id, (ep, e) => new { ep.PositionId, e.FullName })
            .ToDictionaryAsync(x => x.PositionId, x => x.FullName, ct);
    }

    /// <summary>
    /// How many active requests of <paramref name="type"/> are still in flight (spec §18, §22.2).
    /// While this is above zero the type's approval steps must not be changed: the SuperAdmin
    /// has to resolve those requests first (force-close them from the Active Requests page).
    /// </summary>
    public async Task<int> CountBlockingRequestsAsync(WorkflowTypeCode type, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowRequests.AsNoTracking().Active().CountAsync(x => x.WorkflowType == type, ct);
    }

    /// <summary>Number of requests currently sitting at a step assigned to this position — drives the inbox badge.</summary>
    public async Task<int> CountInboxAsync(Guid? approverPositionId, CancellationToken ct = default)
    {
        if (approverPositionId is not Guid positionId)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowRequests.AsNoTracking()
            .CountAsync(x => x.Status == WorkflowRequestStatus.PendingApproval
                              && x.Steps.Any(s => s.Round == x.CurrentRound
                                                  && s.Order == x.CurrentStepOrder
                                                  && s.Status == WorkflowStepStatus.Pending
                                                  && s.ApproverPositionId == positionId), ct);
    }

    /// <summary>Number of the employee's own requests with a decision they haven't looked at yet — drives the "my requests" badge.</summary>
    public async Task<int> CountUnseenForRequesterAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowRequests.AsNoTracking().CountAsync(x => x.RequesterEmployeeId == id && !x.RequesterHasSeenLatestUpdate, ct);
    }
}
