using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Organization;
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
    /// approver position for <paramref name="requesterPositionId"/> (walking up the hierarchy
    /// for "N levels up" steps). Fails with a human-readable reason if there are no active
    /// steps configured, or if a manager-level step has no ancestor that high up.
    /// </summary>
    public async Task<ResolveResult> ResolveStepsAsync(WorkflowTypeCode type, Guid requesterPositionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var definitions = await db.WorkflowStepDefinitions.AsNoTracking()
            .Where(x => x.WorkflowType == type && x.IsActive)
            .OrderBy(x => x.Order)
            .ToListAsync(ct);
        if (definitions.Count == 0)
            return new ResolveResult(false, "برای این نوع گردش‌کار هیچ مرحله تاییدی تعریف نشده است. با مدیر سامانه تماس بگیرید.", []);

        var parents = await db.Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionaryAsync(x => x.Id, x => x.ParentPositionId, ct);
        var resolved = new List<ResolvedStep>();
        foreach (var def in definitions)
        {
            var approverPositionId = def.ApproverRuleKind == ApproverRuleKind.SpecificPosition
                ? def.ApproverPositionId
                : PositionHierarchy.GetAncestorPositionId(requesterPositionId, def.ManagerLevel!.Value, parents);
            if (approverPositionId is not Guid pid)
                return new ResolveResult(false, $"مرحله «{def.Name}» قابل تعیین تاییدکننده نیست؛ سلسله‌مراتب سمت شما به اندازه کافی بالا نمی‌رود.", []);
            resolved.Add(new ResolvedStep(def.Id, def.Order, def.Name, pid));
        }
        return new ResolveResult(true, null, resolved);
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

    /// <summary>Number of requests currently sitting at a step assigned to this position — drives the inbox badge.</summary>
    public async Task<int> CountInboxAsync(Guid? approverPositionId, CancellationToken ct = default)
    {
        if (approverPositionId is not Guid positionId)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowRequests.AsNoTracking()
            .CountAsync(x => x.Status == WorkflowRequestStatus.PendingApproval
                              && x.Steps.Any(s => s.Order == x.CurrentStepOrder && s.Status == WorkflowStepStatus.Pending && s.ApproverPositionId == positionId), ct);
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
