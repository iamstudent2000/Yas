# Icon map

Icons are Tabler Icons (webfont, `wwwroot/lib/tabler-icons`). Every icon in the app comes from the `Icons` class (`src/YasPortal.Web/Icons.cs`): one concept, one icon. To change an icon, change the constant; to add one, add a constant. Do not write literal `ti-*` classes in markup.

Avatar pictograms (`AvatarService`) are the only literal icons: they are user-chosen pictures, not concepts.

The page is generated from `Icons.cs`; regenerate it after changing icons.

## Generic UI actions

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `Close` | `ti-x` | 17 | AccessManagement×4, AuditLog×2, PermissionGroups×2, WorkflowDisplay×2 +7 more |
| `Confirm` | `ti-check` | 22 | Positions×6, AccessManagement×3, PermissionGroups×2, WorkflowDisplay×2 +9 more |
| `ConfirmAll` | `ti-checks` | 4 | Notifications, AccessManagement, PermissionGroups, MyRequests |
| `Save` | `ti-device-floppy` | 8 | MyProfile, PasswordPolicy, Organizations, Workflows +4 more |
| `Delete` | `ti-trash` | 4 | AuditLog, Workflows, PermissionGroups, Positions |
| `Search` | `ti-search` | 16 | AccessManagement×3, AuditLog×2, Organizations×2, Permissions×2 +7 more |
| `SearchEmpty` | `ti-search-off` | 6 | AuditLog, AllRequests, Organizations, Permissions +2 more |
| `Filter` | `ti-filter` | 1 | ListFilterBar |
| `FilterClear` | `ti-filter-off` | 1 | ListFilterBar |
| `CancelSearch` | `ti-player-stop` | 1 | ListFilterBar |
| `Add` | `ti-plus` | 7 | Positions×2, Delegations, AuditLog, Organizations +2 more |
| `Edit` | `ti-pencil` | 15 | Workflows×3, Organizations×2, PermissionGroups×2, Users×2 +4 more |
| `Details` | `ti-eye` | 5 | AuditLog, AllRequests, RejectedRequests, Permissions +1 more |
| `More` | `ti-dots` | 1 | ListFilterBar |
| `Send` | `ti-send` | 3 | NewRequestForOthers, NewRequest, MyRequests |
| `Print` | `ti-printer` | 2 | RequestSummary, ListFilterBar |
| `ExportData` | `ti-file-export` | 1 | ListFilterBar |
| `ExportSteps` | `ti-file-spreadsheet` | 1 | ListFilterBar |
| `Pdf` | `ti-file-type-pdf` | 1 | RequestSummary |

## Navigation and direction (RTL: forward points left, back points right)

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `ChevronDown` | `ti-chevron-down` | 19 | PersistentSidebar×4, Permissions×2, Login, Organizations +11 more |
| `ChevronUp` | `ti-chevron-up` | 6 | Permissions×2, AllRequests, TeamHistory, RequestSummary +1 more |
| `PagePrev` | `ti-chevron-right` | 2 | ServerPagination, UsagePager |
| `PageNext` | `ti-chevron-left` | 3 | ServerPagination, UsagePager, SearchableGuidSelect |
| `ArrowGo` | `ti-arrow-left` | 10 | Dashboard×5, Login, MyProfile, Delegations +2 more |
| `Back` | `ti-arrow-right` | 4 | AuditLog, Organizations, Users, Positions |
| `MoveUp` | `ti-arrow-up` | 1 | Workflows |
| `MoveDown` | `ti-arrow-down` | 1 | Workflows |
| `StageArrow` | `ti-arrow-down` | 5 | Workflows×2, RequestFlow×2, WorkflowStepTimeline |
| `ExpandAll` | `ti-arrows-maximize` | 3 | Organizations, Permissions, Positions |
| `CollapseAll` | `ti-arrows-minimize` | 3 | Organizations, Permissions, Positions |
| `Folder` | `ti-folder` | 3 | AccessManagement, PermissionGroups, Permissions |
| `List` | `ti-list` | 3 | AccessManagement, Workflows, PermissionGroups |
| `TreeExpand` | `ti-plus` | 1 | Positions |
| `TreeCollapse` | `ti-minus` | 1 | Positions |
| `SortAscending` | `ti-sort-ascending` | 1 | SortableHeader |
| `SortDescending` | `ti-sort-descending` | 1 | SortableHeader |
| `SortNone` | `ti-arrows-sort` | 1 | SortableHeader |

