using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Application.Persistence;
using YasPortal.Domain.Auditing;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Development;
using YasPortal.Infrastructure.Guides;
using YasPortal.Infrastructure.Store;
using YasPortal.Infrastructure.Landing;
using YasPortal.Infrastructure.PhoneBook;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Claim type used to throttle how often OnValidatePrincipal / SessionGuard actually hit the
// database for a given cookie, rather than on every request. Declared here, at the top, because
// it is referenced both by the /account/reauth-check endpoint below and by OnValidatePrincipal
// further down, and a local const in top-level statements must be declared before any lambda or
// local function that closes over it, regardless of when that lambda actually runs.
const string LastValidatedClaimType = "yas_last_validated_utc";

builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    // A guide's text travels from the editor to the server in one message; the 32 KB default is too small for a long guide.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024);
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = "YasPortal.Auth";
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // The 8-hour sliding cookie otherwise carries a snapshot of permissions taken at sign-in for
    // its whole life, so deactivating an employee, ending their position, or revoking a permission
    // would not take effect until they happen to sign in again. This re-checks against the
    // database periodically (throttled — see ValidateAndRefreshPrincipalAsync) and either signs
    // the user out or reissues the cookie with a fresh permission snapshot.
    options.Events = new CookieAuthenticationEvents { OnValidatePrincipal = ValidateAndRefreshPrincipalAsync };
});
builder.Services.AddAuthorization(options => {
    foreach (var permission in PermissionCatalog.Codes)
        options.AddPolicy(permission, policy => policy.Requirements.Add(new PermissionRequirement(permission)));
    // Administrator accounts have no organizational position, so the personal request pages (new / mine / inbox) are not for them.
    // The "team" page is open to anyone holding at least one of the two subordinate-history permissions (the page itself shows only the tabs they hold).
    options.AddPolicy("Requests.ViewSubordinates", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindAll(AuthClaimNames.Permission).Any(c => c.Value is Perm.RequestsTeamView or Perm.RequestsTeamApprovalsView)));
    // The guides workspace is open to anyone who holds one of the three guide permissions; the page then limits what each person can see and change.
    options.AddPolicy(Perm.StoreAdminPolicy, policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindAll(AuthClaimNames.Permission).Any(c => c.Value is Perm.StoreItemsView or Perm.StoreItemsManage or Perm.StoreStockView or Perm.StoreStockManage or Perm.StoreOrdersIssue or Perm.StoreReportsView)));
    options.AddPolicy(Perm.GuidesWorkspacePolicy, policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
        ctx.User.FindAll(AuthClaimNames.Permission).Any(c => c.Value is Perm.AdminGuidesView or Perm.AdminGuidesManage or Perm.GuidesOwnManage)));
    options.AddPolicy("NonAdmin", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx => !ctx.User.HasClaim("yas_is_admin", "True")));
    // Administrators' own notifications (vacant-position alerts) live on /admin/notifications; /notifications is for everyone else.
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx => ctx.User.HasClaim("yas_is_admin", "True")));
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddScoped<AppState>();
builder.Services.AddSingleton<AuditActorContext>();
builder.Services.AddScoped<AuditEventWriter>();
builder.Services.AddScoped<AuditArchiver>();
builder.Services.AddSingleton<LandingContent>();
builder.Services.AddScoped<HomeSlideProvider>();
builder.Services.AddScoped<GuideProvider>();
builder.Services.AddScoped<StoreService>();
builder.Services.AddScoped<PortalLinkProvider>();
builder.Services.AddScoped<PhoneBookProvider>();
builder.Services.AddScoped<AuditArchiveSettingsService>();
builder.Services.AddHostedService<AuditArchiveWorker>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<PermissionChecker>();
builder.Services.AddScoped<IPermissionChecker>(sp => sp.GetRequiredService<PermissionChecker>());
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
builder.Services.AddScoped<AdminQueryService>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<StoreOrderService>();
builder.Services.AddScoped<StoreReportService>();
builder.Services.AddScoped<StoreLotteryService>();
builder.Services.AddScoped<StoreCart>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<RequestReportBuilder>();
builder.Services.AddScoped<RequestPdfService>();
// Report customizers run in this order and may change the model of every request PDF (see IRequestReportCustomizer).
builder.Services.AddScoped<IRequestReportCustomizer, DefaultRequestReportCustomizer>();
// One printable form per request type; a type without a design has no «فرم چاپی» button.
builder.Services.AddScoped<IRequestFormDesign, VehicleRequestFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, LeaveFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, PurchaseFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, AccessFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, LoanFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, HelpdeskFormDesign>();
builder.Services.AddScoped<IRequestFormDesign, WorkReportFormDesign>();
builder.Services.AddScoped<UserPreferenceService>();
builder.Services.AddScoped<PasswordPolicyService>();
builder.Services.AddScoped<AvatarService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Employee>>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'dbo.PositionAssignmentHistories', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[PositionAssignmentHistories] ([Id] uniqueidentifier NOT NULL,[EmployeeId] uniqueidentifier NOT NULL,[PositionId] uniqueidentifier NOT NULL,[StartedAt] datetime2 NULL,[EndedAt] datetime2 NULL,CONSTRAINT [PK_PositionAssignmentHistories] PRIMARY KEY ([Id]),CONSTRAINT [FK_PositionAssignmentHistories_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE CASCADE,CONSTRAINT [FK_PositionAssignmentHistories_Positions_PositionId] FOREIGN KEY ([PositionId]) REFERENCES [dbo].[Positions] ([Id]) ON DELETE NO ACTION);
            CREATE INDEX [IX_PositionAssignmentHistories_EmployeeId_StartedAt] ON [dbo].[PositionAssignmentHistories] ([EmployeeId], [StartedAt]);
            CREATE INDEX [IX_PositionAssignmentHistories_PositionId_StartedAt] ON [dbo].[PositionAssignmentHistories] ([PositionId], [StartedAt]);
            CREATE UNIQUE INDEX [IX_PositionAssignmentHistories_PositionId] ON [dbo].[PositionAssignmentHistories] ([PositionId]) WHERE [EndedAt] IS NULL;
        END;
        """);
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO [dbo].[PositionAssignmentHistories] ([Id], [EmployeeId], [PositionId], [StartedAt], [EndedAt])
        SELECT NEWID(), ep.[EmployeeId], ep.[PositionId], NULL, ep.[EndedAt] FROM [dbo].[EmployeePositions] ep
        WHERE NOT EXISTS (SELECT 1 FROM [dbo].[PositionAssignmentHistories] h WHERE h.[EmployeeId] = ep.[EmployeeId] AND h.[PositionId] = ep.[PositionId] AND ((h.[EndedAt] IS NULL AND ep.[EndedAt] IS NULL) OR (h.[EndedAt] = ep.[EndedAt])));
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions') AND name = N'IX_EmployeePositions_PositionId') DROP INDEX [IX_EmployeePositions_PositionId] ON [dbo].[EmployeePositions];
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions') AND name = N'IX_EmployeePositions_PositionId') CREATE UNIQUE INDEX [IX_EmployeePositions_PositionId] ON [dbo].[EmployeePositions] ([PositionId]) WHERE [EndedAt] IS NULL;
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH(N'dbo.Employees', N'AvatarKey') IS NULL ALTER TABLE [dbo].[Employees] ADD [AvatarKey] nvarchar(40) NULL;
        IF COL_LENGTH(N'dbo.WorkflowRequests', N'CreatedByEmployeeId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[WorkflowRequests] ADD [CreatedByEmployeeId] uniqueidentifier NULL;
            CREATE INDEX [IX_WorkflowRequests_CreatedByEmployeeId] ON [dbo].[WorkflowRequests] ([CreatedByEmployeeId]);
            ALTER TABLE [dbo].[WorkflowRequests] ADD CONSTRAINT [FK_WorkflowRequests_Employees_CreatedByEmployeeId] FOREIGN KEY ([CreatedByEmployeeId]) REFERENCES [dbo].[Employees] ([Id]);
        END;
        IF COL_LENGTH(N'dbo.WorkflowStepDefinitions', N'FillsPart') IS NULL
        BEGIN
            ALTER TABLE [dbo].[WorkflowStepDefinitions] ADD [FillsPart] int NULL;
            EXEC(N'UPDATE [dbo].[WorkflowStepDefinitions] SET [FillsPart] = [Order] + 1 WHERE [WorkflowType] = N''VehicleRequest'' AND [Order] IN (1, 2)');
        END;
        IF COL_LENGTH(N'dbo.WorkflowRequestSteps', N'FillsPart') IS NULL
        BEGIN
            ALTER TABLE [dbo].[WorkflowRequestSteps] ADD [FillsPart] int NULL;
            EXEC(N'UPDATE s SET s.[FillsPart] = s.[Order] + 1 FROM [dbo].[WorkflowRequestSteps] s INNER JOIN [dbo].[WorkflowRequests] r ON r.[Id] = s.[RequestId] WHERE r.[WorkflowType] = N''VehicleRequest'' AND s.[Order] IN (1, 2)');
        END;
        IF COL_LENGTH(N'dbo.Notifications', N'SenderEmployeeId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Notifications] ADD [SenderEmployeeId] uniqueidentifier NULL;
            EXEC(N'ALTER TABLE [dbo].[Notifications] ADD CONSTRAINT [FK_Notifications_Employees_SenderEmployeeId] FOREIGN KEY ([SenderEmployeeId]) REFERENCES [dbo].[Employees] ([Id])');
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_SenderEmployeeId_CreatedAtUtc')
            EXEC(N'CREATE INDEX [IX_Notifications_SenderEmployeeId_CreatedAtUtc] ON [dbo].[Notifications] ([SenderEmployeeId], [CreatedAtUtc])');
        IF COL_LENGTH(N'dbo.Notifications', N'BatchId') IS NULL ALTER TABLE [dbo].[Notifications] ADD [BatchId] uniqueidentifier NULL;
        IF COL_LENGTH(N'dbo.Notifications', N'AudienceLabel') IS NULL ALTER TABLE [dbo].[Notifications] ADD [AudienceLabel] nvarchar(300) NULL;
        IF COL_LENGTH(N'dbo.Notifications', N'RemovedAtUtc') IS NULL ALTER TABLE [dbo].[Notifications] ADD [RemovedAtUtc] datetime2 NULL;
        IF COL_LENGTH(N'dbo.Notifications', N'RemovedByEmployeeId') IS NULL
        BEGIN
            ALTER TABLE [dbo].[Notifications] ADD [RemovedByEmployeeId] uniqueidentifier NULL;
            EXEC(N'ALTER TABLE [dbo].[Notifications] ADD CONSTRAINT [FK_Notifications_Employees_RemovedByEmployeeId] FOREIGN KEY ([RemovedByEmployeeId]) REFERENCES [dbo].[Employees] ([Id])');
        END;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_BatchId')
            EXEC(N'CREATE INDEX [IX_Notifications_BatchId] ON [dbo].[Notifications] ([BatchId])');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_CreatedAtUtc')
            EXEC(N'CREATE INDEX [IX_Notifications_CreatedAtUtc] ON [dbo].[Notifications] ([CreatedAtUtc])');
        IF OBJECT_ID(N'dbo.RecipientGroups', N'U') IS NULL
            CREATE TABLE [dbo].[RecipientGroups] ([Id] uniqueidentifier NOT NULL, [OwnerEmployeeId] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [CreatedAtUtc] datetime2 NOT NULL, [UpdatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_RecipientGroups] PRIMARY KEY ([Id]), CONSTRAINT [FK_RecipientGroups_Employees_OwnerEmployeeId] FOREIGN KEY ([OwnerEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE CASCADE);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RecipientGroups') AND name = N'IX_RecipientGroups_OwnerEmployeeId_Name')
            EXEC(N'CREATE UNIQUE INDEX [IX_RecipientGroups_OwnerEmployeeId_Name] ON [dbo].[RecipientGroups] ([OwnerEmployeeId], [Name])');
        IF OBJECT_ID(N'dbo.RecipientGroupMembers', N'U') IS NULL
            CREATE TABLE [dbo].[RecipientGroupMembers] ([GroupId] uniqueidentifier NOT NULL, [EmployeeId] uniqueidentifier NOT NULL, CONSTRAINT [PK_RecipientGroupMembers] PRIMARY KEY ([GroupId], [EmployeeId]), CONSTRAINT [FK_RecipientGroupMembers_RecipientGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[RecipientGroups] ([Id]) ON DELETE CASCADE, CONSTRAINT [FK_RecipientGroupMembers_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RecipientGroupMembers') AND name = N'IX_RecipientGroupMembers_EmployeeId')
            EXEC(N'CREATE INDEX [IX_RecipientGroupMembers_EmployeeId] ON [dbo].[RecipientGroupMembers] ([EmployeeId])');
        IF OBJECT_ID(N'dbo.PasswordPolicies', N'U') IS NULL
            CREATE TABLE [dbo].[PasswordPolicies] ([Id] int NOT NULL, [MinLength] int NOT NULL, [MaxLength] int NOT NULL, [RequireUppercase] bit NOT NULL, [RequireLowercase] bit NOT NULL, [RequireDigit] bit NOT NULL, [RequireSymbol] bit NOT NULL, [DisallowUsername] bit NOT NULL, [DisallowCommonPasswords] bit NOT NULL, [DisallowReuseOfCurrent] bit NOT NULL, [UpdatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_PasswordPolicies] PRIMARY KEY ([Id]));
        """);
    await db.Database.ExecuteSqlRawAsync("""
        IF COL_LENGTH(N'dbo.AuditEntries', N'ArchivedAtUtc') IS NULL ALTER TABLE [dbo].[AuditEntries] ADD [ArchivedAtUtc] datetime2 NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditEntries') AND name = N'IX_AuditEntries_ArchivedAtUtc_OccurredAtUtc')
            EXEC(N'CREATE INDEX [IX_AuditEntries_ArchivedAtUtc_OccurredAtUtc] ON [dbo].[AuditEntries] ([ArchivedAtUtc], [OccurredAtUtc])');
        IF OBJECT_ID(N'dbo.AuditArchiveSettings', N'U') IS NULL
            CREATE TABLE [dbo].[AuditArchiveSettings] ([Id] int NOT NULL, [AutomaticEnabled] bit NOT NULL, [AfterMonths] int NOT NULL, [LastAutomaticRunAtUtc] datetime2 NULL, [LastAutomaticRunCount] int NOT NULL, [UpdatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_AuditArchiveSettings] PRIMARY KEY ([Id]));
        IF OBJECT_ID(N'dbo.HomeSlides', N'U') IS NULL
            CREATE TABLE [dbo].[HomeSlides] ([Id] uniqueidentifier NOT NULL, [Title] nvarchar(200) NOT NULL, [Text] nvarchar(600) NOT NULL, [Icon] nvarchar(50) NOT NULL, [LinkText] nvarchar(100) NULL, [LinkUrl] nvarchar(300) NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, [StartsOn] date NULL, [ExpiresOn] date NULL, [CreatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_HomeSlides] PRIMARY KEY ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HomeSlides') AND name = N'IX_HomeSlides_IsActive_SortOrder')
            EXEC(N'CREATE INDEX [IX_HomeSlides_IsActive_SortOrder] ON [dbo].[HomeSlides] ([IsActive], [SortOrder])');
        IF OBJECT_ID(N'dbo.LinkGroups', N'U') IS NULL
            CREATE TABLE [dbo].[LinkGroups] ([Id] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [ParentId] uniqueidentifier NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_LinkGroups] PRIMARY KEY ([Id]), CONSTRAINT [FK_LinkGroups_LinkGroups_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[LinkGroups] ([Id]));
        IF OBJECT_ID(N'dbo.PortalLinks', N'U') IS NULL
            CREATE TABLE [dbo].[PortalLinks] ([Id] uniqueidentifier NOT NULL, [Title] nvarchar(150) NOT NULL, [Url] nvarchar(500) NOT NULL, [Description] nvarchar(300) NULL, [GroupId] uniqueidentifier NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, [IsQuickAccess] bit NOT NULL, [QuickOrder] int NOT NULL, CONSTRAINT [PK_PortalLinks] PRIMARY KEY ([Id]), CONSTRAINT [FK_PortalLinks_LinkGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[LinkGroups] ([Id]));
        IF OBJECT_ID(N'dbo.ContactGroups', N'U') IS NULL
            CREATE TABLE [dbo].[ContactGroups] ([Id] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_ContactGroups] PRIMARY KEY ([Id]));
        IF OBJECT_ID(N'dbo.Contacts', N'U') IS NULL
            CREATE TABLE [dbo].[Contacts] ([Id] uniqueidentifier NOT NULL, [GroupId] uniqueidentifier NOT NULL, [FullName] nvarchar(150) NOT NULL, [JobTitle] nvarchar(150) NULL, [Extension] nvarchar(30) NULL, [Phone] nvarchar(30) NULL, [Mobile] nvarchar(30) NULL, [Email] nvarchar(150) NULL, [Note] nvarchar(300) NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_Contacts] PRIMARY KEY ([Id]), CONSTRAINT [FK_Contacts_ContactGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[ContactGroups] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Contacts') AND name = N'IX_Contacts_GroupId')
            EXEC(N'CREATE INDEX [IX_Contacts_GroupId] ON [dbo].[Contacts] ([GroupId])');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LinkGroups') AND name = N'IX_LinkGroups_ParentId')
            EXEC(N'CREATE INDEX [IX_LinkGroups_ParentId] ON [dbo].[LinkGroups] ([ParentId])');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PortalLinks') AND name = N'IX_PortalLinks_GroupId')
            EXEC(N'CREATE INDEX [IX_PortalLinks_GroupId] ON [dbo].[PortalLinks] ([GroupId])');
        IF OBJECT_ID(N'dbo.HomeSlideAttachments', N'U') IS NULL
            CREATE TABLE [dbo].[HomeSlideAttachments] ([SlideId] uniqueidentifier NOT NULL, [FileName] nvarchar(200) NOT NULL, [ContentType] nvarchar(150) NOT NULL, [SizeBytes] bigint NOT NULL, [Content] varbinary(max) NOT NULL, [UploadedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_HomeSlideAttachments] PRIMARY KEY ([SlideId]), CONSTRAINT [FK_HomeSlideAttachments_HomeSlides_SlideId] FOREIGN KEY ([SlideId]) REFERENCES [dbo].[HomeSlides] ([Id]) ON DELETE CASCADE);
        IF OBJECT_ID(N'dbo.StoreCategories', N'U') IS NULL
            CREATE TABLE [dbo].[StoreCategories] ([Id] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_StoreCategories] PRIMARY KEY ([Id]));
        IF OBJECT_ID(N'dbo.StoreItems', N'U') IS NULL
            CREATE TABLE [dbo].[StoreItems] ([Id] uniqueidentifier NOT NULL, [Code] nvarchar(50) NOT NULL, [Name] nvarchar(200) NOT NULL, [CategoryId] uniqueidentifier NOT NULL, [Unit] nvarchar(30) NOT NULL, [Description] nvarchar(500) NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_StoreItems] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreItems_StoreCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[StoreCategories] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'IX_StoreItems_Code')
            EXEC(N'CREATE UNIQUE INDEX [IX_StoreItems_Code] ON [dbo].[StoreItems] ([Code])');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'IX_StoreItems_CategoryId')
            EXEC(N'CREATE INDEX [IX_StoreItems_CategoryId] ON [dbo].[StoreItems] ([CategoryId])');
        IF COL_LENGTH(N'dbo.StoreItems', N'MinStock') IS NOT NULL ALTER TABLE [dbo].[StoreItems] DROP COLUMN [MinStock];
        IF COL_LENGTH(N'dbo.StoreItems', N'MaxPerRequest') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [MaxPerRequest] decimal(18,3) NOT NULL CONSTRAINT [DF_StoreItems_MaxPerRequest] DEFAULT 0;
        IF COL_LENGTH(N'dbo.StoreItems', N'RequestOpensOn') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [RequestOpensOn] datetime2(0) NULL;
        IF COL_LENGTH(N'dbo.StoreItems', N'RequestClosesOn') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [RequestClosesOn] datetime2(0) NULL;
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'RequestOpensOn' AND system_type_id = TYPE_ID(N'date'))
            ALTER TABLE [dbo].[StoreItems] ALTER COLUMN [RequestOpensOn] datetime2(0) NULL;
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'RequestClosesOn' AND system_type_id = TYPE_ID(N'date'))
        BEGIN
            ALTER TABLE [dbo].[StoreItems] ALTER COLUMN [RequestClosesOn] datetime2(0) NULL;
            -- an end date used to mean the whole day: keep it open until the end of that day
            EXEC(N'UPDATE [dbo].[StoreItems] SET [RequestClosesOn] = DATEADD(MINUTE, 1439, [RequestClosesOn]) WHERE [RequestClosesOn] IS NOT NULL');
        END
        IF COL_LENGTH(N'dbo.StoreItems', N'IsLottery') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [IsLottery] bit NOT NULL CONSTRAINT [DF_StoreItems_IsLottery] DEFAULT 0;
        IF COL_LENGTH(N'dbo.StoreItems', N'IsVisible') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [IsVisible] bit NOT NULL CONSTRAINT [DF_StoreItems_IsVisible] DEFAULT 1;
        IF OBJECT_ID(N'dbo.StoreStockMovements', N'U') IS NULL
            CREATE TABLE [dbo].[StoreStockMovements] ([Id] uniqueidentifier NOT NULL, [ItemId] uniqueidentifier NOT NULL, [Kind] int NOT NULL, [Delta] decimal(18,3) NOT NULL, [Reference] nvarchar(200) NULL, [Note] nvarchar(300) NULL, [CreatedByEmployeeId] uniqueidentifier NULL, [OrderId] uniqueidentifier NULL, [CreatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_StoreStockMovements] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreStockMovements_StoreItems_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[StoreItems] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreStockMovements') AND name = N'IX_StoreStockMovements_ItemId_CreatedAtUtc')
            EXEC(N'CREATE INDEX [IX_StoreStockMovements_ItemId_CreatedAtUtc] ON [dbo].[StoreStockMovements] ([ItemId], [CreatedAtUtc])');
        IF OBJECT_ID(N'dbo.StoreOrders', N'U') IS NULL
            CREATE TABLE [dbo].[StoreOrders] ([Id] uniqueidentifier NOT NULL, [RequestId] uniqueidentifier NOT NULL, [EmployeeId] uniqueidentifier NOT NULL, [PositionId] uniqueidentifier NOT NULL, [Note] nvarchar(500) NULL, [CreatedAtUtc] datetime2 NOT NULL, [IssuedAtUtc] datetime2 NULL, [IssuedByEmployeeId] uniqueidentifier NULL, [IssueNote] nvarchar(500) NULL, CONSTRAINT [PK_StoreOrders] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreOrders_WorkflowRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[WorkflowRequests] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrders') AND name = N'IX_StoreOrders_RequestId')
            EXEC(N'CREATE UNIQUE INDEX [IX_StoreOrders_RequestId] ON [dbo].[StoreOrders] ([RequestId])');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrders') AND name = N'IX_StoreOrders_EmployeeId_CreatedAtUtc')
            EXEC(N'CREATE INDEX [IX_StoreOrders_EmployeeId_CreatedAtUtc] ON [dbo].[StoreOrders] ([EmployeeId], [CreatedAtUtc])');
        IF OBJECT_ID(N'dbo.StoreOrderLines', N'U') IS NULL
            CREATE TABLE [dbo].[StoreOrderLines] ([Id] uniqueidentifier NOT NULL, [OrderId] uniqueidentifier NOT NULL, [ItemId] uniqueidentifier NOT NULL, [ItemCode] nvarchar(50) NOT NULL, [ItemName] nvarchar(200) NOT NULL, [Unit] nvarchar(30) NOT NULL, [Quantity] decimal(18,3) NOT NULL, CONSTRAINT [PK_StoreOrderLines] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreOrderLines_StoreOrders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[StoreOrders] ([Id]) ON DELETE CASCADE, CONSTRAINT [FK_StoreOrderLines_StoreItems_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[StoreItems] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrderLines') AND name = N'IX_StoreOrderLines_OrderId')
            EXEC(N'CREATE INDEX [IX_StoreOrderLines_OrderId] ON [dbo].[StoreOrderLines] ([OrderId])');
        IF OBJECT_ID(N'dbo.StoreLotteryEntries', N'U') IS NULL
            CREATE TABLE [dbo].[StoreLotteryEntries] ([Id] uniqueidentifier NOT NULL, [ItemId] uniqueidentifier NOT NULL, [EmployeeId] uniqueidentifier NOT NULL, [PositionId] uniqueidentifier NOT NULL, [Quantity] decimal(18,3) NOT NULL, [Status] nvarchar(20) NOT NULL, [AppliedAtUtc] datetime2 NOT NULL, [DrawnAtUtc] datetime2 NULL, [OrderId] uniqueidentifier NULL, CONSTRAINT [PK_StoreLotteryEntries] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreLotteryEntries_StoreItems_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[StoreItems] ([Id]));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreLotteryEntries') AND name = N'IX_StoreLotteryEntries_ItemId_EmployeeId')
            EXEC(N'CREATE UNIQUE INDEX [IX_StoreLotteryEntries_ItemId_EmployeeId] ON [dbo].[StoreLotteryEntries] ([ItemId], [EmployeeId]) WHERE [Status] = N''Applied''');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreLotteryEntries') AND name = N'IX_StoreLotteryEntries_EmployeeId_AppliedAtUtc')
            EXEC(N'CREATE INDEX [IX_StoreLotteryEntries_EmployeeId_AppliedAtUtc] ON [dbo].[StoreLotteryEntries] ([EmployeeId], [AppliedAtUtc])');
        IF OBJECT_ID(N'dbo.GuideBlocks', N'U') IS NOT NULL DROP TABLE [dbo].[GuideBlocks];
        IF OBJECT_ID(N'dbo.UserGuides', N'U') IS NULL
            CREATE TABLE [dbo].[UserGuides] ([Id] uniqueidentifier NOT NULL, [Title] nvarchar(200) NOT NULL, [Summary] nvarchar(300) NULL, [ContentHtml] nvarchar(max) NOT NULL, [Visibility] int NOT NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, [CreatedAtUtc] datetime2 NOT NULL, [UpdatedAtUtc] datetime2 NOT NULL, [CreatedByEmployeeId] uniqueidentifier NULL, CONSTRAINT [PK_UserGuides] PRIMARY KEY ([Id]));
        IF COL_LENGTH(N'dbo.UserGuides', N'Direction') IS NOT NULL ALTER TABLE [dbo].[UserGuides] DROP COLUMN [Direction];
        IF COL_LENGTH(N'dbo.UserGuides', N'RelatedPermissionCode') IS NOT NULL ALTER TABLE [dbo].[UserGuides] DROP COLUMN [RelatedPermissionCode];
        IF COL_LENGTH(N'dbo.UserGuides', N'ContentHtml') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [ContentHtml] nvarchar(max) NOT NULL CONSTRAINT [DF_UserGuides_ContentHtml] DEFAULT N'';
        IF COL_LENGTH(N'dbo.UserGuides', N'Visibility') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [Visibility] int NOT NULL CONSTRAINT [DF_UserGuides_Visibility] DEFAULT 1;
        IF COL_LENGTH(N'dbo.UserGuides', N'CreatedByEmployeeId') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [CreatedByEmployeeId] uniqueidentifier NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserGuides') AND name = N'IX_UserGuides_IsActive_SortOrder')
            EXEC(N'CREATE INDEX [IX_UserGuides_IsActive_SortOrder] ON [dbo].[UserGuides] ([IsActive], [SortOrder])');
        """);
    await DevelopmentDataSeeder.SeedAsync(db, passwordHasher);
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapPost("/account/login", async (HttpContext http, ApplicationDbContext db, IPasswordHasher<Employee> passwordHasher, IAntiforgery antiforgery, AuditEventWriter audit) => {
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        return Results.Redirect("/login?error=1");
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var employee = await db.Employees.Include(x => x.Positions).SingleOrDefaultAsync(x => x.Username.ToLower() == username.ToLower() && x.IsActive);
    if (employee is null || string.IsNullOrWhiteSpace(employee.PasswordHash))
    {
        // The typed username is kept (cut short) so repeated guesses can be spotted; the password never is.
        await audit.WriteAsync(AuditActions.LoginFailed, AuditEntityTypes.Session, null,
            new { Username = username.Length > 100 ? username[..100] : username, Reason = AuditReasons.UnknownOrInactive, Ip = ip }, anonymous: true);
        return Results.Redirect("/login?error=1");
    }
    var passwordResult = passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
    if (passwordResult == PasswordVerificationResult.Failed)
    {
        await audit.WriteAsync(AuditActions.LoginFailed, AuditEntityTypes.Session, employee.Id.ToString(),
            new { employee.Username, Reason = AuditReasons.BadPassword, Ip = ip }, employeeId: employee.Id, anonymous: true);
        return Results.Redirect("/login?error=1");
    }
    if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
        employee.SetPasswordHash(passwordHasher.HashPassword(employee, password));

    Guid? activePositionId = null;
    if (!employee.IsAdmin)
    {
        activePositionId = employee.Positions.Where(x => x.EndedAt == null && x.PositionId == employee.LastActivePositionId).Select(x => (Guid?)x.PositionId).FirstOrDefault();
        activePositionId ??= employee.Positions.Where(x => x.EndedAt == null).Select(x => (Guid?)x.PositionId).FirstOrDefault();
        if (activePositionId is null)
        {
            await audit.WriteAsync(AuditActions.LoginFailed, AuditEntityTypes.Session, employee.Id.ToString(),
                new { employee.Username, Reason = AuditReasons.NoPosition, Ip = ip }, employeeId: employee.Id, anonymous: true);
            return Results.Redirect("/login?error=no-position");
        }
    }

    if (employee.LastActivePositionId != activePositionId)
        employee.SetLastActivePosition(activePositionId);
    await db.SaveChangesAsync();
    await audit.WriteAsync(AuditActions.LoginSucceeded, AuditEntityTypes.Session, employee.Id.ToString(),
        new { employee.Username, Ip = ip }, employeeId: employee.Id, activePositionId: activePositionId, anonymous: true);

    var permissionSnapshot = await BuildPermissionSnapshotAsync(db, employee.Id, activePositionId, employee.IsAdmin);
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
        new(ClaimTypes.Name, employee.Username),
        new(AuthClaimNames.IsAdmin, employee.IsAdmin.ToString())
    };
    if (activePositionId is Guid positionId)
        claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));
    claims.AddRange(permissionSnapshot.Select(code => new Claim(AuthClaimNames.Permission, code)));

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/");
});

