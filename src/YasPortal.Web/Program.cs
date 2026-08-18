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

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "YasPortal.Auth";
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in new[]
    {
        "Dashboard.View", "Profile.View",
        "Requests.Create", "Requests.View", "Requests.Approve", "Requests.Reject",
        "Requests.ReturnToRequester", "Requests.ReturnToPreviousStep",
        "Employees.View", "Employees.Manage",
        "Organizations.View",
        "Positions.View",
        "Permissions.View",
        "Admin.Users", "Admin.Positions", "Admin.Permissions", "Admin.Organizations",
        "Admin.Access", "Admin.AssignmentHistory"
    })
    {
        options.AddPolicy(permission, policy => policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
builder.Services.AddScoped<AppState>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
builder.Services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<PermissionChecker>();
builder.Services.AddScoped<IPermissionChecker>(sp => sp.GetRequiredService<PermissionChecker>());
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();
builder.Services.AddScoped<AdminQueryService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Employee>>();

    await db.Database.EnsureCreatedAsync();

    // Ensure the assignment-history table also exists when an existing development
    // database was created before the history feature was added.
    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'dbo.PositionAssignmentHistories', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[PositionAssignmentHistories]
            (
                [Id] uniqueidentifier NOT NULL,
                [EmployeeId] uniqueidentifier NOT NULL,
                [PositionId] uniqueidentifier NOT NULL,
                [StartedAt] datetime2 NULL,
                [EndedAt] datetime2 NULL,
                CONSTRAINT [PK_PositionAssignmentHistories] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_PositionAssignmentHistories_Employees_EmployeeId]
                    FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_PositionAssignmentHistories_Positions_PositionId]
                    FOREIGN KEY ([PositionId]) REFERENCES [dbo].[Positions] ([Id]) ON DELETE NO ACTION
            );
            CREATE INDEX [IX_PositionAssignmentHistories_EmployeeId_StartedAt]
                ON [dbo].[PositionAssignmentHistories] ([EmployeeId], [StartedAt]);
            CREATE INDEX [IX_PositionAssignmentHistories_PositionId_StartedAt]
                ON [dbo].[PositionAssignmentHistories] ([PositionId], [StartedAt]);
            CREATE UNIQUE INDEX [IX_PositionAssignmentHistories_PositionId]
                ON [dbo].[PositionAssignmentHistories] ([PositionId])
                WHERE [EndedAt] IS NULL;
        END;
        """);

    // Preserve the assignments that already existed before history tracking was introduced.
    // Their original start date is unknown, so StartedAt intentionally remains NULL.
    await db.Database.ExecuteSqlRawAsync("""
        INSERT INTO [dbo].[PositionAssignmentHistories] ([Id], [EmployeeId], [PositionId], [StartedAt], [EndedAt])
        SELECT NEWID(), ep.[EmployeeId], ep.[PositionId], NULL, ep.[EndedAt]
        FROM [dbo].[EmployeePositions] ep
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM [dbo].[PositionAssignmentHistories] h
            WHERE h.[EmployeeId] = ep.[EmployeeId]
              AND h.[PositionId] = ep.[PositionId]
              AND ((h.[EndedAt] IS NULL AND ep.[EndedAt] IS NULL) OR (h.[EndedAt] = ep.[EndedAt]))
        );
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions')
              AND name = N'IX_EmployeePositions_PositionId'
        )
        BEGIN
            DROP INDEX [IX_EmployeePositions_PositionId] ON [dbo].[EmployeePositions];
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions')
              AND name = N'IX_EmployeePositions_PositionId'
        )
        BEGIN
            CREATE UNIQUE INDEX [IX_EmployeePositions_PositionId]
                ON [dbo].[EmployeePositions] ([PositionId])
                WHERE [EndedAt] IS NULL;
        END;
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

app.MapPost("/account/login", async (HttpContext http, ApplicationDbContext db, IPasswordHasher<Employee> passwordHasher, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return Results.Redirect("/login?error=1");

    var employee = await db.Employees.Include(x => x.Positions).SingleOrDefaultAsync(x => x.Username.ToLower() == username.ToLower() && x.IsActive);
    if (employee is null || string.IsNullOrWhiteSpace(employee.PasswordHash)) return Results.Redirect("/login?error=1");
    var passwordResult = passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
    if (passwordResult == PasswordVerificationResult.Failed) return Results.Redirect("/login?error=1");
    if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded) employee.SetPasswordHash(passwordHasher.HashPassword(employee, password));

    Guid? activePositionId = null;
    if (!employee.IsAdmin)
    {
        activePositionId = employee.Positions
            .Where(x => x.EndedAt == null && x.PositionId == employee.LastActivePositionId)
            .Select(x => (Guid?)x.PositionId)
            .FirstOrDefault();
        activePositionId ??= employee.Positions
            .Where(x => x.EndedAt == null)
            .Select(x => (Guid?)x.PositionId)
            .FirstOrDefault();

        if (activePositionId is null)
            return Results.Redirect("/login?error=no-position");
    }

    if (employee.LastActivePositionId != activePositionId)
        employee.SetLastActivePosition(activePositionId);
    await db.SaveChangesAsync();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
        new(ClaimTypes.Name, employee.Username),
        new(AuthClaimNames.IsAdmin, employee.IsAdmin.ToString())
    };
    if (activePositionId is Guid positionId)
        claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));

    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    return Results.Redirect("/");
});

app.MapPost("/account/position", async (HttpContext http, ApplicationDbContext db, IAntiforgery antiforgery) =>
{
    if (!(http.User.Identity?.IsAuthenticated ?? false)) return Results.Redirect("/login");
    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var employeeIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var positionIdValue = form["positionId"].ToString();
    var returnUrl = form["returnUrl"].ToString();
    if (!Guid.TryParse(employeeIdValue, out var employeeId) || !Guid.TryParse(positionIdValue, out var positionId)) return Results.Redirect("/my-positions");

    var validPosition = await db.EmployeePositions.AnyAsync(x => x.EmployeeId == employeeId && x.PositionId == positionId && x.EndedAt == null);
    if (!validPosition) return Results.Redirect("/my-positions");
    var employee = await db.Employees.SingleOrDefaultAsync(x => x.Id == employeeId && x.IsActive && !x.IsAdmin);
    if (employee is null) return Results.Redirect("/login");
    employee.SetLastActivePosition(positionId);
    await db.SaveChangesAsync();

    var claims = http.User.Claims.Where(c => c.Type != AuthClaimNames.ActivePositionId).ToList();
    claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

    if (IsSafeLocalReturnUrl(returnUrl))
        return Results.Redirect(returnUrl);
    return Results.Redirect("/my-positions");
});

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<YasPortal.Web.Components.App>().AddInteractiveServerRenderMode();
app.Run();

static bool IsSafeLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/', StringComparison.Ordinal))
        return false;

    // Reject protocol-relative URLs such as //evil.example, which start with '/'
    // but would be interpreted as an external host by a redirect response.
    if (returnUrl.StartsWith("//", StringComparison.Ordinal))
        return false;

    return Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) && !uri.IsAbsoluteUri;
}
