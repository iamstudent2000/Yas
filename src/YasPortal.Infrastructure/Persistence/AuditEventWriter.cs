using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Authorization;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// Writes an explicit audit event (sign-in, export, search, blocked page, ...) as one <see cref="AuditEntry"/> row.
/// It uses its own short-lived context so it never saves someone else's pending changes, and it never
/// throws: a failure to write an event is logged and must not break the action being recorded.
/// </summary>
public sealed class AuditEventWriter(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    AuditActorContext actorContext,
    ILogger<AuditEventWriter> logger,
    AuthenticationStateProvider? authState = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        IncludeFields = true,   // some page filters are plain classes with public fields
    };

    /// <summary>
    /// The filters of the list whose export button was just pressed. The filter bar sets it, the export service
    /// writes it into the export event and clears it. One circuit has one of each, so there is no mix-up between users.
    /// </summary>
    public object? PendingExportFilters { get; set; }

    // Shared by every circuit and request: the prerender pass of a page and its live circuit are different scopes,
    // so a per-scope memory would still log a "seen" event twice.
    private static readonly Dictionary<string, DateTime> Recent = [];

    /// <summary>
    /// True when this person has not had <paramref name="key"/> recorded in the last <paramref name="window"/> (and marks it recorded now).
    /// Used for "seen" events, so re-rendering a list or re-opening a card within a few minutes is one row, not twenty.
    /// The person is <paramref name="employeeId"/>, or the signed-in one; with nobody signed in it returns false (nothing to record).
    /// </summary>
    public async Task<bool> ShouldLogAsync(string key, TimeSpan window, Guid? employeeId = null)
    {
        employeeId ??= (await ClaimsActorAsync())?.EmployeeId ?? actorContext.Actor?.EmployeeId;
        if (employeeId is null)
            return false;
        var fullKey = $"{employeeId}|{key}";
        var now = DateTime.UtcNow;
        lock (Recent)
        {
            if (Recent.TryGetValue(fullKey, out var last) && now - last < window)
                return false;
            Recent[fullKey] = now;
            if (Recent.Count > 2000)
                foreach (var stale in Recent.Where(x => now - x.Value > TimeSpan.FromHours(1)).Select(x => x.Key).ToList())
                    Recent.Remove(stale);
            return true;
        }
    }

    /// <param name="details">Anything serializable to a JSON object (never a password); shown as the "after" side on the audit page.</param>
    /// <param name="employeeId">Who did it. When null, the signed-in user of the current circuit or request is used.</param>
    /// <param name="anonymous">True for events with no known person (a failed sign-in for an unknown username).</param>
    public async Task WriteAsync(
        string action,
        string entityType,
        string? entityId,
        object? details = null,
        Guid? employeeId = null,
        Guid? activePositionId = null,
        bool anonymous = false,
        CancellationToken ct = default)
    {
        try
        {
            if (!anonymous)
            {
                if (employeeId is null && await ClaimsActorAsync() is { } claims)
                {
                    employeeId = claims.EmployeeId;
                    activePositionId ??= claims.PositionId;
                }

                var ambient = actorContext.Actor;
                employeeId ??= ambient?.EmployeeId;
                if (activePositionId is null && employeeId is not null && employeeId == ambient?.EmployeeId)
                    activePositionId = ambient?.ActivePositionId;
            }

            var json = details is null ? null : JsonSerializer.Serialize(details, JsonOptions);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditEntries.Add(new AuditEntry(employeeId, activePositionId, action, entityType, entityId, null, json, DateTime.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not write the audit event {Action} for {EntityType} {EntityId}.", action, entityType, entityId);
        }
    }

    /// <summary>A list search: which page, and every filter that was set (empty ones are left out).</summary>
    public Task WriteSearchAsync(string path, object? filters, CancellationToken ct = default) =>
        WriteAsync(AuditActions.Searched, AuditEntityTypes.Report, path, Describe(("Path", path), filters), ct: ct);

    /// <summary>
    /// The filters as a flat name → value map, so each one shows on its own line on the audit page.
    /// Unset values (null, blank text, false, empty lists) and computed counters are dropped.
    /// </summary>
    public static Dictionary<string, object?> Flatten(object? filters)
    {
        var result = new Dictionary<string, object?>();
        if (filters is null || JsonSerializer.SerializeToNode(filters, JsonOptions) is not JsonObject node)
            return result;
        foreach (var (key, value) in node)
        {
            if (value is null || key is "Count" or "IsEmpty")
                continue;
            if (value is JsonValue scalar)
            {
                if (scalar.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text))
                    continue;
                if (scalar.TryGetValue<bool>(out var flag) && !flag)
                    continue;
            }
            if (value is JsonArray { Count: 0 })
                continue;
            result[key] = value.DeepClone();
        }
        return result;
    }

    /// <summary>A details map made of a few fixed values followed by the flattened filters.</summary>
    public static Dictionary<string, object?> Describe((string Key, object? Value) first, object? filters, params (string Key, object? Value)[] more)
    {
        var map = new Dictionary<string, object?> { [first.Key] = first.Value };
        foreach (var (key, value) in more)
            map[key] = value;
        foreach (var (key, value) in Flatten(filters))
            map.TryAdd(key, value);
        return map;
    }

    private async Task<(Guid? EmployeeId, Guid? PositionId)?> ClaimsActorAsync()
    {
        if (authState is null)
            return null;
        try
        {
            var user = (await authState.GetAuthenticationStateAsync()).User;
            if (user.Identity?.IsAuthenticated != true || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var employeeId))
                return null;
            Guid? positionId = Guid.TryParse(user.FindFirstValue(AuthClaimNames.ActivePositionId), out var position) ? position : null;
            return (employeeId, positionId);
        }
        catch (Exception)
        {
            return null;   // no circuit or request to read the user from
        }
    }
}
