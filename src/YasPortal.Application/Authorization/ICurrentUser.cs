namespace YasPortal.Application.Authorization;

public interface ICurrentUser
{
    Guid? EmployeeId { get; }
    Guid? ActivePositionId { get; }
    bool IsAdmin { get; }
}
