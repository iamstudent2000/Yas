using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using YasPortal.Domain.Gallery;
using YasPortal.Infrastructure.Gallery;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

// Gallery group tests: the rules of the group list (unique name, delete only when empty, switched-off groups), the choice made in a
// picture's form, and what visitors see (a switched-off group hides its pictures; headings follow the group order).
public sealed class GalleryGroupTests
{
    [Fact]
    public void Group_needs_a_name_that_is_not_too_long_and_can_be_switched_off()
    {
        Assert.Throws<ArgumentException>(() => new GalleryGroup("  ", 1));
        Assert.Throws<ArgumentException>(() => new GalleryGroup(new string('x', GalleryGroup.NameMax + 1), 1));
        var group = new GalleryGroup("  مراسم   سالانه ", 3);
        Assert.Equal("مراسم سالانه", group.Name);
        Assert.Equal(3, group.SortOrder);
        Assert.True(group.IsActive);
        group.Deactivate();
        Assert.False(group.IsActive);
        group.Activate();
        Assert.True(group.IsActive);
    }

    [Fact]
    public async Task Save_adds_and_renames_groups_and_refuses_empty_or_used_names()
    {
        var (service, _) = Create();
        Assert.True((await service.SaveAsync(null, "مراسم", 1)).Ok);
        Assert.True((await service.SaveAsync(null, "محل کار", 2)).Ok);

        Assert.False((await service.SaveAsync(null, "   ", 3)).Ok);                        // empty name
        Assert.False((await service.SaveAsync(null, "  محل کار ", 3)).Ok);                 // same name after cleaning
        Assert.True((await service.SaveAsync(null, "Party", 4)).Ok);
        Assert.False((await service.SaveAsync(null, "pARTY", 5)).Ok);                      // letter case does not make a new group
        Assert.True((await service.SaveAsync(null, "۱۴۰۵", 6)).Ok);
        Assert.False((await service.SaveAsync(null, "1405", 7)).Ok);                       // Latin digits instead of Persian
        Assert.False((await service.SaveAsync(null, new string('x', GalleryGroup.NameMax + 1), 3)).Ok);

        var work = (await service.ListAsync(true)).Single(g => g.Name == "محل کار");
        Assert.True((await service.SaveAsync(work.Id, "محل کار", 9)).Ok);                  // keeping its own name is fine
        Assert.False((await service.SaveAsync(work.Id, "مراسم", 9)).Ok);                   // taking another group's name is not
        Assert.False((await service.SaveAsync(Guid.NewGuid(), "نو", 1)).Ok);               // unknown group
        Assert.Equal(9, (await service.ListAsync(true)).Single(g => g.Name == "محل کار").SortOrder);
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
    public async Task Delete_is_refused_while_a_picture_uses_the_group()
    {
        var (service, factory) = Create();
        await service.SaveAsync(null, "پرکاربرد", 1);
        await service.SaveAsync(null, "خالی", 2);
        var used = (await service.ListAsync(true)).Single(g => g.Name == "پرکاربرد");
        var empty = (await service.ListAsync(true)).Single(g => g.Name == "خالی");
        await using (var db = factory.CreateDbContext())
        {
            db.GalleryImages.Add(new GalleryImage("t", null, 1, "a.png", 10, used.Id));
            await db.SaveChangesAsync();
        }

        Assert.False((await service.DeleteAsync(used.Id)).Ok);
        Assert.True((await service.DeleteAsync(empty.Id)).Ok);
        Assert.True((await service.DeleteAsync(empty.Id)).Ok);   // already gone is not an error
        Assert.Equal(["پرکاربرد"], (await service.ListAsync(true)).Select(g => g.Name));
        Assert.Equal(1, (await service.CountImagesAsync())[used.Id]);
    }

    [Fact]
    public async Task Choice_must_exist_and_be_on_unless_the_picture_already_belongs_to_it()
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
        Assert.Null(await service.CheckChoiceAsync(off.Id, off.Id));                   // but a picture may keep it
        Assert.NotNull(await service.CheckChoiceAsync(Guid.NewGuid(), null));          // a deleted group
    }

    [Fact]
    public async Task Visitors_see_only_switched_on_pictures_not_those_of_a_switched_off_group_and_sections_follow_the_group_order()
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
            db.GalleryImages.Add(new GalleryImage("الف", null, 1, "a.png", 10, Id("دوم")));
            db.GalleryImages.Add(new GalleryImage("ب", null, 2, "b.png", 10, Id("اول")));
            db.GalleryImages.Add(new GalleryImage("پ", null, 3, "c.png", 10, Id("خاموش")));
            db.GalleryImages.Add(new GalleryImage("ت", null, 4, "d.png", 10));
            var off = new GalleryImage("ث", null, 5, "e.png", 10, Id("اول"));
            off.Deactivate();
            db.GalleryImages.Add(off);
            await db.SaveChangesAsync();
        }

        var list = await new GalleryProvider(factory, NullLogger<GalleryProvider>.Instance).ListAsync();
        Assert.DoesNotContain(list, g => g.Title == "پ");                               // its group is switched off
        Assert.DoesNotContain(list, g => g.Title == "ث");                               // the picture itself is switched off
        var sections = GalleryCatalog.Group(list);
        Assert.Equal(["اول", "دوم", null], sections.Select(s => s.GroupName));          // the group's order number wins over the picture order

        // The group buttons: one group, only the pictures without a group, or everything.
        Assert.Equal(["الف"], GalleryCatalog.Filter(list, "دوم").Select(g => g.Title));
        Assert.Equal(["ت"], GalleryCatalog.Filter(list, "").Select(g => g.Title));
        Assert.Equal(3, GalleryCatalog.Filter(list, null).Count);
    }

    private static (GalleryGroupService Service, Factory Factory) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        var factory = new Factory(options);
        return (new GalleryGroupService(factory), factory);
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
