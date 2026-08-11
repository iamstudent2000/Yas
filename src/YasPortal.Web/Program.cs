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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "YasPortal.Auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin.Users", policy => policy.Requirements.Add(new PermissionRequirement("Admin.Users")));
    options.AddPolicy("Admin.Positions", policy => policy.Requirements.Add(new PermissionRequirement("Admin.Positions")));
    options.AddPolicy("Admin.Permissions", policy => policy.Requirements.Add(new PermissionRequirement("Admin.Permissions")));
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();
builder.Services.AddScoped<IPasswordHasher<Employee>, PasswordHasher<Employee>>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Employee>>();
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

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        return Results.Redirect("/login?error=1");

    var employee = await db.Employees
        .Include(x => x.Positions)
        .SingleOrDefaultAsync(x => x.Username.ToLower() == username.ToLower() && x.IsActive);

    if (employee is null || string.IsNullOrWhiteSpace(employee.PasswordHash))
        return Results.Redirect("/login?error=1");

    var passwordResult = passwordHasher.VerifyHashedPassword(employee, employee.PasswordHash, password);
    if (passwordResult == PasswordVerificationResult.Failed)
        return Results.Redirect("/login?error=1");

    if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
    {
        employee.SetPasswordHash(passwordHasher.HashPassword(employee, password));
        await db.SaveChangesAsync();
    }

    var activePositionId = employee.Positions
        .Where(x => x.EndedAt == null)
        .Select(x => (Guid?)x.PositionId)
        .FirstOrDefault();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
        new(ClaimTypes.Name, employee.Username),
        new(AuthClaimNames.IsAdmin, employee.IsAdmin.ToString())
    };

    if (activePositionId is Guid positionId)
        claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    return Results.Redirect("/");
});

app.MapPost("/account/position", async (HttpContext http, ApplicationDbContext db, IAntiforgery antiforgery) =>
{
    if (!(http.User.Identity?.IsAuthenticated ?? false))
        return Results.Redirect("/login");

    await antiforgery.ValidateRequestAsync(http);
    var form = await http.Request.ReadFormAsync();
    var employeeIdValue = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
    var positionIdValue = form["positionId"].ToString();

    if (!Guid.TryParse(employeeIdValue, out var employeeId) || !Guid.TryParse(positionIdValue, out var positionId))
        return Results.Redirect("/my-positions");

    var validPosition = await db.EmployeePositions
        .AnyAsync(x => x.EmployeeId == employeeId && x.PositionId == positionId && x.EndedAt == null);

    if (!validPosition)
        return Results.Redirect("/my-positions");

    var claims = http.User.Claims
        .Where(c => c.Type != AuthClaimNames.ActivePositionId)
        .ToList();
    claims.Add(new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()));

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    return Results.Redirect("/my-positions");
});

app.MapPost("/account/logout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<YasPortal.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
