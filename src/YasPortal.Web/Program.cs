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
        "Organizations.View", "Organizations.Manage",
        "Positions.View", "Positions.Manage",
        "Permissions.View", "Permissions.Manage",
        "Admin.Users", "Admin.Positions", "Admin.Permissions", "Admin.Organizations"
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