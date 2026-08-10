using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Application.Persistence;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(IApplicationDbContext db, ICurrentUser currentUser) : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        if (currentUser.EmployeeId is null || currentUser.ActivePositionId is null)
            return Task.FromResult(false);

        return db.UserPositionPermissions.AnyAsync(
            x => x.EmployeeId == currentUser.EmployeeId.Value &&
                 x.PositionId == currentUser.ActivePositionId.Value &&
                 x.Permission.Code == permissionCode,
            cancellationToken);
    }
}

public sealed class CurrentUser : ICurrentUser
{
    public Guid? EmployeeId { get; init; }
    public Guid? ActivePositionId { get; init; }
    public bool IsAdmin { get; init; }
}