app.MapPost("/account/position", async (HttpContext http, ApplicationDbContext db, IAntiforgery antiforgery, AuditEventWriter audit) => {
    if (!(http.User.Identity?.IsAuthenticated ?? false))
        return Results.Redirect("/login");
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var employeeIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var positionIdValue = form["positionId"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    if (!Guid.TryParse(employeeIdValue, out var employeeId) || !Guid.TryParse(positionIdValue, out var positionId))
        return Results.Redirect("/my-positions");

    var employee = await db.Employees.Include(x => x.Positions).SingleOrDefaultAsync(x => x.Id == employeeId && x.IsActive && !x.IsAdmin);
    if (employee is null)
        return Results.Redirect("/login");
    if (!employee.Positions.Any(x => x.PositionId == positionId && x.EndedAt == null))
        return Results.Redirect("/my-positions");

    var previousPositionId = Guid.TryParse(http.User.FindFirstValue(AuthClaimNames.ActivePositionId), out var previous) ? previous : (Guid?)null;
    employee.SetLastActivePosition(positionId);
    await db.SaveChangesAsync();
    if (previousPositionId != positionId)
        await audit.WriteAsync(AuditActions.PositionSwitched, AuditEntityTypes.Session, employee.Id.ToString(),
            new { FromPositionId = previousPositionId, ToPositionId = positionId }, employeeId: employee.Id, activePositionId: positionId, anonymous: true);

    // Rebuild the snapshot for the newly selected position. Old permission claims
    // are explicitly removed so permissions from the previous position cannot leak.
    var permissionSnapshot = await BuildPermissionSnapshotAsync(db, employee.Id, positionId, isAdmin: false);
    var claims = http.User.Claims
        .Where(c => c.Type != AuthClaimNames.ActivePositionId && c.Type != AuthClaimNames.Permission)
        .ToList();
    claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));
    claims.AddRange(permissionSnapshot.Select(code => new Claim(AuthClaimNames.Permission, code)));

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    if (IsSafeLocalReturnUrl(returnUrl))
        return Results.Redirect(returnUrl);
    return Results.Redirect("/my-positions");
});

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery, AuditEventWriter audit) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var (employeeId, positionId) = ActorOf(http.User);
    if (employeeId is not null)
        await audit.WriteAsync(AuditActions.LoggedOut, AuditEntityTypes.Session, employeeId.ToString(), null, employeeId: employeeId, activePositionId: positionId, anonymous: true);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");   // the public home page; its top bar and buttons lead to the sign-in screen
});

