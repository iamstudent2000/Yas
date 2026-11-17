using YasPortal.Domain.Organization;

namespace YasPortal.Infrastructure.Authorization;

/// <summary>
/// Whether a principal's claimed identity — employee, admin flag, active position — still matches
/// the database. This is the one place that check is written: it backs both the cookie's periodic
/// <c>OnValidatePrincipal</c> check (which only runs on a real HTTP request — see Program.cs) and
/// <c>SessionGuard</c>'s in-circuit check (which covers a long-lived Blazor Server session that
/// makes no further HTTP requests after the page first loads). Keeping both paths calling the same
/// method means they can never quietly drift apart and disagree about who is still valid.
/// </summary>
public static class IdentitySnapshotValidator
{
    // Still valid only if: the employee exists and is active, the admin flag is unchanged since sign-in, and (for a
    // non-admin) the position chosen at sign-in is still one of the employee's active assignments.
    public static bool IsStillValid(Employee? employee, bool claimedIsAdmin, Guid? claimedPositionId) =>
        employee is not null
        && employee.IsActive
        && employee.IsAdmin == claimedIsAdmin
        && (claimedIsAdmin || (claimedPositionId is Guid activePositionId && employee.Positions.Any(p => p.PositionId == activePositionId && p.IsActive)));
}
