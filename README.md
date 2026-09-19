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

## Workflow / requests

Employees submit fixed-type requests (Leave, Purchase, Access, Loan, Helpdesk, Work Report) that route through the org hierarchy for approval:

- **Types and fields are fixed in code** (`YasPortal.Domain.Workflows.WorkflowFieldCatalog`) — not admin-editable.
- **Approval steps are admin-editable** (`YasPortal.Domain.Workflows.WorkflowStepDefinition`, managed from `/admin/workflows`, `Admin.Workflows` permission). Each step resolves its approver one of two ways: N levels up the requester's own position hierarchy (e.g. "1" = their direct manager), or a fixed specific position regardless of who's asking — so different workflow types can have entirely different paths.
- A step's approver is *whoever currently holds* the resolved position, not a snapshot of a person, consistent with how the rest of the app ties authority to positions.
- Pages: `/requests/new` (`Requests.Create`), `/requests/mine` (`Requests.View`), `/requests/inbox` (`Requests.View`, with `Requests.Approve`/`Requests.Reject`/`Requests.ReturnToRequester`/`Requests.ReturnToPreviousStep` gating the individual actions).
- The development seed grants a default approval path per type (e.g. Leave → direct manager → HR) matching the seeded `manager`/`hr`/`finance` approver permissions; an admin can add/reorder/remove steps afterward without the seeder ever overwriting those edits.

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