// Called by SessionGuard (a component mounted in every authenticated page) when it notices, from
// inside a long-lived Blazor Server circuit, that the signed-in identity may no longer be valid.
// A circuit can run for hours without making another HTTP request — in-app navigation stays on
// the same SignalR connection — so OnValidatePrincipal below, which only runs on real requests,
// cannot catch that on its own; this endpoint is what a forced page reload actually lands on.
// Deliberately a plain GET, no antiforgery token: every value this reads (identity, admin flag,
// active position, permissions) comes from the authenticated cookie and the database, not from
// the request, so a crafted link cannot make it produce anything other than what the visiting
// user's own account already legitimately has. The only effect of an unwanted visit is the same
// early-logout nuisance any GET logout link already carries.
app.MapGet("/account/reauth-check", async (HttpContext http, IDbContextFactory<ApplicationDbContext> dbFactory, AuditEventWriter audit, string? returnUrl) =>
{
    var principal = http.User;
    if (principal.Identity?.IsAuthenticated != true)
        return Results.Redirect("/login");
    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    await using var db = await dbFactory.CreateDbContextAsync(http.RequestAborted);
    var employee = await db.Employees.AsNoTracking().Include(x => x.Positions)
        .SingleOrDefaultAsync(x => x.Id == employeeId, http.RequestAborted);
    var claimedIsAdmin = bool.TryParse(principal.FindFirstValue(AuthClaimNames.IsAdmin), out var isAdminValue) && isAdminValue;
    Guid? claimedPositionId = Guid.TryParse(principal.FindFirstValue(AuthClaimNames.ActivePositionId), out var positionValue) ? positionValue : null;

    if (!IdentitySnapshotValidator.IsStillValid(employee, claimedIsAdmin, claimedPositionId))
    {
        await audit.WriteAsync(AuditActions.SessionEnded, AuditEntityTypes.Session, employeeId.ToString(),
            new { Reason = AuditReasons.IdentityInvalid }, employeeId: employeeId, activePositionId: claimedPositionId, anonymous: true);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    // Identity still holds — refresh permissions here too, same as OnValidatePrincipal, since a
    // grant or revocation is exactly the kind of change SessionGuard exists to catch quickly.
    var freshPermissionSnapshot = await BuildPermissionSnapshotAsync(db, employee!.Id, claimedPositionId, employee.IsAdmin);
    var claims = principal.Claims.Where(c => c.Type != AuthClaimNames.Permission && c.Type != LastValidatedClaimType).ToList();
    claims.AddRange(freshPermissionSnapshot.Select(code => new Claim(AuthClaimNames.Permission, code)));
    claims.Add(new Claim(LastValidatedClaimType, DateTime.UtcNow.ToString("O")));
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

    return Results.Redirect(IsSafeLocalReturnUrl(returnUrl) ? returnUrl! : "/");
});
// Download of a landing slide's attached file. Open to everyone while the slide is shown; for a slide that is not shown (expired,
// not started, switched off) only people who may see the slide list. Always sent as a download, never rendered by the browser.
app.MapGet("/landing/files/{slideId:guid}", async (Guid slideId, HttpContext http, IDbContextFactory<ApplicationDbContext> dbFactory) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var slide = await db.HomeSlides.AsNoTracking().SingleOrDefaultAsync(x => x.Id == slideId);
    var file = slide is null ? null : await db.HomeSlideAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.SlideId == slideId);
    if (slide is null || file is null)
        return Results.NotFound();
    var canSeeAll = http.User.Identity?.IsAuthenticated == true && http.User.HasClaim(AuthClaimNames.Permission, Perm.AdminLandingSlidesView);
    if (!slide.IsShownOn(DateTime.Now) && !canSeeAll)
        return Results.NotFound();
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(file.Content, file.ContentType, file.FileName);
});
app.MapRazorComponents<YasPortal.Web.Components.App>().AddInteractiveServerRenderMode();
app.Run();

