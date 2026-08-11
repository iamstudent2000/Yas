using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddInfrastructure();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/account/login", async (HttpContext http, ApplicationDbContext db) =>
{
    if (!http.Request.HasFormContentType)
        return Results.Redirect("/login?error=1");

    var form = await http.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();

    var employee = await db.Employees
        .Include(x => x.EmployeePositions)
        .SingleOrDefaultAsync(x => x.Username.ToLower() == username.ToLower() && x.IsActive);

    if (employee is null || !PasswordHasher.Verify(password, employee.PasswordHash))
        return Results.Redirect("/login?error=1");

    var activePositionId = employee.EmployeePositions
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
}).Add(endpointBuilder => endpointBuilder.Metadata.Add(new RequireAntiforgeryTokenAttribute()));

app.MapPost("/account/position", async (HttpContext http, ApplicationDbContext db, IAntiforgery antiforgery) =>
{
    if (!(http.User.Identity?.IsAuthenticated ?? false))
        return Results.Redirect("/login");

    // Validate the token explicitly before reading the form. Reading Request.Form
    // first is rejected by ASP.NET Core's antiforgery FormFeature.
    await antiforgery.ValidateRequestAsync(http);

    if (!http.Request.HasFormContentType)
        return Results.Redirect("/my-positions");

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

internal static class PasswordHasher
{
    public static bool Verify(string password, string hash)
        => !string.IsNullOrWhiteSpace(hash) && BCrypt.Net.BCrypt.Verify(password, hash);
}
