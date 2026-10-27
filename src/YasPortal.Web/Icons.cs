namespace YasPortal.Web;

/// <summary>
/// Single source of truth for icons (Tabler Icons webfont, wwwroot/lib/tabler-icons).
/// One concept = one icon. Use as: <c>&lt;i class="ti @Icons.Delete"&gt;&lt;/i&gt;</c>.
/// Do not write literal ti-* class names in markup; add or reuse a concept here instead.
/// </summary>
public static class Icons
{
    // Generic UI actions
    public const string Close = "ti-x";
    public const string Confirm = "ti-check";
    public const string ConfirmAll = "ti-checks";
    public const string Save = "ti-device-floppy";
    public const string Delete = "ti-trash";
    public const string Search = "ti-search";
    public const string SearchEmpty = "ti-search-off";
    public const string Filter = "ti-filter";
    public const string FilterClear = "ti-filter-off";
    public const string CancelSearch = "ti-player-stop";
    public const string Add = "ti-plus";
    public const string Edit = "ti-pencil";
    public const string Details = "ti-eye";
    public const string More = "ti-dots";
    public const string Send = "ti-send";
    public const string Print = "ti-printer";
    public const string ExportData = "ti-file-export";
    public const string ExportSteps = "ti-file-spreadsheet";
    public const string Pdf = "ti-file-type-pdf";

    // Navigation and direction (RTL: forward points left, back points right)
    public const string ChevronDown = "ti-chevron-down";
    public const string ChevronUp = "ti-chevron-up";
    public const string PagePrev = "ti-chevron-right";
    public const string PageNext = "ti-chevron-left";
    public const string ArrowGo = "ti-arrow-left";
    public const string Back = "ti-arrow-right";
    public const string MoveUp = "ti-arrow-up";
    public const string MoveDown = "ti-arrow-down";
    public const string StageArrow = "ti-arrow-down";
    public const string ExpandAll = "ti-arrows-maximize";
    public const string CollapseAll = "ti-arrows-minimize";
    public const string Folder = "ti-folder";
    public const string List = "ti-list";
    public const string TreeExpand = "ti-plus";
    public const string TreeCollapse = "ti-minus";
    public const string SortAscending = "ti-sort-ascending";
    public const string SortDescending = "ti-sort-descending";
    public const string SortNone = "ti-arrows-sort";

    // State and feedback
    public const string Alert = "ti-alert-circle";
    public const string Warning = "ti-alert-triangle";
    public const string Info = "ti-info-circle";
    public const string Success = "ti-circle-check";
    public const string Failure = "ti-circle-x";
    public const string Active = "ti-circle-check";
    public const string Inactive = "ti-circle-minus";
    public const string Circle = "ti-circle";
    public const string Calendar = "ti-calendar";
    public const string Clock = "ti-clock";
    public const string Awaiting = "ti-hourglass-high";
    public const string Age = "ti-hourglass";
    public const string Bell = "ti-bell";
    public const string NoNotifications = "ti-bell-off";
    public const string LogIn = "ti-login";
    public const string LogOut = "ti-logout";
    public const string Code = "ti-code";
    public const string EventCount = "ti-database";
    public const string Test = "ti-test-pipe";
    public const string Secure = "ti-shield-lock";

    // Notifications
    public const string Message = "ti-message-circle";
    public const string SendNotification = "ti-bell-plus";
    public const string NotificationsReport = "ti-report-search";

    // Account and appearance
    public const string Brand = "ti-flower";
    public const string Dashboard = "ti-layout-dashboard";
    public const string Reports = "ti-chart-bar";
    public const string Profile = "ti-user-circle";
    public const string AccountGroup = "ti-user-square-rounded";
    public const string Identity = "ti-id";
    public const string Username = "ti-at";
    public const string AccountType = "ti-user-shield";
    public const string Avatar = "ti-photo";
    public const string Appearance = "ti-palette";
    public const string AppearanceReset = "ti-rotate-2";
    public const string ThemeSystem = "ti-device-desktop";
    public const string ThemeLight = "ti-sun";
    public const string ThemeDark = "ti-moon";
    public const string ChangePassword = "ti-lock-password";
    public const string PasswordField = "ti-lock";
    public const string Restricted = "ti-lock";
    public const string PasswordPolicy = "ti-lock-cog";
    public const string PasswordRules = "ti-adjustments";

