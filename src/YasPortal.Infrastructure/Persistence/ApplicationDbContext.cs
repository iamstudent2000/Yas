using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using YasPortal.Application.Persistence;
using YasPortal.Domain.Auditing;
using YasPortal.Domain.Gallery;
using YasPortal.Domain.Guides;
using YasPortal.Domain.Store;
using YasPortal.Domain.Landing;
using YasPortal.Domain.PhoneBook;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Delegations;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Preferences;
using YasPortal.Domain.Security;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Authorization;

namespace YasPortal.Infrastructure.Persistence;

// The application's connection to the database. Each property below is one table, and OnModelCreating describes
// the columns, keys, links between tables and indexes. Saving changes also runs business checks and writes the
// audit trail automatically, so nobody can forget to record a change.
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, AuditActorContext? auditActorContext = null) : DbContext(options), IApplicationDbContext
{
    private readonly AuditActorContext _auditActorContext = auditActorContext ?? new AuditActorContext();
    // Organization structure: organizations, employees, positions and who holds which position (now and in the past).
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<EmployeePosition> EmployeePositions => Set<EmployeePosition>();
    public DbSet<PositionAssignmentHistory> PositionAssignmentHistories => Set<PositionAssignmentHistory>();
    // Permissions: the catalog, grants to people for a position, direct grants to administrators, and permission groups.
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPositionPermission> UserPositionPermissions => Set<UserPositionPermission>();
    public DbSet<EmployeePermission> EmployeePermissions => Set<EmployeePermission>();
    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();
    public DbSet<PermissionGroupPermission> PermissionGroupPermissions => Set<PermissionGroupPermission>();
    public DbSet<UserPositionPermissionGroup> UserPositionPermissionGroups => Set<UserPositionPermissionGroup>();
    public DbSet<EmployeePermissionGroup> EmployeePermissionGroups => Set<EmployeePermissionGroup>();
    // Audit trail and archive settings.
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AuditArchiveSetting> AuditArchiveSettings => Set<AuditArchiveSetting>();
    // Home page: slides with optional files, and the link groups and links.
    public DbSet<HomeSlide> HomeSlides => Set<HomeSlide>();
    public DbSet<HomeSlideAttachment> HomeSlideAttachments => Set<HomeSlideAttachment>();
    public DbSet<LinkGroup> LinkGroups => Set<LinkGroup>();
    public DbSet<PortalLink> PortalLinks => Set<PortalLink>();
    // Phone book.
    public DbSet<ContactGroup> ContactGroups => Set<ContactGroup>();
    public DbSet<Contact> Contacts => Set<Contact>();
    // User guides.
    public DbSet<UserGuide> UserGuides => Set<UserGuide>();
    public DbSet<PortalLinkFile> PortalLinkFiles => Set<PortalLinkFile>();
    public DbSet<HomeTile> HomeTiles => Set<HomeTile>();
    public DbSet<UserGuideAttachment> UserGuideAttachments => Set<UserGuideAttachment>();
    public DbSet<UserGuideGroup> UserGuideGroups => Set<UserGuideGroup>();
    // Image gallery: the picture details and, in another table, the picture bytes.
    public DbSet<GalleryGroup> GalleryGroups => Set<GalleryGroup>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<GalleryImageFile> GalleryImageFiles => Set<GalleryImageFile>();
    // Internal store: groups, items, stock ledger, orders and lottery applications.
    public DbSet<StoreCategory> StoreCategories => Set<StoreCategory>();
    public DbSet<StoreItem> StoreItems => Set<StoreItem>();
    public DbSet<StoreStockMovement> StoreStockMovements => Set<StoreStockMovement>();
    public DbSet<StoreOrder> StoreOrders => Set<StoreOrder>();
    public DbSet<StoreOrderLine> StoreOrderLines => Set<StoreOrderLine>();
    public DbSet<StoreLotteryEntry> StoreLotteryEntries => Set<StoreLotteryEntry>();
    // Workflow: approval step definitions, requests and their steps, requester actions, decisions and field history.
    public DbSet<WorkflowStepDefinition> WorkflowStepDefinitions => Set<WorkflowStepDefinition>();
    public DbSet<WorkflowRequest> WorkflowRequests => Set<WorkflowRequest>();
    public DbSet<WorkflowRequestAttachment> WorkflowRequestAttachments => Set<WorkflowRequestAttachment>();
    public DbSet<WorkflowRequestStep> WorkflowRequestSteps => Set<WorkflowRequestStep>();
    public DbSet<WorkflowRequesterAction> WorkflowRequesterActions => Set<WorkflowRequesterAction>();
    public DbSet<WorkflowStepDecision> WorkflowStepDecisions => Set<WorkflowStepDecision>();
    public DbSet<WorkflowFieldChange> WorkflowFieldChanges => Set<WorkflowFieldChange>();
    // Notifications and reusable recipient lists.
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<RecipientGroup> RecipientGroups => Set<RecipientGroup>();
    public DbSet<RecipientGroupMember> RecipientGroupMembers => Set<RecipientGroupMember>();
    // Personal appearance and reminder settings, and the password policy.
    public DbSet<UserUiPreference> UiPreferences => Set<UserUiPreference>();
    public DbSet<WaitingReminderSetting> WaitingReminderSettings => Set<WaitingReminderSetting>();
    public DbSet<PasswordPolicy> PasswordPolicies => Set<PasswordPolicy>();
    // Delegations of a position's authority to another position.
    public DbSet<PositionDelegation> PositionDelegations => Set<PositionDelegation>();

    // Every save first checks the business rules, records position history, and writes audit rows.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ChangeTracker.DetectChanges();
        ValidateBusinessInvariants();
        CaptureAssignmentHistory();
        CaptureAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    // The same steps for code that saves asynchronously.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        await ValidateBusinessInvariantsAsync(cancellationToken);
        await CaptureAssignmentHistoryAsync(cancellationToken);
        CaptureAuditEntries();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Writes one audit row for every created, updated or deleted record, with the values before and after.
    // The audit rows themselves, notifications, recipient lists and appearance settings are not audited.
    private void CaptureAuditEntries()
    {
        // Pick all changed records except the excluded kinds.
        var entries = ChangeTracker.Entries().Where(x => x.Entity is not AuditEntry && x.Entity is not Notification && x.Entity is not RecipientGroup && x.Entity is not RecipientGroupMember && x.Entity is not UserUiPreference && (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).ToList();
        foreach (var entry in entries)
        {
            var action = entry.State switch {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => entry.State.ToString()
            };
            // The row says who did it, which position, what happened, which kind of record and which id, plus before and after values.
            AuditEntries.Add(new AuditEntry(_auditActorContext.Actor?.EmployeeId, _auditActorContext.Actor?.ActivePositionId, action, entry.Metadata.ClrType.Name, GetEntityId(entry), entry.State is EntityState.Modified or EntityState.Deleted ? SerializeValues(entry, true) : null, entry.State is EntityState.Added or EntityState.Modified ? SerializeValues(entry, false) : null, DateTime.UtcNow));
        }
    }

    // Text identifying the record (its key values) for the audit row.
    private static string? GetEntityId(EntityEntry entry)
    {
        var keys = entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).ToList();
        return keys.Count == 0 ? null : string.Join(",", keys.Select(p => $"{p.Metadata.Name}={p.CurrentValue ?? p.OriginalValue}"));
    }
    // Turns the record's values into JSON text. Password hashes and file contents are never included.
    private static string? SerializeValues(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() || property.Metadata.Name.Equals("PasswordHash", StringComparison.OrdinalIgnoreCase) || property.Metadata.ClrType == typeof(byte[]))   // file contents never go into the audit log
                continue;
            values[property.Metadata.Name] = original ? property.OriginalValue : property.CurrentValue;
        }
        return values.Count == 0 ? null : JsonSerializer.Serialize(values, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // Business rules checked before saving: assignments and the position tree.
    private void ValidateBusinessInvariants()
    {
        ValidateEmployeeAssignments();
        ValidatePositionHierarchy();
    }
    // Same checks, for asynchronous saving.
    private async Task ValidateBusinessInvariantsAsync(CancellationToken cancellationToken)
    {
        await ValidateEmployeeAssignmentsAsync(cancellationToken);
        await ValidatePositionHierarchyAsync(cancellationToken);
    }
    // Rule: a position can have only one current occupant, and only active employees can be assigned.
    private void ValidateEmployeeAssignments()
    {
        var endingByPosition = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is not null).GroupBy(x => x.Entity.PositionId).ToDictionary(x => x.Key, x => x.Select(e => e.Entity.EmployeeId).ToHashSet());
        var activeEntries = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Added || (x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is null)).ToList();
        foreach (var entry in activeEntries)
        {
            ValidateActiveEmployee(entry.Entity.EmployeeId);
            if (activeEntries.Any(other => !ReferenceEquals(other, entry) && other.Entity.PositionId == entry.Entity.PositionId && other.Entity.EmployeeId != entry.Entity.EmployeeId && !IsBeingEnded(other)))
                throw new InvalidOperationException("A position can have only one active employee.");
            var endingEmployeeIds = endingByPosition.TryGetValue(entry.Entity.PositionId, out var ids) ? ids : new HashSet<Guid>();
            if (EmployeePositions.AsNoTracking().Any(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null && x.EmployeeId != entry.Entity.EmployeeId && !endingEmployeeIds.Contains(x.EmployeeId)))
                throw new InvalidOperationException("A position can have only one active employee.");
        }
    }
    // Same rule check, for asynchronous saving.
    private async Task ValidateEmployeeAssignmentsAsync(CancellationToken cancellationToken)
    {
        var endingByPosition = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is not null).GroupBy(x => x.Entity.PositionId).ToDictionary(x => x.Key, x => x.Select(e => e.Entity.EmployeeId).ToHashSet());
        var activeEntries = ChangeTracker.Entries<EmployeePosition>().Where(x => x.State == EntityState.Added || (x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified && x.Property(p => p.EndedAt).CurrentValue is null)).ToList();
        foreach (var entry in activeEntries)
        {
            await ValidateActiveEmployeeAsync(entry.Entity.EmployeeId, cancellationToken);
            if (activeEntries.Any(other => !ReferenceEquals(other, entry) && other.Entity.PositionId == entry.Entity.PositionId && other.Entity.EmployeeId != entry.Entity.EmployeeId && !IsBeingEnded(other)))
                throw new InvalidOperationException("A position can have only one active employee.");
            var endingEmployeeIds = endingByPosition.TryGetValue(entry.Entity.PositionId, out var ids) ? ids : new HashSet<Guid>();
            if (await EmployeePositions.AsNoTracking().AnyAsync(x => x.PositionId == entry.Entity.PositionId && x.EndedAt == null && x.EmployeeId != entry.Entity.EmployeeId && !endingEmployeeIds.Contains(x.EmployeeId), cancellationToken))
                throw new InvalidOperationException("A position can have only one active employee.");
        }
    }
    // Rule: an inactive or unknown employee cannot hold a position.
    private void ValidateActiveEmployee(Guid employeeId)
    {
        var localEmployee = ChangeTracker.Entries<Employee>().FirstOrDefault(x => x.Entity.Id == employeeId)?.Entity;
        if (localEmployee is not null)
        {
            if (!localEmployee.IsActive)
                throw new InvalidOperationException("An inactive employee cannot have an active position assignment.");
            return;
        }
        if (!Employees.AsNoTracking().Any(x => x.Id == employeeId && x.IsActive))
            throw new InvalidOperationException("An inactive or unknown employee cannot have an active position assignment.");
    }
    // Same rule check, for asynchronous saving.
    private async Task ValidateActiveEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var localEmployee = ChangeTracker.Entries<Employee>().FirstOrDefault(x => x.Entity.Id == employeeId)?.Entity;
        if (localEmployee is not null)
        {
            if (!localEmployee.IsActive)
                throw new InvalidOperationException("An inactive employee cannot have an active position assignment.");
            return;
        }
        if (!await Employees.AsNoTracking().AnyAsync(x => x.Id == employeeId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("An inactive or unknown employee cannot have an active position assignment.");
    }
    // True when this assignment is being given an end date in this save.
    private static bool IsBeingEnded(EntityEntry<EmployeePosition> entry) => entry.State == EntityState.Modified && entry.Property(x => x.EndedAt).IsModified && entry.Property(x => x.EndedAt).CurrentValue is not null;
    // Rule: the position tree must not loop; checked only when positions changed.
    private void ValidatePositionHierarchy()
    {
        if (!ChangeTracker.Entries<Position>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return;
        var parents = Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionary(x => x.Id, x => x.ParentPositionId);
        ApplyTrackedPositionChanges(parents);
        ValidateHierarchyGraph(parents);
    }
    // Same rule check, for asynchronous saving.
    private async Task ValidatePositionHierarchyAsync(CancellationToken cancellationToken)
    {
        if (!ChangeTracker.Entries<Position>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return;
        var parents = await Positions.AsNoTracking().Select(x => new { x.Id, x.ParentPositionId }).ToDictionaryAsync(x => x.Id, x => x.ParentPositionId, cancellationToken);
        ApplyTrackedPositionChanges(parents);
        ValidateHierarchyGraph(parents);
    }
    // Apply the pending changes to the parent list so the check sees the tree as it will be after saving.
    private void ApplyTrackedPositionChanges(Dictionary<Guid, Guid?> parents)
    {
        foreach (var entry in ChangeTracker.Entries<Position>())
        {
            if (entry.State == EntityState.Deleted)
            {
                parents.Remove(entry.Entity.Id);
                continue;
            }
            parents[entry.Entity.Id] = entry.Entity.ParentPositionId;
        }
    }
    // Follow each position up through its parents; meeting a position twice means a loop, which is refused.
    private static void ValidateHierarchyGraph(IReadOnlyDictionary<Guid, Guid?> parents)
    {
        foreach (var start in parents.Keys)
        {
            var visited = new HashSet<Guid>();
            var current = (Guid?)start;
            while (current is Guid positionId)
            {
                if (!visited.Add(positionId))
                    throw new InvalidOperationException("The position hierarchy contains a cycle.");
                if (!parents.TryGetValue(positionId, out current))
                    break;
            }
        }
    }
    // Keeps the position assignment history: a new assignment starts a history row; ending an assignment closes it;
    // reactivating an assignment opens a new history row.
    private void CaptureAssignmentHistory()
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();
        foreach (var entry in entries.Where(x => x.State == EntityState.Added))
        if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
            PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue;
            var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;
            if (originalEndedAt is null && currentEndedAt is not null)
            {
                var activeHistory = PositionAssignmentHistories.Local.FirstOrDefault(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null) ?? PositionAssignmentHistories.FirstOrDefault(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt == null);
                activeHistory?.End(currentEndedAt.Value);
            }
            else if (originalEndedAt is not null && currentEndedAt is null && !PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }
    }
    // Same history logic, for asynchronous saving.
    private async Task CaptureAssignmentHistoryAsync(CancellationToken cancellationToken)
    {
        var entries = ChangeTracker.Entries<EmployeePosition>().ToList();
        foreach (var entry in entries.Where(x => x.State == EntityState.Added))
        if (!PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
            PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        foreach (var entry in entries.Where(x => x.State == EntityState.Modified && x.Property(p => p.EndedAt).IsModified))
        {
            var originalEndedAt = entry.Property(x => x.EndedAt).OriginalValue;
            var currentEndedAt = entry.Property(x => x.EndedAt).CurrentValue;
            if (originalEndedAt is null && currentEndedAt is not null)
            {
                var activeHistory = PositionAssignmentHistories.Local.FirstOrDefault(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null) ?? await PositionAssignmentHistories.FirstOrDefaultAsync(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt == null, cancellationToken);
                activeHistory?.End(currentEndedAt.Value);
            }
            else if (originalEndedAt is not null && currentEndedAt is null && !PositionAssignmentHistories.Local.Any(x => x.EmployeeId == entry.Entity.EmployeeId && x.PositionId == entry.Entity.PositionId && x.EndedAt is null))
                PositionAssignmentHistories.Add(new PositionAssignmentHistory(entry.Entity.EmployeeId, entry.Entity.PositionId));
        }
    }

    // Table definitions follow. Unique indexes protect business rules such as a unique login name or code. Foreign keys
    // with Restrict prevent deleting a record that other records still refer to, so history stays intact.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Audit trail: indexed by time, by kind and id of record, by person, and by archive state for fast filtering.
        modelBuilder.Entity<AuditEntry>(e => { e.HasKey(x => x.Id); e.Property(x => x.Action).HasMaxLength(100).IsRequired(); e.Property(x => x.EntityType).HasMaxLength(200).IsRequired(); e.Property(x => x.EntityId).HasMaxLength(500); e.Property(x => x.BeforeJson).HasColumnType("nvarchar(max)"); e.Property(x => x.AfterJson).HasColumnType("nvarchar(max)"); e.HasIndex(x => x.OccurredAtUtc); e.HasIndex(x => new { x.EntityType, x.EntityId }); e.HasIndex(x => x.EmployeeId); e.HasIndex(x => new { x.ArchivedAtUtc, x.OccurredAtUtc }); });
        // Organizations: name and code must each be unique.
        modelBuilder.Entity<Organization>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.Code).HasMaxLength(50).IsRequired(); });
        // Employees: username (the login) and code are unique; an organization cannot be deleted while it has employees;
        // if the remembered last active position is deleted, it is simply cleared.
        modelBuilder.Entity<Employee>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Username).IsUnique(); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(50).IsRequired(); e.Property(x => x.Username).HasMaxLength(256).IsRequired(); e.Property(x => x.FullName).HasMaxLength(256).IsRequired(); e.Property(x => x.PasswordHash).HasMaxLength(512); e.Property(x => x.AvatarKey).HasMaxLength(Employee.AvatarKeyMaxLength); e.HasOne(x => x.Organization).WithMany(x => x.Employees).HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Position>().WithMany().HasForeignKey(x => x.LastActivePositionId).OnDelete(DeleteBehavior.SetNull); });
        // Home page link groups, nested by parent group; a group with children cannot be deleted.
        modelBuilder.Entity<LinkGroup>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(LinkGroup.NameMax).IsRequired(); e.HasIndex(x => x.ParentId); e.HasOne<LinkGroup>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict); });
        // User guides, indexed by whether they are on and by their display order.
        modelBuilder.Entity<UserGuide>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Title).HasMaxLength(UserGuide.TitleMax).IsRequired(); e.Property(x => x.Summary).HasMaxLength(UserGuide.SummaryMax); e.Property(x => x.ContentHtml).IsRequired(); e.HasIndex(x => new { x.IsActive, x.SortOrder }); e.HasIndex(x => x.GroupId); });
        // Guide groups: the name is unique, so the same group can never exist twice.
        modelBuilder.Entity<UserGuideGroup>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(UserGuideGroup.NameMax).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); });
        // Store groups.
        modelBuilder.Entity<StoreCategory>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(StoreCategory.NameMax).IsRequired(); });
        // Store items: unique code, quantities with up to 3 decimals, prices with 2 decimals, shown in the shop by default,
        // request opening and closing dates; a group cannot be deleted while it has items.
        modelBuilder.Entity<StoreItem>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Code).HasMaxLength(StoreItem.CodeMax).IsRequired(); e.Property(x => x.Name).HasMaxLength(StoreItem.NameMax).IsRequired(); e.Property(x => x.Unit).HasMaxLength(StoreItem.UnitMax).IsRequired(); e.Property(x => x.Description).HasMaxLength(StoreItem.DescriptionMax); e.Property(x => x.MaxPerRequest).HasPrecision(18, 3); e.Property(x => x.Price).HasPrecision(18, 2); e.Property(x => x.IsVisible).HasDefaultValue(true); e.Property(x => x.RequestOpensOn).HasColumnType("datetime2(0)"); e.Property(x => x.RequestClosesOn).HasColumnType("datetime2(0)"); e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => x.CategoryId); e.HasOne<StoreCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict); });
        // Stock ledger: every movement of an item; items with movements cannot be deleted, so the history stays.
        modelBuilder.Entity<StoreStockMovement>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Delta).HasPrecision(18, 3); e.Property(x => x.Reference).HasMaxLength(StoreStockMovement.ReferenceMax); e.Property(x => x.Note).HasMaxLength(StoreStockMovement.NoteMax); e.HasIndex(x => new { x.ItemId, x.CreatedAtUtc }); e.HasOne<StoreItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<StoreOrder>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Note).HasMaxLength(StoreOrder.NoteMax); e.Property(x => x.IssueNote).HasMaxLength(StoreOrder.NoteMax); e.Property(x => x.SalaryDeductionAcceptedAmount).HasPrecision(18, 2); e.HasIndex(x => x.RequestId).IsUnique(); e.HasIndex(x => new { x.EmployeeId, x.CreatedAtUtc }); e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade); e.HasOne<WorkflowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<StoreOrderLine>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.ItemCode).HasMaxLength(StoreItem.CodeMax).IsRequired(); e.Property(x => x.ItemName).HasMaxLength(StoreItem.NameMax).IsRequired(); e.Property(x => x.Unit).HasMaxLength(StoreItem.UnitMax).IsRequired(); e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.UnitPrice).HasPrecision(18, 2); e.HasIndex(x => x.OrderId); e.HasOne<StoreItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<StoreLotteryEntry>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.UnitPrice).HasPrecision(18, 2); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.HasIndex(x => new { x.ItemId, x.EmployeeId }).IsUnique().HasFilter("[Status] = N'Applied'"); e.HasIndex(x => new { x.EmployeeId, x.AppliedAtUtc }); e.HasOne<StoreItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ContactGroup>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(ContactGroup.NameMax).IsRequired(); });
        // Phone book contacts: text length limits for each field; a group cannot be deleted while it has contacts.
        modelBuilder.Entity<Contact>(e =>
        {
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FullName).HasMaxLength(Contact.NameMax).IsRequired();
            e.Property(x => x.JobTitle).HasMaxLength(Contact.TitleMax);
            e.Property(x => x.Extension).HasMaxLength(Contact.NumberMax);
            e.Property(x => x.Phone).HasMaxLength(Contact.NumberMax);
            e.Property(x => x.Mobile).HasMaxLength(Contact.NumberMax);
            e.Property(x => x.Email).HasMaxLength(Contact.EmailMax);
            e.Property(x => x.Note).HasMaxLength(Contact.NoteMax);
            e.HasIndex(x => x.GroupId);
            e.HasOne<ContactGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        });
        // Home page links: title and address required; a group cannot be deleted while it has links.
        modelBuilder.Entity<PortalLink>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Title).HasMaxLength(PortalLink.TitleMax).IsRequired(); e.Property(x => x.Url).HasMaxLength(PortalLink.UrlMax).IsRequired(); e.Property(x => x.Description).HasMaxLength(PortalLink.DescriptionMax); e.HasIndex(x => x.GroupId); e.HasOne<LinkGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict); });
        // File attached to a home slide: one file per slide, deleted together with the slide.
        // Files of a guide: removed together with the guide.
        // The file of a link: one row per link, removed together with the link.
        modelBuilder.Entity<PortalLinkFile>(e => { e.HasKey(x => x.LinkId); e.Property(x => x.LinkId).ValueGeneratedNever(); e.Property(x => x.FileName).HasMaxLength(UserGuideAttachment.FileNameMax).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); e.Property(x => x.Content).IsRequired(); e.HasOne<PortalLink>().WithOne().HasForeignKey<PortalLinkFile>(x => x.LinkId).OnDelete(DeleteBehavior.Cascade); });
        modelBuilder.Entity<UserGuideAttachment>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.FileName).HasMaxLength(UserGuideAttachment.FileNameMax).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); e.Property(x => x.Content).IsRequired(); e.HasOne<UserGuide>().WithMany().HasForeignKey(x => x.GuideId).OnDelete(DeleteBehavior.Cascade); e.HasIndex(x => x.GuideId); });
        modelBuilder.Entity<HomeSlideAttachment>(e => { e.HasKey(x => x.SlideId); e.Property(x => x.SlideId).ValueGeneratedNever(); e.Property(x => x.FileName).HasMaxLength(HomeSlideAttachment.FileNameMax).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); e.Property(x => x.Content).IsRequired(); e.HasOne<HomeSlide>().WithOne().HasForeignKey<HomeSlideAttachment>(x => x.SlideId).OnDelete(DeleteBehavior.Cascade); });
        // Gallery pictures: a list ordered by the administrator; the bytes sit in their own table and go with the picture.
        modelBuilder.Entity<GalleryImage>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Title).HasMaxLength(GalleryImage.TitleMax).IsRequired(); e.Property(x => x.Caption).HasMaxLength(GalleryImage.CaptionMax); e.Property(x => x.FileName).HasMaxLength(UserGuideAttachment.FileNameMax).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); e.HasIndex(x => new { x.IsActive, x.SortOrder }); e.HasIndex(x => x.GroupId); });
        modelBuilder.Entity<GalleryGroup>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(GalleryGroup.NameMax).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); });
        modelBuilder.Entity<GalleryImageFile>(e => { e.HasKey(x => x.ImageId); e.Property(x => x.ImageId).ValueGeneratedNever(); e.Property(x => x.Content).IsRequired(); e.HasOne<GalleryImage>().WithOne().HasForeignKey<GalleryImageFile>(x => x.ImageId).OnDelete(DeleteBehavior.Cascade); });
        // Home slides: text length limits, optional start and end dates, indexed by on/off and order.
        modelBuilder.Entity<HomeTile>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Title).HasMaxLength(HomeTile.TitleMax).IsRequired(); e.Property(x => x.Text).HasMaxLength(HomeTile.TextMax); e.Property(x => x.Icon).HasMaxLength(HomeSlide.IconMax).IsRequired(); e.Property(x => x.Url).HasMaxLength(HomeTile.UrlMax).IsRequired(); });
        modelBuilder.Entity<HomeSlide>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Title).HasMaxLength(HomeSlide.TitleMax).IsRequired(); e.Property(x => x.Text).HasMaxLength(HomeSlide.TextMax).IsRequired(); e.Property(x => x.Icon).HasMaxLength(HomeSlide.IconMax).IsRequired(); e.Property(x => x.LinkText).HasMaxLength(HomeSlide.LinkTextMax); e.Property(x => x.LinkUrl).HasMaxLength(HomeSlide.LinkUrlMax); e.Property(x => x.StartsOn).HasColumnType("date"); e.Property(x => x.ExpiresOn).HasColumnType("date"); e.HasIndex(x => new { x.IsActive, x.SortOrder }); });
        // Settings row for the audit archive; its id is set by the application.
        modelBuilder.Entity<AuditArchiveSetting>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); });
        // Settings row for the password policy; its id is set by the application.
        modelBuilder.Entity<PasswordPolicy>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); });
        // Positions: unique name and code, arranged in a tree by parent position, active by default, belong to an organization.
        modelBuilder.Entity<Position>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(50).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.HasOne(x => x.ParentPosition).WithMany(x => x.Children).HasForeignKey(x => x.ParentPositionId).OnDelete(DeleteBehavior.Restrict); e.Property(x => x.IsActive).HasDefaultValue(true); e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => x.OrganizationId); });
        // A person holding a position. The unique index on the position covers only rows with no end date, so a position
        // has at most one current occupant; ended assignments stay as history.
        modelBuilder.Entity<EmployeePosition>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId }); e.HasOne(x => x.Employee).WithMany(x => x.Positions).HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL"); });
        // History of who held each position and when; again only one open (not ended) row per position.
        modelBuilder.Entity<PositionAssignmentHistory>(e => { e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.StartedAt }); e.HasIndex(x => new { x.PositionId, x.StartedAt }); e.HasIndex(x => x.PositionId).IsUnique().HasFilter("[EndedAt] IS NULL"); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); });
        // Permission catalog: each permission code is unique.
        modelBuilder.Entity<Permission>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(200).IsRequired(); e.Property(x => x.Name).HasMaxLength(300).IsRequired(); });
        // A permission granted to a person while they hold a specific position (the employee track).
        modelBuilder.Entity<UserPositionPermission>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId, x.PermissionId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        // A permission granted directly to an administrator (the admin track).
        modelBuilder.Entity<EmployeePermission>(e => { e.HasKey(x => new { x.EmployeeId, x.PermissionId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        // Permission groups: bundles of permissions with a unique name and code.
        modelBuilder.Entity<PermissionGroup>(e => { e.HasKey(x => x.Id); e.HasIndex(x => x.Name).IsUnique(); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Code).HasMaxLength(50).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.Description).HasMaxLength(500); });
        // Which permissions belong to which group.
        modelBuilder.Entity<PermissionGroupPermission>(e => { e.HasKey(x => new { x.GroupId, x.PermissionId }); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade); });
        // A permission group granted to a person for a specific position.
        modelBuilder.Entity<UserPositionPermissionGroup>(e => { e.HasKey(x => new { x.EmployeeId, x.PositionId, x.GroupId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Position).WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        // A permission group granted directly to an administrator.
        modelBuilder.Entity<EmployeePermissionGroup>(e => { e.HasKey(x => new { x.EmployeeId, x.GroupId }); e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade); });
        // Approval step definitions per request type; the type and rules are stored as readable text, indexed by type and order.
        modelBuilder.Entity<WorkflowStepDefinition>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.WorkflowType).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.ApproverRuleKind).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.ApprovalMode).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.RejectPolicy).HasConversion<string>().HasMaxLength(50).IsRequired(); e.HasIndex(x => new { x.WorkflowType, x.Order }); e.HasOne<Position>().WithMany().HasForeignKey(x => x.ApproverPositionId).OnDelete(DeleteBehavior.Restrict); });
        // Requests: Revision guards against two people changing the same request at once; the status and type are stored as
        // text; the filled-in form is stored as one JSON text; creator, requester and position cannot be deleted while used.
        modelBuilder.Entity<WorkflowRequest>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Revision).IsConcurrencyToken(); e.Property(x => x.ForceCloseReason).HasMaxLength(1000); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ForceClosedByEmployeeId).OnDelete(DeleteBehavior.Restrict); e.Property(x => x.WorkflowType).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.FieldValuesJson).HasColumnType("nvarchar(max)").IsRequired(); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RequesterEmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Position>().WithMany().HasForeignKey(x => x.RequesterPositionId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new { x.RequesterEmployeeId, x.CreatedAtUtc }); e.HasIndex(x => new { x.WorkflowType, x.Status }); });
        // Files attached to a request: deleted together with the request; the uploader cannot be deleted while referenced.
        modelBuilder.Entity<WorkflowRequestAttachment>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.FileName).HasMaxLength(WorkflowRequestAttachment.FileNameMax).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); e.Property(x => x.Content).IsRequired(); e.HasOne<WorkflowRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.UploadedByEmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => x.RequestId); });
        // Steps of a request: unique per request, round, order and approver; indexed by approver and status for inboxes.
        modelBuilder.Entity<WorkflowRequestStep>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.Comment).HasMaxLength(2000); e.HasOne<WorkflowRequest>().WithMany(x => x.Steps).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade); e.HasOne<WorkflowStepDefinition>().WithMany().HasForeignKey(x => x.StepDefinitionId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Position>().WithMany().HasForeignKey(x => x.ApproverPositionId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ActedByEmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new { x.ApproverPositionId, x.Status }); e.HasIndex(x => new { x.RequestId, x.Round, x.Order, x.ApproverPositionId }).IsUnique(); e.Property(x => x.ApprovalMode).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.RejectPolicy).HasConversion<string>().HasMaxLength(50).IsRequired(); });
        // Actions by the requester (such as cancelling or reopening), indexed by request, round and time.
        modelBuilder.Entity<WorkflowRequesterAction>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(50).IsRequired(); e.Property(x => x.Reason).HasMaxLength(1000); e.HasOne<WorkflowRequest>().WithMany(x => x.RequesterActions).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new { x.RequestId, x.Round, x.AtUtc }); });
        // Decisions on steps, kept as history, indexed by request, round, order and time.
        modelBuilder.Entity<WorkflowStepDecision>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(50).IsRequired(); e.HasOne<WorkflowRequest>().WithMany(x => x.StepDecisions).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ActedByEmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Position>().WithMany().HasForeignKey(x => x.ApproverPositionId).OnDelete(DeleteBehavior.Restrict); e.HasIndex(x => new { x.RequestId, x.Round, x.Order, x.ActedAtUtc }); e.HasIndex(x => x.StepId); });
        // The approver inbox history filters decisions by the position that decided, newest first.
        modelBuilder.Entity<WorkflowStepDecision>().HasIndex(x => new { x.ApproverPositionId, x.ActedAtUtc });
        // Append-only field history (spec §11.3). Indexed per §20.1: request, actor and timestamp.
        modelBuilder.Entity<WorkflowFieldChange>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FieldKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
            e.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
            e.HasOne<WorkflowRequest>().WithMany(x => x.FieldChanges).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ChangedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Position>().WithMany().HasForeignKey(x => x.ChangedByPositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RequestId, x.ChangedAtUtc });
            e.HasIndex(x => x.ChangedByEmployeeId);
        });
        // Delegations (spec §15, §16): never deleted, only revoked.
        modelBuilder.Entity<PositionDelegation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.StartDate).HasColumnType("date");
            e.Property(x => x.EndDate).HasColumnType("date");
            e.HasOne<Position>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Position>().WithMany().HasForeignKey(x => x.DelegatePositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.DelegatorEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RevokedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DelegatePositionId, x.StartDate, x.EndDate });
            e.HasIndex(x => new { x.PositionId, x.StartDate });
            e.HasIndex(x => x.DelegatorEmployeeId);
        });
        // In-app notifications (spec §17). Never deleted; the inbox reads them newest first per recipient.
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(50).IsRequired();
            e.Property(x => x.RequestType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.Property(x => x.LinkPath).HasMaxLength(300).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RecipientEmployeeId).OnDelete(DeleteBehavior.Cascade);
            // NoAction: SQL Server allows only one cascade path from Employees, and a sender's messages must outlive nothing but the sender row itself.
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.SenderEmployeeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.RecipientEmployeeId, x.IsRead, x.CreatedAtUtc });
            e.HasIndex(x => x.RequestId);
            e.Property(x => x.AudienceLabel).HasMaxLength(300);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.RemovedByEmployeeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.BatchId);
            e.HasIndex(x => new { x.SenderEmployeeId, x.CreatedAtUtc });
            e.HasIndex(x => x.CreatedAtUtc);
        });
        // Reusable recipient lists: private to their owner. Members' FK to Employees is NoAction (SQL Server allows one cascade path: Employees -> groups -> members).
        modelBuilder.Entity<RecipientGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(RecipientGroup.MaxNameLength).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.OwnerEmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OwnerEmployeeId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<RecipientGroupMember>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.EmployeeId });
            e.HasOne<RecipientGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.EmployeeId);
        });
        // Per-employee appearance settings: cosmetic only, one row per employee, never audited.
        modelBuilder.Entity<WaitingReminderSetting>(e => { e.HasKey(x => x.EmployeeId); e.Property(x => x.EmployeeId).ValueGeneratedNever(); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade); });
        // Appearance choices (theme, accent, font, text size, density) are stored as text; one row per employee.
        modelBuilder.Entity<UserUiPreference>(e =>
        {
            e.HasKey(x => x.EmployeeId);
            e.Property(x => x.EmployeeId).ValueGeneratedNever();
            e.Property(x => x.Theme).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Accent).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Font).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.TextSize).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Density).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