static async Task ValidateAndRefreshPrincipalAsync(CookieValidatePrincipalContext context)
{
    var principal = context.Principal;
    if (principal is null)
    {
        context.RejectPrincipal();
        return;
    }

    if (DateTime.TryParse(principal.FindFirstValue(LastValidatedClaimType), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var lastValidatedUtc)
        && DateTime.UtcNow - lastValidatedUtc < TimeSpan.FromMinutes(2))
    {
        return; // Recently checked — skip the database round trip on this request.
    }

    if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return;
    }

    var dbFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync(context.HttpContext.RequestAborted);
    var employee = await db.Employees.AsNoTracking().Include(x => x.Positions)
        .SingleOrDefaultAsync(x => x.Id == employeeId, context.HttpContext.RequestAborted);

    var claimedIsAdmin = bool.TryParse(principal.FindFirstValue(AuthClaimNames.IsAdmin), out var isAdminValue) && isAdminValue;
    Guid? claimedPositionId = Guid.TryParse(principal.FindFirstValue(AuthClaimNames.ActivePositionId), out var positionValue) ? positionValue : null;

    // Deactivated employee, or their admin state changed, or (for a non-admin) the position they
    // are claiming is no longer their active one: none of these can be fixed by reissuing claims,
    // since the identity itself is no longer valid the way the cookie describes it.
    if (!IdentitySnapshotValidator.IsStillValid(employee, claimedIsAdmin, claimedPositionId))
    {
        await context.HttpContext.RequestServices.GetRequiredService<AuditEventWriter>().WriteAsync(
            AuditActions.SessionEnded, AuditEntityTypes.Session, employeeId.ToString(),
            new { Reason = AuditReasons.IdentityInvalid }, employeeId: employeeId, activePositionId: claimedPositionId, anonymous: true);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return;
    }

    // Identity still holds — but permissions may have been revoked or granted since sign-in.
    // Reissue the cookie with a fresh snapshot so that takes effect without forcing a re-login.
    var currentPermissionClaims = principal.FindAll(AuthClaimNames.Permission).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var freshPermissionSnapshot = await BuildPermissionSnapshotAsync(db, employee!.Id, claimedPositionId, employee.IsAdmin);

    var claims = principal.Claims
        .Where(c => c.Type != AuthClaimNames.Permission && c.Type != LastValidatedClaimType)
        .ToList();
    claims.AddRange(freshPermissionSnapshot.Select(code => new Claim(AuthClaimNames.Permission, code)));
    claims.Add(new Claim(LastValidatedClaimType, DateTime.UtcNow.ToString("O")));

    context.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    // ShouldRenew must be true here regardless of whether permissions actually changed: it is what
    // makes the cookie middleware write the replaced principal back to the browser's cookie. Without
    // it the refreshed LastValidatedClaimType never reaches the client, and every single request
    // would hit the database again instead of only once per RevalidationInterval.
    context.ShouldRenew = true;
}

