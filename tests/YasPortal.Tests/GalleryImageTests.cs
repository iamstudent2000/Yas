using YasPortal.Domain.Files;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Gallery;
using YasPortal.Domain.Guides;
using Xunit;

namespace YasPortal.Tests;

// Image gallery tests: which files are accepted (type, size, name), how many may be uploaded at once, the title and caption rules,
// the search rule and the gallery permissions.
public sealed class GalleryImageTests
{
    [Theory]
    [InlineData("a.png")]
    [InlineData("a.jpg")]
    [InlineData("PHOTO.JPEG")]
    [InlineData("a.gif")]
    [InlineData("a.WebP")]
    public void Picture_types_are_accepted_whatever_the_letter_case(string name) =>
        Assert.Null(GalleryImage.ValidateFile(name, 100));

    [Theory]
    [InlineData("image.svg")]       // a browser could run it
    [InlineData("page.html")]
    [InlineData("form.pdf")]        // allowed for guides, but not a picture
    [InlineData("run.exe")]
    [InlineData("noextension")]
    [InlineData("")]
    public void Other_types_are_refused(string name) =>
        Assert.NotNull(GalleryImage.ValidateFile(name, 100));

    [Fact]
    public void Empty_and_too_big_pictures_are_refused_with_the_guide_attachment_limit()
    {
        Assert.NotNull(GalleryImage.ValidateFile("a.png", 0));
        Assert.Null(GalleryImage.ValidateFile("a.png", UploadRules.MaxBytes));
        Assert.NotNull(GalleryImage.ValidateFile("a.png", UploadRules.MaxBytes + 1));
        Assert.NotNull(GalleryImage.ValidateFile(new string('x', UploadRules.FileNameMax) + ".png", 10));
    }

    [Fact]
    public void Only_a_limited_number_of_pictures_may_be_uploaded_at_once()
    {
        Assert.Null(GalleryImage.ValidateCount(GalleryImage.MaxPerUpload));
        Assert.NotNull(GalleryImage.ValidateCount(GalleryImage.MaxPerUpload + 1));
    }

    [Fact]
    public void New_picture_keeps_its_details_and_is_switched_on()
    {
        var image = new GalleryImage("  عکس  ", "  توضیح  ", 3, @"C:\x\a.PNG", 10);
        Assert.NotEqual(Guid.Empty, image.Id);
        Assert.Equal("عکس", image.Title);
        Assert.Equal("توضیح", image.Caption);
        Assert.Equal(3, image.SortOrder);
        Assert.True(image.IsActive);
        Assert.Equal("a.PNG", image.FileName);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(10, image.SizeBytes);
        Assert.Throws<ArgumentException>(() => new GalleryImage("t", null, 1, "a.svg", 10));   // type not allowed
        Assert.Throws<ArgumentException>(() => new GalleryImage("t", null, 1, "a.png", 0));    // empty file
    }

    [Fact]
    public void A_blank_title_becomes_the_file_name_without_its_ending()
    {
        Assert.Equal("جشن سال نو", new GalleryImage(null, null, 1, "جشن سال نو.jpg", 10).Title);
        Assert.Equal("a", new GalleryImage("  ", null, 1, "a.gif", 10).Title);
        Assert.Equal(GalleryImage.TitleMax, GalleryImage.TitleFromFileName(new string('x', 300) + ".png").Length);
    }

    [Fact]
    public void Update_needs_a_title_and_cuts_a_blank_caption()
    {
        var image = new GalleryImage("t", "c", 1, "a.png", 10);
        image.Update("new", "  ", 7);
        Assert.Equal("new", image.Title);
        Assert.Null(image.Caption);
        Assert.Equal(7, image.SortOrder);
        Assert.Throws<ArgumentException>(() => image.Update(" ", null, 1));
        Assert.Throws<ArgumentException>(() => image.Update(new string('x', GalleryImage.TitleMax + 1), null, 1));
        Assert.Throws<ArgumentException>(() => image.Update("t", new string('x', GalleryImage.CaptionMax + 1), 1));
    }

    [Fact]
    public void A_picture_can_be_switched_off_and_on()
    {
        var image = new GalleryImage("t", null, 1, "a.png", 10);
        image.Deactivate();
        Assert.False(image.IsActive);
        image.Activate();
        Assert.True(image.IsActive);
    }

    [Fact]
    public void Search_needs_every_word_in_title_caption_or_file_name_and_ignores_arabic_letter_forms()
    {
        Assert.True(GalleryImage.Matches("جشن سال نو", null, "a.png", null, null));        // no search: everything
        Assert.True(GalleryImage.Matches("جشن سال نو", "همکاران", "a.png", null, "جشن همکاران"));
        Assert.True(GalleryImage.Matches("جشن", null, "party.png", null, "PARTY"));
        Assert.False(GalleryImage.Matches("جشن", null, "a.png", null, "جشن همکاران"));
        Assert.True(GalleryImage.Matches("كتاب", null, "a.png", null, "کتاب"));            // Arabic kaf = Persian kaf
        Assert.True(GalleryImage.Matches("جشن", null, "a.png", "مراسم", "مراسم"));         // the group name is searched too
    }

    [Fact]
    public void A_picture_keeps_its_group_and_an_empty_id_means_no_group()
    {
        var group = Guid.NewGuid();
        var image = new GalleryImage("t", null, 1, "a.png", 10, group);
        Assert.Equal(group, image.GroupId);
        image.Update("t", null, 1, Guid.Empty);
        Assert.Null(image.GroupId);
        image.Update("t", null, 1, group);
        image.Update("t", null, 1);
        Assert.Null(image.GroupId);
    }

    [Fact]
    public void The_picture_address_points_to_the_picture()
    {
        var id = Guid.NewGuid();
        Assert.Equal("/gallery/images/" + id + "?v=2", GalleryImage.UrlFor(id, 2));
    }

    [Fact]
    public void Replacing_the_file_changes_the_details_and_the_address_but_keeps_title_caption_and_order()
    {
        var image = new GalleryImage("t", "c", 4, "a.png", 10);
        var before = GalleryImage.UrlFor(image.Id, image.Version);
        image.ReplaceFile("folder/b.JPG", 99);
        Assert.Equal("b.JPG", image.FileName);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal(99, image.SizeBytes);
        Assert.Equal(("t", "c", 4), (image.Title, image.Caption, image.SortOrder));
        Assert.NotEqual(before, GalleryImage.UrlFor(image.Id, image.Version));
        // A refused file changes nothing.
        Assert.Throws<ArgumentException>(() => image.ReplaceFile("b.svg", 5));
        Assert.Throws<ArgumentException>(() => image.ReplaceFile("c.png", UploadRules.MaxBytes + 1));
        Assert.Equal("b.JPG", image.FileName);
        Assert.Equal(1, image.Version);
    }

    [Fact]
    public void Gallery_permissions_exist_and_manage_covers_view()
    {
        Assert.Contains(Perm.AdminGalleryView, PermissionCatalog.Codes);
        Assert.Contains(Perm.AdminGalleryManage, PermissionCatalog.Codes);
        Assert.Equal(Perm.AdminGalleryManage, Perm.ManageFor(Perm.AdminGalleryView));
    }
}
