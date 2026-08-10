namespace YasPortal.Application.Authorization;

public interface ICurrentUser
{
    Guid? EmployeeId { get; }
    Guid? ActivePositionId { get; }
    bool IsAdmin { get; }
}

public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);
}
