using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Persistence;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;

namespace YasPortal.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CaptureAssignmentHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await CaptureAssignmentHistoryAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void CaptureAssignmentHistory()
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();

        foreach (var entry in entries.Where(x => x.State == EntityState.Added))
        {
            if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }

        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue;
            var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;

            if (originalEndedAt is null && currentEndedAt is not null)
            {
                var activeHistory = PositionAssignmentHistories.Local
                    .FirstOrDefault(x => x.PositionId == entry.Entity.PositionId && x.EndedAt is null);

                if (activeHistory is not null)
                    activeHistory.End(currentEndedAt);
            }
            else if (originalEndedAt is not null && currentEndedAt is null)
            {
                if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                    PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
            }
        }
    }

    private async Task CaptureAssignmentHistoryAsync(CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();

        foreach (var entry in entries.Where(x => x.State == EntityState.Added))
        {
            if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }

        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue;
            var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;

            if (originalEndedAt is null && currentEndedAt is not null)
            {
                var activeHistory = PositionAssignmentHistories.Local
                    .FirstOrDefault(x => x.PositionId == entry.Entity.PositionId && x.EndedAt is null);

                activeHistory ??= await PositionAssignmentHistories
                    .FirstOrDefaultAsync(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null, cancellationToken);

                activeHistory?.End(currentEndedAt);
            }
            else if (originalEndedAt is not null && currentEndedAt is null)
            {
                if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                    PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(256).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.HasOne(x => x.Organization).WithMany(x => x.Employees).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Position>().WithMany().HasForeignKey(x => x.LastActivePositionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.ParentPosition).WithMany(x => x.Children).HasForeignKey(x => x.ParentPositionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmployeePosition>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.PositionId });
            e.HasOne(x => x.Employee).WithMany(x => x.Positions).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL");
        });

        modelBuilder.Entity<PositionAssignmentHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EmployeeId, x.StartedAt });
            e.HasIndex(x => new { x.PositionId, x.StartedAt });
            e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL");
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
        });

        modelBuilder.Entity<UserPositionPermission>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.PositionId, x.PermissionId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeePermission>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.PermissionId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PermissionGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<PermissionGroupPermission>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.PermissionId });
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPositionPermissionGroup>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.PositionId, x.GroupId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeePermissionGroup>(e =>
        {
            e.HasKey(x => new { x.EmployeeId, x.GroupId });
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
