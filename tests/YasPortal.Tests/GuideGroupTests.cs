using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using YasPortal.Domain.Guides;
using YasPortal.Infrastructure.Guides;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

// Guide group tests: cleaning of names, the rules of the group list (unique name, delete only when empty, switched-off groups),
// the choice made in a guide's form, and what readers see when a group is switched off.
public sealed class GuideGroupTests
{
    [Fact]
    public void Group_name_is_cleaned_and_blank_means_no_name()
    {
        Assert.Equal("درخواست‌ها", UserGuideGroup.NormalizeName("  درخواست‌ها  "));
        Assert.Equal("سامانه فروشگاه", UserGuideGroup.NormalizeName("سامانه    فروشگاه"));
        Assert.Equal("کیف", UserGuideGroup.NormalizeName("كيف"));   // Arabic ک and ی become the Persian ones
        Assert.Null(UserGuideGroup.NormalizeName("   "));
        Assert.Null(UserGuideGroup.NormalizeName(null));
    }

    [Fact]
    public void Group_needs_a_name_that_is_not_too_long()
    {
        Assert.Throws<ArgumentException>(() => new UserGuideGroup("  ", 1));
        Assert.Throws<ArgumentException>(() => new UserGuideGroup(new string('x', UserGuideGroup.NameMax + 1), 1));
        var group = new UserGuideGroup(" فروشگاه ", 3);
        Assert.Equal("فروشگاه", group.Name);
        Assert.Equal(3, group.SortOrder);
        Assert.True(group.IsActive);
        group.Deactivate();
        Assert.False(group.IsActive);
        group.Activate();
        Assert.True(group.IsActive);
    }

