using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Application.Persistence;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(IApplicationDbContext db, ICurrentUser currentUser) : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        if (currentUser.EmployeeId is not Guid employeeId || currentUser.ActivePositionId is not Guid positionId)
            return Task.FromResult(false);

        return db.UserPositionPermissions.AnyAsync(
            x => x.EmployeeId == employeeId &&
                 x.PositionId == positionId &&
                 x.Permission.Code == permissionCode,
            cancellationToken);
    }
}
