# YasPortal

Blazor Server intranet for organization, positions, permissions, and approval workflows. .NET 10, EF Core, SQL Server.

Persian RTL UI. Authority is tied to **positions**, not to people: whoever currently holds a position may act for it.

## Run

SQL Server Express locally. Connection string in `src/YasPortal.Web/appsettings.json`:

```
Server=.\SQLEXPRESS;Database=YasPortalClean;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

```bash
dotnet run --project src/YasPortal.Web
```

Development creates the schema with `EnsureCreated` and runs `DevelopmentDataSeeder`. There is no migration workflow. Drop `YasPortalClean` and restart whenever the schema needs a clean slate. The seeder also deletes and recreates the database if required columns are missing.

## Authentication

Cookie auth (`YasPortal.Auth`, 8 hours, sliding). Passwords are hashed on `Employee`. Login is `POST /account/login`. Logout is `POST /account/logout`. A non-admin cannot sign in without an active position assignment.

Development seed accounts (created on first run):

| Username | Password | Admin | Position |
|---|---|---|---|
| `admin` | `Admin123!` | Yes | none — admin track |
| `employee` | `Employee123!` | No | کارمند |
| `manager` | `Manager123!` | No | مدیر واحد |
| `hr` | `Hr123!` | No | مدیر منابع انسانی |
| `finance` | `Finance123!` | No | کارشناس مالی |

Seeded position tree:

```
مدیر سامانه
├─ مدیر منابع انسانی   (hr)
└─ مدیر واحد             (manager)
    ├─ کارشناس مالی         (finance)
    └─ کارمند               (employee)
```

## Authorization model

No Identity roles. No SuperAdmin bypass. `Employee.IsAdmin` only selects which grant tables are snapshotted.

Two independent grant tracks, evaluated when the cookie is written (login or `POST /account/position`):

- **Admin track** (`Employee.IsAdmin == true`): `EmployeePermission` + `EmployeePermissionGroup`. No active position. Admins do not receive position grants.
- **Employee track** (`Employee.IsAdmin == false`): `UserPositionPermission` + `UserPositionPermissionGroup` for the **currently selected** active position only. Switching position rebuilds the cookie so the previous position's grants cannot leak.

`PermissionChecker` then reads only the snapshot in the cookie — repeated `yas_permission` claims. It does not hit the database.

Cookie claims:

| Claim | Meaning |
|---|---|
| `NameIdentifier` | Employee id |
| `Name` | Username |
| `yas_is_admin` | `Employee.IsAdmin` at sign-in |
| `yas_active_position_id` | Selected position (non-admin) |
| `yas_permission` | One claim per granted permission code |

Grant or revoke changes take effect on the next login or position switch, not on the current cookie. Workflow **actions** are an exception: inbox load and step decisions re-check that the actor still holds the claimed position (`WorkflowService.HoldsActivePositionAsync`).

## Workflow / requests

Employees submit fixed-type requests that route through the org hierarchy.

- **Types and fields are fixed in code** (`WorkflowFieldCatalog`): Leave, Purchase, Access, Loan, Helpdesk, Work Report. Not admin-editable.
- **Approval steps are admin-editable** (`WorkflowStepDefinition`, `/admin/workflows`, `Admin.Workflows`). Each step is either *N levels up* the requester's position tree, or a fixed position. The seeder writes a default path per type only if that type has no steps yet — later admin edits are never overwritten.
- A step's actor is *whoever currently holds* the resolved position, not a person snapshotted at submit time.
- Submit resolves the path then. Resubmit (after return-to-requester) starts a **new round** with a freshly resolved path; the closed round is kept as history (`Superseded` for unused pending steps).
- Requester can cancel only while returned, or while pending with no approval yet in the current round.
- `RequesterHasSeenLatestUpdate` drives the "my requests" badge.

Default seed paths:

| Type | Path |
|---|---|
| Leave | direct manager → HR |
| Purchase | direct manager → finance |
| Access | direct manager → مدیر سامانه |
| Loan | direct manager → finance → HR |
| Helpdesk | مدیر سامانه |
| Work Report | direct manager |

Pages:

| Route | Permission |
|---|---|
| `/requests/new` | `Requests.Create` |
| `/requests/mine` | `Requests.View` |
| `/requests/inbox` | `Requests.View` (actions gated by `Requests.Approve` / `Reject` / `ReturnToRequester` / `ReturnToPreviousStep`) |
| `/admin/workflows` | `Admin.Workflows` |

A manager-level step with nobody that far up the tree clamps to the highest ancestor that exists, or is skipped if the requester has no manager at all.

## Organization rules

Enforced in domain + persistence, not only in UI:

- Inactive employee cannot hold an active assignment. Deactivate ends all assignments and clears last-position preference.
- A position has at most one active employee (filtered unique index on `EmployeePositions`).
- Position hierarchy cycles are rejected before save.
- Assignment history opens on create/reactivate and closes when the assignment ends.
- Employee, Organization, Position, and PermissionGroup have a manually entered `Code`.

## Pages

| Route | Permission |
|---|---|
| `/dashboard` | `Dashboard.View` |
| `/my-profile` | `Profile.View` |
| `/my-positions` | signed-in non-admin |
| `/admin/users` | `Admin.Users` |
| `/admin/access` | `Admin.Access` |
| `/admin/permissions` | `Admin.Permissions` |
| `/admin/permission-groups` | `Admin.Permissions` |
| `/admin/positions` | `Admin.Positions` |
| `/admin/organizations` | `Admin.Organizations` |
| `/admin/assignment-history` | `Admin.AssignmentHistory` |
| `/admin/audit-log` | `Admin.AuditLog` |

## Solution

- `src/YasPortal.Domain` — entities and invariants
- `src/YasPortal.Application` — contracts (`ICurrentUser`, `IPermissionChecker`, permission policies)
- `src/YasPortal.Infrastructure` — EF Core, cookie snapshot builder inputs, authorization handler, seeder
- `src/YasPortal.Web` — Blazor Server UI, cookie endpoints, `WorkflowService`
- `tests/YasPortal.Tests` — tests
- `Patchs/` — numbered development patches
