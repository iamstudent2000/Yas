namespace YasPortal.Application.Authorization;

// This file holds two small "contracts" (interfaces): promises that the rest of the system relies on.
// The real implementations live in the web project; here we only describe what they must be able to answer.
// Contract: who is the person currently signed in? An interface = a list of questions the system can ask.
// EmployeeId is the signed-in person's identity; it is empty when nobody is signed in.
// ActivePositionId is the position (job seat) the person is currently acting in, because positions, not people, hold workflow steps.
// IsAdmin says whether the person is a system administrator.
public interface ICurrentUser
{
    Guid? EmployeeId
    {
        get;
    }
    Guid? ActivePositionId
    {
        get;
    }
    bool IsAdmin
    {
        get;
    }
}

// Contract: can the signed-in person do a given action? Each action has a permission code such as "Admin.Users.View".
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default);
}