## State and feedback

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `Alert` | `ti-alert-circle` | 11 | Workflows×3, Login×2, MyProfile, Delegations +4 more |
| `Warning` | `ti-alert-triangle` | 2 | AppNotifications, Positions |
| `Info` | `ti-info-circle` | 3 | AppNotifications×2, MyPositions |
| `Success` | `ti-circle-check` | 12 | AppNotifications, MyProfile, MyPositions, Dashboard +8 more |
| `Failure` | `ti-circle-x` | 1 | AppNotifications |
| `Active` | `ti-circle-check` | 1 | AssignmentHistory |
| `Inactive` | `ti-circle-minus` | 2 | MyProfile, AuditLog |
| `Circle` | `ti-circle` | 1 | WorkflowDisplay |
| `Calendar` | `ti-calendar` | 1 | Appearance |
| `Clock` | `ti-clock` | 5 | AuditLog×2, WorkflowDisplay×2, RequestSummary |
| `Awaiting` | `ti-hourglass-high` | 6 | RequestSummary×2, WorkflowDisplay×2, Inbox, RequestNowLine |
| `Age` | `ti-hourglass` | 1 | RequestNowLine |
| `Bell` | `ti-bell` | 4 | Notifications×2, TopBarNotificationBell, PersistentSidebar |
| `NoNotifications` | `ti-bell-off` | 1 | Notifications |
| `LogIn` | `ti-login` | 2 | ApplicationLayout, ApplicationShell |
| `LogOut` | `ti-logout` | 2 | ApplicationLayout, ApplicationShell |
| `Code` | `ti-code` | 1 | Login |
| `EventCount` | `ti-database` | 1 | AuditLog |
| `Test` | `ti-test-pipe` | 2 | PasswordPolicy, Workflows |
| `Secure` | `ti-shield-lock` | 3 | Login, ApplicationLayout, PersistentSidebar |

## Account and appearance

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `Brand` | `ti-flower` | 1 | ApplicationLayout |
| `Dashboard` | `ti-layout-dashboard` | 2 | ApplicationLayout, PersistentSidebar |
| `Reports` | `ti-chart-bar` | 1 | Dashboard |
| `Profile` | `ti-user-circle` | 4 | Dashboard×2, ApplicationLayout, PersistentSidebar |
| `AccountGroup` | `ti-user-square-rounded` | 1 | PersistentSidebar |
| `Identity` | `ti-id` | 1 | MyProfile |
| `Username` | `ti-at` | 1 | MyProfile |
| `AccountType` | `ti-user-shield` | 2 | MyProfile×2 |
| `Avatar` | `ti-photo` | 1 | MyProfile |
| `Appearance` | `ti-palette` | 2 | Appearance, PersistentSidebar |
| `AppearanceReset` | `ti-rotate-2` | 1 | Appearance |
| `ThemeSystem` | `ti-device-desktop` | 1 | Appearance |
| `ThemeLight` | `ti-sun` | 1 | Appearance |
| `ThemeDark` | `ti-moon` | 1 | Appearance |
| `ChangePassword` | `ti-lock-password` | 1 | MyProfile |
| `PasswordField` | `ti-lock` | 1 | Login |
| `Restricted` | `ti-lock` | 4 | Delegations, Organizations, Workflows, TeamHistory |
| `PasswordPolicy` | `ti-lock-cog` | 2 | PasswordPolicy, PersistentSidebar |
| `PasswordRules` | `ti-adjustments` | 1 | PasswordPolicy |

## Users, access and permissions

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `User` | `ti-user` | 9 | AssignmentHistory×2, Login, AuditLog, AccessManagement +4 more |
| `Users` | `ti-users` | 8 | Organizations×2, PasswordPolicy, AccessManagement, Users +3 more |
| `UserAdd` | `ti-user-plus` | 1 | Users |
| `UserActivate` | `ti-user-check` | 1 | Users |
| `UserDeactivate` | `ti-user-off` | 1 | Users |
| `Requester` | `ti-user-pin` | 1 | NewRequestForOthers |
| `Admin` | `ti-user-shield` | 2 | AccessManagement, Users |
| `AccessGroup` | `ti-user-cog` | 1 | PersistentSidebar |
| `AccessManagement` | `ti-shield-cog` | 8 | Users×2, Permissions×2, AccessManagement, PermissionGroups +2 more |
| `AccessFinal` | `ti-shield-check` | 1 | AccessManagement |
| `AccessLevel` | `ti-shield-check` | 1 | Dashboard |
| `Permission` | `ti-key` | 8 | PermissionGroups×3, Permissions×2, AccessManagement, ApplicationLayout +1 more |
| `PermissionGroup` | `ti-stack-2` | 11 | Permissions×5, PermissionGroups×2, AccessManagement, PermissionGroupAssignment +2 more |
| `Assignments` | `ti-link` | 1 | PermissionGroups |
| `DirectAssignment` | `ti-user-check` | 3 | Permissions×3 |

