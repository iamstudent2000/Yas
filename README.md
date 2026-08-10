# YasPortal

Clean rebuild of YasPortal.

## Authorization model

- No SuperAdmin role.
- No EMPLOYEE Identity role.
- Admin status is represented by `Employee.IsAdmin`.
- Permissions are assigned through `UserPositionPermission` (User/Employee + Position + Permission).
- The active Position determines the permissions used for authorization.
- Admin status identifies access to the Admin area; it does not automatically grant every permission.

## Solution

- `src/YasPortal.Domain` — business entities and rules
- `src/YasPortal.Application` — application contracts and use cases
- `src/YasPortal.Infrastructure` — EF Core and external implementations
- `src/YasPortal.Web` — Blazor Server UI
- `tests/YasPortal.Tests` — automated tests
