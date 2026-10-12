using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Delegations;
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
    /// level to a lower manager, and never skips a step — see the resolver for why (spec §23/§24).
    /// Steps landing on the requester's own position, or on the same position twice, are allowed.
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
            WorkflowResolutionErrorKind.TreeLevelUnavailable =>
                $"مرحله «{e.StepName}» باید تا سطح {e.TreeLevel} درخت سازمانی بالا برود، اما سمت شما بالاتر از آن سطح قرار دارد و تاییدکننده‌ای برای ارجاع وجود ندارد. مدیر سامانه باید مسیر گردش‌کار را اصلاح کند.",
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

    /// <summary>A delegation that gives an employee authority over a position right now.</summary>
    public sealed record ActiveDelegation(Guid DelegationId, Guid PositionId, Guid? DelegatorEmployeeId, bool ArrangedByAdmin);

    /// <summary>What lets an employee act on a step: nothing, their own position, or a delegation.</summary>
    public sealed record ActingResult(bool Allowed, WorkflowActingAuthority? Authority)
    {
        public static ActingResult Denied { get; } = new(false, null);
        public static ActingResult AsHolder { get; } = new(true, null);
    }

    /// <summary>
    /// The delegations that are in effect today for <paramref name="employeeId"/> as delegate (spec §15, §16):
    /// not revoked, inside their dates, and — for a normal delegation — the delegator still holds the
    /// position. A SuperAdmin-arranged replacement does not depend on who holds the position.
    /// </summary>
    public async Task<IReadOnlyList<ActiveDelegation>> GetActiveDelegationsAsync(Guid? employeeId, DateTime today, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var day = today.Date;
        var rows = await db.PositionDelegations.AsNoTracking()
            .Where(d => db.EmployeePositions.Any(ep => ep.EmployeeId == id && ep.PositionId == d.DelegatePositionId && ep.EndedAt == null)
                        && d.RevokedAtUtc == null
                        && d.StartDate <= day && d.EndDate >= day
                        && (d.ArrangedByAdmin
                            || db.EmployeePositions.Any(ep => ep.EmployeeId == d.DelegatorEmployeeId && ep.PositionId == d.PositionId && ep.EndedAt == null)))
            .Select(d => new { d.Id, d.PositionId, d.DelegatorEmployeeId, d.ArrangedByAdmin })
            .ToListAsync(ct);
        return rows.Select(d => new ActiveDelegation(d.Id, d.PositionId, d.DelegatorEmployeeId, d.ArrangedByAdmin)).ToList();
    }

    /// <summary>
    /// Requests currently waiting at a step this approver may decide: steps of their own position, plus
    /// steps of positions delegated to them (admin arrangements on any step, normal delegations only on
    /// steps that allow delegation).
    /// </summary>
    public static IQueryable<WorkflowRequest> PendingFor(ApplicationDbContext db, Guid? ownPositionId, IReadOnlyList<ActiveDelegation> delegations)
    {
        var anyStep = delegations.Where(d => d.ArrangedByAdmin).Select(d => d.PositionId).Distinct().ToList();
        var allowedSteps = delegations.Where(d => !d.ArrangedByAdmin).Select(d => d.PositionId).Distinct().ToList();
        var own = ownPositionId ?? Guid.Empty;
        return db.WorkflowRequests.Where(x => x.Status == WorkflowRequestStatus.PendingApproval
            && x.Steps.Any(s => s.Round == x.CurrentRound
                                && s.Order == x.CurrentStepOrder
                                && s.Status == WorkflowStepStatus.Pending
                                && (s.ApproverPositionId == own
                                    || anyStep.Contains(s.ApproverPositionId)
                                    || (allowedSteps.Contains(s.ApproverPositionId)
                                        && db.WorkflowStepDefinitions.Any(d => d.Id == s.StepDefinitionId && d.AllowDelegation)))));
    }

    /// <summary>
    /// Decides, fresh from the database, whether <paramref name="employeeId"/> may act on
    /// <paramref name="step"/>: as the holder of its position (their active position), or through a
    /// delegation that covers it (spec §15.1: a normal delegation only if the step allows it).
    /// </summary>
    public async Task<ActingResult> ResolveActingAsync(Guid employeeId, Guid? activePositionId, WorkflowRequestStep step, DateTime today, CancellationToken ct = default)
    {
        if (activePositionId is Guid own && own == step.ApproverPositionId && await HoldsActivePositionAsync(employeeId, own, ct))
            return ActingResult.AsHolder;

        var delegations = await GetActiveDelegationsAsync(employeeId, today, ct);
        var candidates = delegations.Where(d => d.PositionId == step.ApproverPositionId).ToList();
        if (candidates.Count == 0)
            return ActingResult.Denied;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var allowsDelegation = await db.WorkflowStepDefinitions.AsNoTracking().Where(d => d.Id == step.StepDefinitionId).Select(d => d.AllowDelegation).SingleOrDefaultAsync(ct);
        var match = candidates.OrderBy(d => d.ArrangedByAdmin).FirstOrDefault(d => d.ArrangedByAdmin || allowsDelegation);
        return match is null ? ActingResult.Denied : new ActingResult(true, new WorkflowActingAuthority(match.DelegatorEmployeeId, match.DelegationId));
    }

    /// <summary>Number of requests waiting for this approver, counting delegated positions — drives the inbox badge.</summary>
    public async Task<int> CountInboxAsync(Guid? employeeId, Guid? activePositionId, DateTime today, CancellationToken ct = default)
    {
        var delegations = await GetActiveDelegationsAsync(employeeId, today, ct);
        var holds = employeeId is Guid id && await HoldsActivePositionAsync(id, activePositionId, ct);
        if (!holds && delegations.Count == 0)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await PendingFor(db, holds ? activePositionId : null, delegations).AsNoTracking().CountAsync(ct);
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
