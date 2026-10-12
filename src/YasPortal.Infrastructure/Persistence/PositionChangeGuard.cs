using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Workflows;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// Position change protection (spec §19, §22.2, §26.3): before a position is deactivated or re-parented,
/// every active request it affects must be resolved first. This finds them so the admin can be shown the
/// blocking list. Nothing here changes data.
/// </summary>
public static class PositionChangeGuard
{
    /// <summary>A request that blocks a position change, with why.</summary>
    public sealed record Blocker(Guid RequestId, WorkflowTypeCode Type, string Reason)
    {
        public string ShortId => RequestId.ToString("N")[..8];
    }

    /// <summary>
    /// Active requests (pending approval or returned to the requester) that touch any of the given
    /// positions: the position is the one that must approve a step still to be decided in the current round,
    /// or it is the requester's own position (their approval path is resolved from it).
    /// </summary>
    public static async Task<IReadOnlyList<Blocker>> ActiveRequestsTouchingAsync(ApplicationDbContext db, IReadOnlyCollection<Guid> positionIds, CancellationToken ct = default)
    {
        if (positionIds.Count == 0)
            return [];

        var rows = await db.WorkflowRequests.AsNoTracking().Active()
            .Where(r => positionIds.Contains(r.RequesterPositionId)
                        || r.Steps.Any(s => s.Round == r.CurrentRound && s.Status == WorkflowStepStatus.Pending && positionIds.Contains(s.ApproverPositionId)))
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.WorkflowType,
                AsRequester = positionIds.Contains(r.RequesterPositionId),
            })
            .Take(200)
            .ToListAsync(ct);

        return rows.Select(r => new Blocker(r.Id, r.WorkflowType, r.AsRequester ? "ثبت‌شده از این سمت" : "در انتظار تصمیم این سمت")).ToList();
    }

    /// <summary>Workflow steps (by "type — step name") that name the position as a fixed approver.</summary>
    public static async Task<IReadOnlyList<string>> WorkflowStepsUsingAsync(ApplicationDbContext db, Guid positionId, CancellationToken ct = default)
    {
        var steps = await db.WorkflowStepDefinitions.AsNoTracking()
            .Where(d => d.ApproverPositionId == positionId)
            .Select(d => new { d.WorkflowType, d.Name })
            .ToListAsync(ct);
        return steps.Select(s => $"{WorkflowFieldCatalog.GetDisplayName(s.WorkflowType)} — {s.Name}").ToList();
    }

    /// <summary>The position plus all of its descendants (a re-parent moves the whole branch).</summary>
    public static async Task<HashSet<Guid>> SubtreeAsync(ApplicationDbContext db, Guid positionId, CancellationToken ct = default)
    {
        var parents = await db.Positions.AsNoTracking().Select(p => new { p.Id, p.ParentPositionId }).ToListAsync(ct);
        var result = new HashSet<Guid> { positionId };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var p in parents)
                if (p.ParentPositionId is Guid parent && result.Contains(parent) && result.Add(p.Id))
                    added = true;
        }
        return result;
    }

    /// <summary>Whether the position has ever been used (spec §3.4): assignments, permissions, workflow steps, requests, steps, decisions, delegations.</summary>
    public static async Task<string?> HistoricalUsageAsync(ApplicationDbContext db, Guid positionId, CancellationToken ct = default)
    {
        if (await db.EmployeePositions.AnyAsync(x => x.PositionId == positionId, ct)) return "تخصیص کارمند";
        if (await db.UserPositionPermissions.AnyAsync(x => x.PositionId == positionId, ct)) return "مجوز";
        if (await db.WorkflowStepDefinitions.AnyAsync(x => x.ApproverPositionId == positionId, ct)) return "گردش‌کار";
        if (await db.WorkflowRequests.AnyAsync(x => x.RequesterPositionId == positionId, ct)) return "سابقه درخواست";
        if (await db.WorkflowRequestSteps.AnyAsync(x => x.ApproverPositionId == positionId, ct)) return "سابقه تایید";
        if (await db.PositionDelegations.AnyAsync(x => x.PositionId == positionId || x.DelegatePositionId == positionId, ct)) return "تفویض اختیار";
        return null;
    }
}
