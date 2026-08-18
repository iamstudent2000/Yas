using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using YasPortal.Application.Persistence;
using YasPortal.Domain.Auditing;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;

namespace YasPortal.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, AuditActorContext auditActorContext) : DbContext(options), IApplicationDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<EmployeePosition> EmployeePositions => Set<EmployeePosition>();
    public DbSet<PositionAssignmentHistory> PositionAssignmentHistories => Set<PositionAssignmentHistory>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPositionPermission> UserPositionPermissions => Set<UserPositionPermission>();
    public DbSet<EmployeePermission> EmployeePermissions => Set<EmployeePermission>();
    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();
    public DbSet<PermissionGroupPermission> PermissionGroupPermissions => Set<PermissionGroupPermission>();
    public DbSet<UserPositionPermissionGroup> UserPositionPermissionGroups => Set<UserPositionPermissionGroup>();
    public DbSet<EmployeePermissionGroup> EmployeePermissionGroups => Set<EmployeePermissionGroup>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges(); ValidateBusinessInvariants(); CaptureAssignmentHistory(); CaptureAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges(); await ValidateBusinessInvariantsAsync(cancellationToken); await CaptureAssignmentHistoryAsync(cancellationToken); CaptureAuditEntries();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void CaptureAuditEntries()
    {
        var entries = ChangeTracker.Entries().Where(x => x.Entity is not AuditEntry && (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).ToList();
        foreach (var entry in entries)
        {
            var action = entry.State switch { EntityState.Added => "Created", EntityState.Modified => "Updated", EntityState.Deleted => "Deleted", _ => entry.State.ToString() };
            AuditEntries.Add(new AuditEntry(
                auditActorContext.Actor?.EmployeeId,
                auditActorContext.Actor?.ActivePositionId,
                action,
                entry.Metadata.ClrType.Name,
                GetEntityId(entry),
                entry.State is EntityState.Modified or EntityState.Deleted ? SerializeValues(entry, true) : null,
                entry.State is EntityState.Added or EntityState.Modified ? SerializeValues(entry, false) : null,
                DateTime.UtcNow));
        }
    }

    private static string? GetEntityId(EntityEntry entry)
    {
        var keys = entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).ToList();
        return keys.Count == 0 ? null : string.Join(",", keys.Select(p => $"{p.Metadata.Name}={p.CurrentValue ?? p.OriginalValue}"));
    }

    private static string? SerializeValues(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() || property.Metadata.Name.Equals("PasswordHash", StringComparison.OrdinalIgnoreCase)) continue;
            values[property.Metadata.Name] = original ? property.OriginalValue : property.CurrentValue;
        }
        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private void ValidateBusinessInvariants() { ValidateEmployeeAssignments(); ValidatePositionHierarchy(); }
    private async Task ValidateBusinessInvariantsAsync(CancellationToken cancellationToken) { await ValidateEmployeeAssignmentsAsync(cancellationToken); await ValidatePositionHierarchyAsync(cancellationToken); }

    private void ValidateEmployeeAssignments()
    {
        var endingByPosition = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is not null).GroupBy(x => x.Entity.PositionId).ToDictionary(x => x.Key, x => x.Select(e => e.Entity.EmployeeId).ToHashSet());
        var activeEntries = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Added || (x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is null)).ToList();
        foreach (var entry in activeEntries)
        {
            ValidateActiveEmployee(entry.Entity.EmployeeId);
            if (activeEntries.Any(other => !ReferenceEquals(other, entry) && other.Entity.PositionId == entry.Entity.PositionId && other.Entity.EmployeeId != entry.Entity.EmployeeId && !IsBeingEnded(other))) throw new InvalidOperationException("A position can have only one active employee.");
            var endingEmployeeIds = endingByPosition.TryGetValue(entry.Entity.PositionId, out var ids) ? ids : new HashSet<Guid>();
            if (EmployeePositions.AsNoTracking().Any(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null && x.EmployeeId != entry.Entity.EmployeeId && !endingEmployeeIds.Contains(x.EmployeeId))) throw new InvalidOperationException("A position can have only one active employee.");
        }
    }

    private async Task ValidateEmployeeAssignmentsAsync(CancellationToken cancellationToken)
    {
        var endingByPosition = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is not null).GroupBy(x => x.Entity.PositionId).ToDictionary(x => x.Key, x => x.Select(e => e.Entity.EmployeeId).ToHashSet());
        var activeEntries = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Added || (x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is null)).ToList();
        foreach (var entry in activeEntries)
        {
            await ValidateActiveEmployeeAsync(entry.Entity.EmployeeId, cancellationToken);
            if (activeEntries.Any(other => !ReferenceEquals(other, entry) && other.Entity.PositionId == entry.Entity.PositionId && other.Entity.EmployeeId != entry.Entity.EmployeeId && !IsBeingEnded(other))) throw new InvalidOperationException("A position can have only one active employee.");
            var endingEmployeeIds = endingByPosition.TryGetValue(entry.Entity.PositionId, out var ids) ? ids : new HashSet<Guid>();
            if (await EmployeePositions.AsNoTracking().AnyAsync(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null && x.EmployeeId != entry.Entity.EmployeeId && !endingEmployeeIds.Contains(x.EmployeeId), cancellationToken)) throw new InvalidOperationException("A position can have only one active employee.");
        }
    }

    private void ValidateActiveEmployee(Guid employeeId)
    {
        var localEmployee = ChangeTracker.Entries<Employee>().FirstOrDefault(x => x.Entity.Id == employeeId)?.Entity;
        if (localEmployee is not null) { if (!localEmployee.IsActive) throw new InvalidOperationException("An inactive employee cannot have an active position assignment."); return; }
        if (!Employees.AsNoTracking().Any(x => x.Id == employeeId && x.IsActive)) throw new InvalidOperationException("An inactive or unknown employee cannot have an active position assignment.");
    }

    private async Task ValidateActiveEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var localEmployee = ChangeTracker.Entries<Employee>().FirstOrDefault(x => x.Entity.Id == employeeId)?.Entity;
        if (localEmployee is not null) { if (!localEmployee.IsActive) throw new InvalidOperationException("An inactive employee cannot have an active position assignment."); return; }
        if (!await Employees.AsNoTracking().AnyAsync(x => x.Id == employeeId && x.IsActive, cancellationToken)) throw new InvalidOperationException("An inactive or unknown employee cannot have an active position assignment.");
    }

    private static bool IsBeingEnded(EntityEntry<EmployeePosition> entry) => entry.State == EntityState.Modified && entry.Property(x => x.EndedAt).IsModified && entry.Property(x => x.EndedAt).CurrentValue is not null;

    private void ValidatePositionHierarchy()
    {
        if (!ChangeTracker.Entries<Position>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)) return;
        var parents = Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionary(x => x.Id, x => x.ParentPositionId); ApplyTrackedPositionChanges(parents); ValidateHierarchyGraph(parents);
    }

    private async Task ValidatePositionHierarchyAsync(CancellationToken cancellationToken)
    {
        if (!ChangeTracker.Entries<Position>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)) return;
        var parents = await Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionaryAsync(x => x.Id, x => x.ParentPositionId, cancellationToken); ApplyTrackedPositionChanges(parents); ValidateHierarchyGraph(parents);
    }

    private void ApplyTrackedPositionChanges(Dictionary<Guid, Guid?> parents)
    {
        foreach (var entry in ChangeTracker.Entries<Position>()) { if (entry.State == EntityState.Deleted) { parents.Remove(entry.Entity.Id); continue; } parents[entry.Entity.Id] = entry.Entity.ParentPositionId; }
    }

    private static void ValidateHierarchyGraph(IReadOnlyDictionary<Guid, Guid?> parents)
    {
        foreach (var start in parents.Keys) { var visited = new HashSet<Guid>(); var current = (Guid?)start; while (current is Guid positionId) { if (!visited.Add(positionId)) throw new InvalidOperationException("The position hierarchy contains a cycle."); if (!parents.TryGetValue(positionId, out current)) break; } }
    }

    private void CaptureAssignmentHistory()
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();
        foreach (var entry in entries.Where(x => x.State == EntityState.Added)) if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null)) PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue; var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;
            if (originalEndedAt is null && currentEndedAt is not null) { var activeHistory = PositionAssignmentHistories.Local.FirstOrDefault(x => x.PositionId == entry.Entity.PositionId && x.EndedAt is null) ?? PositionAssignmentHistories.FirstOrDefault(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null); activeHistory?.End(currentEndedAt.Value); }
            else if (originalEndedAt is not null && currentEndedAt is null && !PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null)) PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }
    }

    private async Task CaptureAssignmentHistoryAsync(CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();
        foreach (var entry in entries.Where(x => x.State == EntityState.Added)) if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null)) PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue; var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;
            if (originalEndedAt is null && currentEndedAt is not null) { var activeHistory = PositionAssignmentHistories.Local.FirstOrDefault(x => x.PositionId == entry.Entity.PositionId && x.EndedAt is null) ?? await PositionAssignmentHistories.FirstOrDefaultAsync(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null, cancellationToken); activeHistory?.End(currentEndedAt.Value); }
            else if (originalEndedAt is not null && currentEndedAt is null && !PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null)) PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(e => { e.HasKey(x => x.Id); e.Property(x => x.Action).HasMaxLength(100).IsRequired(); e.Property(x => x.EntityType).HasMaxLength(200).IsRequired(); e.Property(x => x.EntityId).HasMaxLength(500); e.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)"); e.Property(x => x.AfterJson).HasColumnType("nvarchar(max)"); e.HasIndex(x => x.OccurredAtUtc); e.HasIndex(x => new { x.EntityType, x.EntityId }); e.HasIndex(x => x.EmployeeId); });
        modelBuilder.Entity<Organization>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); });
        modelBuilder.Entity<Employee>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Username).IsUnique(); e.Property(x => x.Username).HasMaxLength(256).IsRequired(); e.Property(x => x.FullName).HasMaxLength(256).IsRequired(); e.Property(x => x.PasswordHash).HasMaxLength(512); e.HasOne(x => x.Organization).WithMany(x => x.Employees).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Position>().WithMany().HasForeignKey(x => x.LastActivePositionId).OnDelete(DeleteBehavior.SetNull); });
        modelBuilder.Entity<Position>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.HasOne(x => x.ParentPosition).WithMany(x => x.Children).HasForeignKey(x => x.ParentPositionId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<EmployeePosition>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId }); e.HasOne(x => x.Employee).WithMany(x => x.Positions).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL"); });
        modelBuilder.Entity<PositionAssignmentHistory>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.StartedAt }); e.HasIndex(x => new { x.PositionId, x.StartedAt }); e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL"); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<Permission>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(200).IsRequired(); e.Property(x => x.Name).HasMaxLength(300).IsRequired(); });
        modelBuilder.Entity<UserPositionPermission>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId, x.PermissionId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<EmployeePermission>(e => { e.HasKey(x => new { x.EmployeeId, x.PermissionId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<PermissionGroup>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.Description).HasMaxLength(500); });
        modelBuilder.Entity<PermissionGroupPermission>(e => { e.HasKey(x => new { x.GroupId, x.PermissionId }); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<UserPositionPermissionGroup>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId, x.GroupId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<EmployeePermissionGroup>(e => { e.HasKey(x => new { x.EmployeeId, x.GroupId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
    }
}
