# YasPortal

Clean rebuild of YasPortal.

## Authorization model

- No SuperAdmin role or exception.
- No EMPLOYEE Identity role.
- Admin status is represented by `Employee.IsAdmin`.
- Permissions are assigned through `UserPositionPermission` (User/Employee + Position + Permission).
- The active Position determines the permissions used for authorization.
- Admin operations require both `Employee.IsAdmin == true` and the required permission on the active Position.
- Employee operations are controlled by permissions on the active Position.
- Roles are not used for application authorization.

## Authentication

Development uses ASP.NET Core cookie authentication with passwords stored as hashes on the Employee account. Authentication is separate from authorization: the cookie identifies the employee, while authorization evaluates `IsAdmin` and the active Position permissions.

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
