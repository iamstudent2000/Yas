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
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Development;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Claim type used to throttle how often OnValidatePrincipal / SessionGuard actually hit the
// database for a given cookie, rather than on every request. Declared here, at the top, because
// it is referenced both by the /account/reauth-check endpoint below and by OnValidatePrincipal
// further down, and a local const in top-level statements must be declared before any lambda or
// local function that closes over it, regardless of when that lambda actually runs.
const string LastValidatedClaimType = "yas_last_validated_utc";

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
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
    foreach (var permission in new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Requests.ReturnToPreviousStep", "Requests.ForceClose", "Employees.View", "Employees.Manage", "Organizations.View", "Positions.View", "Permissions.View", "Admin.Users", "Admin.Positions", "Admin.Permissions", "Admin.Organizations", "Admin.Access", "Admin.AssignmentHistory", "Admin.AuditLog", "Admin.Workflows" })
        options.AddPolicy(permission, policy => policy.Requirements.Add(new PermissionRequirement(permission)));
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddScoped<AppState>();
builder.Services.AddSingleton<AuditActorContext>();

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

app.MapPost("/account/login", async (HttpContext http, ApplicationDbContext db, IPasswordHasher<Employee> passwordHasher, IAntiforgery antiforgery) => {
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        return Results.Redirect("/login?error=1");
    var employee = await db.Employees.Include(x => x.Positions).SingleOrDefaultAsync(x => x.Username.ToLower() == username.ToLower() && x.IsActive);
    if (employee is null || string.IsNullOrWhiteSpace(employee.PasswordHash))
        return Results.Redirect("/login?error=1");
    var passwordResult = passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
    if (passwordResult == PasswordVerificationResult.Failed)
        return Results.Redirect("/login?error=1");
    if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
        employee.SetPasswordHash(passwordHasher.HashPassword(employee, password));

    Guid? activePositionId = null;
    if (!employee.IsAdmin)
    {
        activePositionId = employee.Positions.Where(x => x.EndedAt == null && x.PositionId == employee.LastActivePositionId).Select(x => (Guid?)x.PositionId).FirstOrDefault();
        activePositionId ??= employee.Positions.Where(x => x.EndedAt == null).Select(x => (Guid?)x.PositionId).FirstOrDefault();
        if (activePositionId is null)
            return Results.Redirect("/login?error=no-position");
    }

    if (employee.LastActivePositionId != activePositionId)
        employee.SetLastActivePosition(activePositionId);
    await db.SaveChangesAsync();

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

app.MapPost("/account/position", async (HttpContext http, ApplicationDbContext db, IAntiforgery antiforgery) => {
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

    employee.SetLastActivePosition(positionId);
    await db.SaveChangesAsync();

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

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery) => { await antiforgery.ValidateRequestAsync(http); await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.Redirect("/login"); });

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
app.MapGet("/account/reauth-check", async (HttpContext http, IDbContextFactory<ApplicationDbContext> dbFactory, string? returnUrl) =>
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