## Organization, positions and delegation

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `Position` | `ti-briefcase` | 21 | MyPositions×3, AssignmentHistory×3, ActivePositionSelector×3, Dashboard×2 +7 more |
| `PositionVacant` | `ti-briefcase-off` | 3 | Positions×2, Notifications |
| `Occupied` | `ti-user-check` | 1 | Positions |
| `PositionTree` | `ti-sitemap` | 5 | Positions×3, Users×2 |
| `RootPosition` | `ti-tree` | 1 | Positions |
| `OpenBranches` | `ti-folder-open` | 1 | Positions |
| `OrgStructure` | `ti-hierarchy-3` | 2 | Login, PersistentSidebar |
| `Organization` | `ti-building` | 8 | Organizations×4, MyProfile, Dashboard, ApplicationLayout +1 more |
| `Subordinates` | `ti-users-group` | 3 | TeamHistory×2, PersistentSidebar |
| `History` | `ti-history` | 13 | AssignmentHistory×3, PositionAssignmentHistoryPanel×3, TeamHistory×2, Inbox×2 +3 more |
| `AuditLog` | `ti-file-search` | 4 | Login, AuditLog, ApplicationLayout, PersistentSidebar |
| `Delegation` | `ti-user-share` | 3 | Delegations, Inbox, PersistentSidebar |
| `DelegationGiven` | `ti-transfer-out` | 2 | Delegations×2 |
| `DelegationReceived` | `ti-transfer-in` | 2 | Delegations×2 |
| `SwitchPosition` | `ti-switch-horizontal` | 1 | MyPositions |
| `AssignEmployee` | `ti-link` | 1 | Positions |
| `ReplaceEmployee` | `ti-replace` | 1 | Positions |
| `FreePosition` | `ti-unlink` | 1 | Positions |
| `Deactivate` | `ti-player-pause` | 3 | Organizations, Workflows, Positions |
| `Activate` | `ti-player-play` | 3 | Organizations, Workflows, Positions |

## Requests and workflow

| Concept | Icon | Uses | Where |
|---|---|---|---|
| `RequestsGroup` | `ti-files` | 1 | PersistentSidebar |
| `Request` | `ti-file-text` | 1 | WorkflowDisplay |
| `NewRequest` | `ti-file-plus` | 5 | MyRequests×2, NewRequest, RequestFlow, PersistentSidebar |
| `MyRequests` | `ti-list-details` | 4 | NewRequest, TeamHistory, MyRequests, PersistentSidebar |
| `RequestsEmpty` | `ti-file-off` | 1 | MyRequests |
| `Inbox` | `ti-inbox` | 3 | Notifications, Inbox, PersistentSidebar |
| `NewRequestForOthers` | `ti-user-edit` | 5 | Notifications, AllRequests, NewRequestForOthers, MyRequests +1 more |
| `RequestsReport` | `ti-report-analytics` | 2 | AllRequests, PersistentSidebar |
| `ActiveRequests` | `ti-progress` | 5 | AllRequests, ActiveRequests, NewRequestForOthers, Positions +1 more |
| `RejectedRequests` | `ti-file-x` | 2 | AllRequests, PersistentSidebar |
| `Reopen` | `ti-restore` | 1 | RejectedRequests |
| `ForceClose` | `ti-ban` | 4 | Notifications, ActiveRequests, RequestSummary, RequestFlow |
| `Withdraw` | `ti-circle-x` | 2 | MyRequests, RequestFlow |
| `Rejected` | `ti-circle-x` | 3 | Notifications, RejectedRequests, RequestSummary |
| `Approved` | `ti-circle-check` | 2 | Notifications, RequestSummary |
| `Returned` | `ti-arrow-back-up` | 5 | Notifications×2, WorkflowDisplay×2, RequestSummary |
| `ReturnToRequester` | `ti-arrow-back-up` | 1 | Inbox |
| `ReturnToPreviousStep` | `ti-chevrons-right` | 1 | Inbox |
| `ReturnToSpecificStep` | `ti-target-arrow` | 1 | Inbox |
| `Approve` | `ti-check` | 1 | Inbox |
| `Reject` | `ti-x` | 1 | Inbox |
| `Resubmit` | `ti-refresh` | 1 | RequestFlow |
| `Workflow` | `ti-route` | 2 | Workflows, PersistentSidebar |
| `WorkflowStep` | `ti-flag` | 1 | Inbox |
| `RequestType` | `ti-category` | 1 | Workflows |
| `StepCount` | `ti-list-numbers` | 1 | Workflows |
| `ParallelApprover` | `ti-users-plus` | 1 | Workflows |
| `NextStep` | `ti-playlist-add` | 1 | Workflows |
| `Parallel` | `ti-arrows-split-2` | 2 | Workflows, WorkflowStepTimeline |
| `Superseded` | `ti-circle-minus` | 1 | WorkflowDisplay |
| `Skipped` | `ti-minus` | 1 | WorkflowDisplay |
| `TypeLeave` | `ti-calendar-off` | 1 | WorkflowDisplay |
| `TypePurchase` | `ti-shopping-cart` | 1 | WorkflowDisplay |
| `TypeAccess` | `ti-door-enter` | 1 | WorkflowDisplay |
| `TypeLoan` | `ti-cash` | 1 | WorkflowDisplay |
| `TypeHelpdesk` | `ti-headset` | 1 | WorkflowDisplay |
| `TypeWorkReport` | `ti-notes` | 1 | WorkflowDisplay |
| `TypeVehicle` | `ti-car` | 1 | WorkflowDisplay |
| `Spinner` | `ti-loader-2 icon-spin` | 19 | Permissions×2, MyRequests×2, ListFilterBar×2, App +12 more |