    // Users, access and permissions
    public const string User = "ti-user";
    public const string Users = "ti-users";
    public const string UserAdd = "ti-user-plus";
    public const string UserActivate = "ti-user-check";
    public const string UserDeactivate = "ti-user-off";
    public const string Requester = "ti-user-pin";
    public const string Admin = "ti-user-shield";
    public const string AccessGroup = "ti-user-cog";
    public const string AccessManagement = "ti-shield-cog";
    public const string AccessFinal = "ti-shield-check";
    public const string AccessLevel = "ti-shield-check";
    public const string Permission = "ti-key";
    public const string PermissionGroup = "ti-stack-2";
    public const string Assignments = "ti-link";
    public const string DirectAssignment = "ti-user-check";

    // Organization, positions and delegation
    public const string Position = "ti-briefcase";
    public const string PositionVacant = "ti-briefcase-off";
    public const string Occupied = "ti-user-check";
    public const string PositionTree = "ti-sitemap";
    public const string RootPosition = "ti-tree";
    public const string OpenBranches = "ti-folder-open";
    public const string OrgStructure = "ti-hierarchy-3";
    public const string Organization = "ti-building";
    public const string Subordinates = "ti-users-group";
    public const string History = "ti-history";
    public const string AuditLog = "ti-file-search";
    public const string Delegation = "ti-user-share";
    public const string DelegationGiven = "ti-transfer-out";
    public const string DelegationReceived = "ti-transfer-in";
    public const string SwitchPosition = "ti-switch-horizontal";
    public const string AssignEmployee = "ti-link";
    public const string ReplaceEmployee = "ti-replace";
    public const string FreePosition = "ti-unlink";
    public const string Deactivate = "ti-player-pause";
    public const string Activate = "ti-player-play";

    // Requests and workflow
    public const string RequestsGroup = "ti-files";
    public const string Request = "ti-file-text";
    public const string NewRequest = "ti-file-plus";
    public const string MyRequests = "ti-list-details";
    public const string RequestsEmpty = "ti-file-off";
    public const string Inbox = "ti-inbox";
    public const string NewRequestForOthers = "ti-user-edit";
    public const string RequestsReport = "ti-report-analytics";
    public const string ActiveRequests = "ti-progress";
    public const string RejectedRequests = "ti-file-x";
    public const string Reopen = "ti-restore";
    public const string ForceClose = "ti-ban";
    public const string Withdraw = "ti-circle-x";
    public const string Rejected = "ti-circle-x";
    public const string Approved = "ti-circle-check";
    public const string Returned = "ti-arrow-back-up";
    public const string ReturnToRequester = "ti-arrow-back-up";
    public const string ReturnToPreviousStep = "ti-chevrons-right";
    public const string ReturnToSpecificStep = "ti-target-arrow";
    public const string Approve = "ti-check";
    public const string Reject = "ti-x";
    public const string Resubmit = "ti-refresh";
    public const string Workflow = "ti-route";
    public const string WorkflowStep = "ti-flag";
    public const string RequestType = "ti-category";
    public const string StepCount = "ti-list-numbers";
    public const string ParallelApprover = "ti-users-plus";
    public const string NextStep = "ti-playlist-add";
    public const string Parallel = "ti-arrows-split-2";
    public const string Superseded = "ti-circle-minus";
    public const string Skipped = "ti-minus";
    public const string TypeLeave = "ti-calendar-off";
    public const string TypePurchase = "ti-shopping-cart";
    public const string TypeAccess = "ti-door-enter";
    public const string TypeLoan = "ti-cash";
    public const string TypeHelpdesk = "ti-headset";
    public const string TypeWorkReport = "ti-notes";
    public const string TypeVehicle = "ti-car";

    // Spinning loader: includes the .icon-spin helper class (components.css).
    public const string Spinner = "ti-loader-2 icon-spin";
}
