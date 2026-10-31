using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class AuditArchiveTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Archives_only_current_rows_older_than_the_cutoff_and_keeps_every_row()
    {
        var (options, archiver) = Create();
        await Seed(options, Now.AddMonths(-14), Now.AddMonths(-13), Now.AddMonths(-2), Now);

        var cutoff = Now.AddMonths(-12);
        Assert.Equal(2, await archiver.CountOlderThanAsync(cutoff));
        Assert.Equal(2, await archiver.ArchiveOlderThanAsync(cutoff));

        await using var db = new ApplicationDbContext(options);
        Assert.Equal(4, await db.AuditEntries.CountAsync());
        Assert.Equal(2, await db.AuditEntries.CountAsync(x => x.ArchivedAtUtc != null));
        Assert.All(db.AuditEntries.Where(x => x.ArchivedAtUtc != null), x => Assert.True(x.OccurredAtUtc < cutoff));
        Assert.Equal(2, await db.AuditEntries.CountAsync(x => x.ArchivedAtUtc == null && x.OccurredAtUtc >= cutoff));
    }

    [Fact]
    public async Task Archiving_twice_does_nothing_the_second_time()
    {
        var (options, archiver) = Create();
        await Seed(options, Now.AddMonths(-20), Now.AddMonths(-18));
        var cutoff = Now.AddMonths(-12);

        Assert.Equal(2, await archiver.ArchiveOlderThanAsync(cutoff));
        Assert.Equal(0, await archiver.CountOlderThanAsync(cutoff));
        Assert.Equal(0, await archiver.ArchiveOlderThanAsync(cutoff));
    }

    [Fact]
    public async Task Archiving_handles_more_rows_than_one_batch()
    {
        var (options, archiver) = Create();
        await Seed(options, Enumerable.Range(0, 4500).Select(i => Now.AddMonths(-24).AddMinutes(i)).ToArray());

        Assert.Equal(4500, await archiver.ArchiveOlderThanAsync(Now.AddMonths(-12)));
    }

    [Fact]
    public void Archive_keeps_the_first_archive_time()
    {
        var entry = new AuditEntry(null, null, "X", "Y", null, null, null, Now.AddYears(-2));
        entry.Archive(Now);
        entry.Archive(Now.AddDays(5));
        Assert.Equal(Now, entry.ArchivedAtUtc);
    }

    [Fact]
    public void Automatic_run_is_due_only_when_on_and_not_run_in_the_last_23_hours()
    {
        var setting = AuditArchiveSetting.CreateDefault();
        Assert.True(setting.IsAutomaticRunDue(Now));                // on, never ran

        setting.RecordAutomaticRun(Now.AddHours(-5), 10);
        Assert.False(setting.IsAutomaticRunDue(Now));               // ran a few hours ago
        Assert.True(setting.IsAutomaticRunDue(Now.AddHours(19)));   // 24 hours after the run

        setting.Update(automaticEnabled: false, afterMonths: 6);
        Assert.False(setting.IsAutomaticRunDue(Now.AddDays(30)));   // manual only: never due
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(121)]
    public void Setting_rejects_months_out_of_range(int months)
    {
        var setting = AuditArchiveSetting.CreateDefault();
        Assert.Throws<ArgumentOutOfRangeException>(() => setting.Update(true, months));
    }

    [Fact]
    public void Setting_defaults_are_clamped_and_updates_apply()
    {
        Assert.Equal(AuditArchiveSetting.HighestMonths, AuditArchiveSetting.CreateDefault(true, 5000).AfterMonths);
        var setting = AuditArchiveSetting.CreateDefault();
        setting.Update(false, 3);
        Assert.False(setting.AutomaticEnabled);
        Assert.Equal(3, setting.AfterMonths);
    }

    private static (DbContextOptions<ApplicationDbContext> Options, AuditArchiver Archiver) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        return (options, new AuditArchiver(new Factory(options)));
    }

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options, params DateTime[] times)
    {
        await using var db = new ApplicationDbContext(options);
        db.AuditEntries.AddRange(times.Select(t => new AuditEntry(null, null, AuditActions.PageViewed, AuditEntityTypes.Access, null, null, null, t)));
        await db.SaveChangesAsync();
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
