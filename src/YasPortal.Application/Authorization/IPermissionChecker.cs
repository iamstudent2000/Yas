namespace YasPortal.Application.Authorization;

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);
}
