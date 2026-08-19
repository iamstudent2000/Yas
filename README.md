# YasPortal

Clean rebuild of YasPortal.

## Authorization model

- No SuperAdmin role or exception.
- No EMPLOYEE Identity role.
- Admin status is represented by `Employee.IsAdmin`.
- Roles are not used for application authorization.
- There are two independent permission tracks, and `PermissionChecker` picks one based on `Employee.IsAdmin`:
  - **Admin track** (`Employee.IsAdmin == true`): permissions come only from `EmployeePermission` and `EmployeePermissionGroup` — direct grants to the employee. The active Position is not consulted at all for admins, and `Admin.*` permission codes can only be satisfied through this track.
  - **Employee track** (`Employee.IsAdmin == false`): permissions come only from `UserPositionPermission` and `UserPositionPermissionGroup`, scoped to the employee's current active Position (`UserPositionPermission` = User/Employee + Position + Permission). `Admin.*` permission codes are always denied on this track, even if granted on a position.
- Every permission check re-verifies `Employee.IsActive`/`IsAdmin` and (for the employee track) the active-position assignment against the database — cookie claims are never trusted on their own, so a revoked position or deactivated account stops working immediately, even before the auth cookie expires.

## Authentication

Development uses ASP.NET Core cookie authentication with passwords stored as hashes on the Employee account. Authentication is separate from authorization: the cookie identifies the employee, while authorization evaluates `IsAdmin` and either the employee's direct permission grants (admin track) or their active Position's permissions (employee track), per the Authorization model above.

Development seed accounts:

| Account | Password | Admin |
|---|---|---|
| `admin` | `Admin123!` | Yes |
| `employee` | `Employee123!` | No |

The development database is `YasPortalClean` on LocalDB. The development seeder creates the accounts, positions, and permissions automatically when the database is first created.

## Solution

- `src/YasPortal.Domain` — business entities and rules
- `src/YasPortal.Application` — application contracts and authorization requirements
- `src/YasPortal.Infrastructure` — EF Core, authentication support, authorization handlers, and seed data
- `src/YasPortal.Web` — Blazor Server UI and cookie authentication endpoints
- `tests/YasPortal.Tests` — automated tests