static (Guid? EmployeeId, Guid? PositionId) ActorOf(ClaimsPrincipal user) =>
    (Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId) ? employeeId : (Guid?)null,
     Guid.TryParse(user.FindFirstValue(AuthClaimNames.ActivePositionId), out var positionId) ? positionId : (Guid?)null);

static async Task<HashSet<string>> BuildPermissionSnapshotAsync(
    ApplicationDbContext db,
    Guid employeeId,
    Guid? positionId,
    bool isAdmin,
    CancellationToken cancellationToken = default)
{
    List<string> permissions;

    if (isAdmin)
    {
        // Admin is not a special full-access bypass. Admin access is still exactly
        // the direct/group permissions assigned to the employee.
        permissions = await db.EmployeePermissions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.Permission.Code)
            .ToListAsync(cancellationToken);

        var grouped = await db.EmployeePermissionGroups
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .SelectMany(x => db.PermissionGroupPermissions
                .Where(gp => gp.GroupId == x.GroupId)
                .Select(gp => gp.Permission.Code))
            .ToListAsync(cancellationToken);

        permissions.AddRange(grouped);
    }
    else
    {
        if (positionId is null || positionId == Guid.Empty)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Normal users get permissions only from User + currently selected Position.
        permissions = await db.UserPositionPermissions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PositionId == positionId.Value)
            .Select(x => x.Permission.Code)
            .ToListAsync(cancellationToken);

        var grouped = await db.UserPositionPermissionGroups
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PositionId == positionId.Value)
            .SelectMany(x => db.PermissionGroupPermissions
                .Where(gp => gp.GroupId == x.GroupId)
                .Select(gp => gp.Permission.Code))
            .ToListAsync(cancellationToken);

        permissions.AddRange(grouped);
    }

    return permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

static bool IsSafeLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal))
        return false;
    if (returnUrl.StartsWith("//", StringComparison.Ordinal))
        return false;
    return Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) && !uri.IsAbsoluteUri;
}
