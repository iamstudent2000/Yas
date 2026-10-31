using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Authorization;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// Writes an explicit audit event (sign-in, export, blocked page, ...) as one <see cref="AuditEntry"/> row.
/// It uses its own short-lived context so it never saves someone else's pending changes, and it never
/// throws: a failure to write an event is logged and must not break the action being recorded.
/// </summary>
public sealed class AuditEventWriter(IDbContextFactory<ApplicationDbContext> dbFactory, AuditActorContext actorContext, ILogger<AuditEventWriter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <param name="details">Anything serializable to a JSON object (never a password); shown as the "after" side on the audit page.</param>
    /// <param name="employeeId">Who did it. When null, the signed-in user of the current request or circuit is used.</param>
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
}
