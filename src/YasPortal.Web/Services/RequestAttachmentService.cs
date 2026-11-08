using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>A file chosen on a new-request page that is kept in memory until the request is submitted.</summary>
public sealed record PendingAttachment(string Name, byte[] Content);

/// <summary>A stored attachment without its contents, for lists.</summary>
public sealed record AttachmentInfo(Guid Id, string FileName, long SizeBytes);

/// <summary>Stores, lists and gates downloads of the files attached to requests.</summary>
public sealed class RequestAttachmentService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows)
{
    /// <summary>Adds the files to the context so they are saved in the same transaction as the request (call before SaveChanges).</summary>
    public static void Stage(ApplicationDbContext db, Guid requestId, Guid uploadedByEmployeeId, IEnumerable<PendingAttachment> files)
    {
        foreach (var file in files.Take(WorkflowRequestAttachment.MaxFilesPerRequest))
            db.WorkflowRequestAttachments.Add(new WorkflowRequestAttachment(requestId, uploadedByEmployeeId, file.Name, file.Content));
    }

    public async Task<IReadOnlyList<AttachmentInfo>> ListAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowRequestAttachments.AsNoTracking()
            .Where(x => x.RequestId == requestId)
            .OrderBy(x => x.UploadedAtUtc)
            .Select(x => new AttachmentInfo(x.Id, x.FileName, x.SizeBytes))
            .ToListAsync(ct);
    }

    /// <summary>
    /// The file, or null when it does not exist or the signed-in user may not see its request. A person may download when they
    /// are the requester or the one who registered it, may view all requests (admin report), hold or are delegated the position of
    /// any step of the request (or decided on it), or are a manager with the team-view permission above the requester's position.
    /// The same answer (null → 404) is given for "missing" and "not allowed" so ids cannot be probed.
    /// </summary>
    public async Task<WorkflowRequestAttachment?> GetForDownloadAsync(Guid id, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var file = await db.WorkflowRequestAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (file is null)
            return null;

        if (user.HasClaim(AuthClaimNames.Permission, Perm.AdminRequestsView))
            return file;

        var request = await db.WorkflowRequests.AsNoTracking()
            .Where(x => x.Id == file.RequestId)
            .Select(x => new { x.RequesterEmployeeId, x.CreatedByEmployeeId, x.RequesterPositionId })
            .SingleOrDefaultAsync(ct);
        if (request is null)
            return null;
        if (request.RequesterEmployeeId == employeeId || request.CreatedByEmployeeId == employeeId)
            return file;

        var steps = await db.WorkflowRequestSteps.AsNoTracking()
            .Where(x => x.RequestId == file.RequestId)
            .Select(x => new { x.ApproverPositionId, x.ActedByEmployeeId })
            .ToListAsync(ct);
        if (steps.Any(s => s.ActedByEmployeeId == employeeId))
            return file;

        // Positions the person may act from: the active one (re-checked against the database, not trusted from the cookie) and delegated ones.
        var positions = new HashSet<Guid>();
        Guid? activePositionId = Guid.TryParse(user.FindFirstValue(AuthClaimNames.ActivePositionId), out var pid) ? pid : null;
        if (activePositionId is Guid active && await workflows.HoldsActivePositionAsync(employeeId, active, ct))
            positions.Add(active);
        foreach (var delegation in await workflows.GetActiveDelegationsAsync(employeeId, DateTime.Today, ct))
            positions.Add(delegation.PositionId);
        if (steps.Any(s => positions.Contains(s.ApproverPositionId)))
            return file;

        if (user.HasClaim(AuthClaimNames.Permission, Perm.RequestsTeamView) && positions.Contains(activePositionId ?? Guid.Empty)
            && (await workflows.GetSubordinatePositionsAsync(activePositionId!.Value, ct)).Any(p => p.Id == request.RequesterPositionId))
            return file;

        return null;
    }
}