    [Fact]
    public void Guide_keeps_the_group_id_and_an_empty_id_means_no_group()
    {
        var id = Guid.NewGuid();
        var guide = new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, id);
        Assert.Equal(id, guide.GroupId);
        guide.Update("t", null, "<p>a</p>", GuideVisibility.Public, 1, Guid.Empty);
        Assert.Null(guide.GroupId);
        guide.Update("t", null, "<p>a</p>", GuideVisibility.Public, 1, id);
        guide.Update("t", null, "<p>a</p>", GuideVisibility.Public, 1);
        Assert.Null(guide.GroupId);
    }

    [Fact]
    public async Task Save_adds_and_renames_groups_and_refuses_empty_or_used_names()
    {
        var (service, _) = Create();
        Assert.True((await service.SaveAsync(null, "درخواست‌ها", 1)).Ok);
        Assert.True((await service.SaveAsync(null, "فروشگاه", 2)).Ok);

        Assert.False((await service.SaveAsync(null, "   ", 3)).Ok);                        // empty name
        Assert.False((await service.SaveAsync(null, "  فروشگاه ", 3)).Ok);                 // same name after cleaning
        Assert.False((await service.SaveAsync(null, "فروشگاه", 3)).Ok);                    // exactly the same
        Assert.True((await service.SaveAsync(null, "Shop", 4)).Ok);
        Assert.False((await service.SaveAsync(null, "sHOP", 5)).Ok);                       // letter case does not make a new group
        Assert.False((await service.SaveAsync(null, new string('x', UserGuideGroup.NameMax + 1), 3)).Ok);

        var groups = await service.ListAsync(true);
        var shop = groups.Single(g => g.Name == "فروشگاه");
        Assert.True((await service.SaveAsync(shop.Id, "فروشگاه", 9)).Ok);                  // keeping its own name is fine
        Assert.False((await service.SaveAsync(shop.Id, "درخواست‌ها", 9)).Ok);              // taking another group's name is not
        Assert.False((await service.SaveAsync(Guid.NewGuid(), "نو", 1)).Ok);               // unknown group
        Assert.Equal(9, (await service.ListAsync(true)).Single(g => g.Name == "فروشگاه").SortOrder);
    }

    [Fact]
    public async Task List_is_in_order_and_hides_switched_off_groups_unless_asked()
    {
        var (service, _) = Create();
        await service.SaveAsync(null, "ب", 2);
        await service.SaveAsync(null, "الف", 1);
        await service.SaveAsync(null, "ج", 1);
        var off = (await service.ListAsync(true)).Single(g => g.Name == "ب");
        await service.SetActiveAsync(off.Id, false);

        Assert.Equal(["الف", "ج", "ب"], (await service.ListAsync(true)).Select(g => g.Name));   // order number, then name
        Assert.Equal(["الف", "ج"], (await service.ListAsync(false)).Select(g => g.Name));
        Assert.False((await service.SetActiveAsync(Guid.NewGuid(), true)).Ok);
    }

    [Fact]
    public async Task Delete_is_refused_while_a_guide_uses_the_group()
    {
        var (service, factory) = Create();
        await service.SaveAsync(null, "پرکاربرد", 1);
        await service.SaveAsync(null, "خالی", 2);
        var used = (await service.ListAsync(true)).Single(g => g.Name == "پرکاربرد");
        var empty = (await service.ListAsync(true)).Single(g => g.Name == "خالی");
        await using (var db = factory.CreateDbContext())
        {
            db.UserGuides.Add(new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, used.Id));
            await db.SaveChangesAsync();
        }

        Assert.False((await service.DeleteAsync(used.Id)).Ok);
        Assert.True((await service.DeleteAsync(empty.Id)).Ok);
        Assert.True((await service.DeleteAsync(empty.Id)).Ok);   // already gone is not an error
        Assert.Equal(["پرکاربرد"], (await service.ListAsync(true)).Select(g => g.Name));
        Assert.Equal(1, (await service.CountGuidesAsync())[used.Id]);
    }

    [Fact]
    public async Task Choice_must_exist_and_be_on_unless_the_guide_already_belongs_to_it()
    {
        var (service, _) = Create();
        await service.SaveAsync(null, "روشن", 1);
        await service.SaveAsync(null, "خاموش", 2);
        var on = (await service.ListAsync(true)).Single(g => g.Name == "روشن");
        var off = (await service.ListAsync(true)).Single(g => g.Name == "خاموش");
        await service.SetActiveAsync(off.Id, false);

        Assert.Null(await service.CheckChoiceAsync(null, null));                       // no group is fine
        Assert.Null(await service.CheckChoiceAsync(Guid.Empty, null));
        Assert.Null(await service.CheckChoiceAsync(on.Id, null));
        Assert.NotNull(await service.CheckChoiceAsync(off.Id, null));                  // nobody can newly pick a switched-off group
        Assert.Null(await service.CheckChoiceAsync(off.Id, off.Id));                   // but a guide may keep it
        Assert.NotNull(await service.CheckChoiceAsync(Guid.NewGuid(), null));          // a deleted group
    }

    [Fact]
    public async Task Readers_do_not_see_the_guides_of_a_switched_off_group_and_sections_follow_the_group_order()
    {
        var (service, factory) = Create();
        await service.SaveAsync(null, "دوم", 2);
        await service.SaveAsync(null, "اول", 1);
        await service.SaveAsync(null, "خاموش", 3);
        var groups = await service.ListAsync(true);
        Guid Id(string name) => groups.Single(g => g.Name == name).Id;
        await service.SetActiveAsync(Id("خاموش"), false);
        await using (var db = factory.CreateDbContext())
        {
            db.UserGuides.Add(new UserGuide("الف", null, "<p>a</p>", GuideVisibility.Public, 1, null, Id("دوم")));
            db.UserGuides.Add(new UserGuide("ب", null, "<p>a</p>", GuideVisibility.Public, 2, null, Id("اول")));
            db.UserGuides.Add(new UserGuide("پ", null, "<p>a</p>", GuideVisibility.Public, 3, null, Id("خاموش")));
            db.UserGuides.Add(new UserGuide("ت", null, "<p>a</p>", GuideVisibility.Public, 4));
            await db.SaveChangesAsync();
        }

        var provider = new GuideProvider(factory, NullLogger<GuideProvider>.Instance);
        var list = await provider.ListAsync(signedIn: true);
        Assert.DoesNotContain(list, g => g.Title == "پ");                               // its group is switched off
        var sections = GuideCatalog.Group(list);
        Assert.Equal(["اول", "دوم", null], sections.Select(s => s.GroupName));          // the group's order number wins over the guide order
    }

    private static (GuideGroupService Service, Factory Factory) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        var factory = new Factory(options);
        return (new GuideGroupService(factory), factory);
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
