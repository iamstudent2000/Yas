using YasPortal.Domain.Guides;
using Xunit;

namespace YasPortal.Tests;

// Guide attachment tests: which files are accepted (type, size, name), the limit on the number of files, name cleaning and the stored details.
public sealed class UserGuideAttachmentTests
{
    [Theory]
    [InlineData("form.pdf")]
    [InlineData("PHOTO.JPG")]
    [InlineData("sheet.xlsx")]
    [InlineData("archive.zip")]
    public void Allowed_types_are_accepted_whatever_the_letter_case(string name) =>
        Assert.Null(UserGuideAttachment.Validate(name, 100));

    [Theory]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("run.exe")]
    [InlineData("noextension")]
    [InlineData("")]
    public void Types_a_browser_could_run_or_unknown_types_are_refused(string name) =>
        Assert.NotNull(UserGuideAttachment.Validate(name, 100));

    [Fact]
    public void Empty_and_too_big_files_are_refused()
    {
        Assert.NotNull(UserGuideAttachment.Validate("a.pdf", 0));
        Assert.Null(UserGuideAttachment.Validate("a.pdf", UserGuideAttachment.MaxBytes));
        Assert.NotNull(UserGuideAttachment.Validate("a.pdf", UserGuideAttachment.MaxBytes + 1));
        Assert.NotNull(UserGuideAttachment.Validate(new string('x', UserGuideAttachment.FileNameMax) + ".pdf", 10));
    }

    [Fact]
    public void A_guide_may_carry_at_most_the_allowed_number_of_files()
    {
        Assert.Null(UserGuideAttachment.ValidateCount(UserGuideAttachment.MaxPerGuide));
        Assert.NotNull(UserGuideAttachment.ValidateCount(UserGuideAttachment.MaxPerGuide + 1));
    }

    [Fact]
    public void Folder_parts_and_quotes_are_removed_from_the_name()
    {
        Assert.Equal("form.pdf", UserGuideAttachment.CleanName(@"C:\Users\me\form.pdf"));
        Assert.Equal("form.pdf", UserGuideAttachment.CleanName("../../etc/\"form\".pdf"));
    }

    [Fact]
    public void Attachment_keeps_the_bytes_type_and_guide()
    {
        var guideId = Guid.NewGuid();
        var file = new UserGuideAttachment(guideId, @"D:\x\نمونه.PDF", [1, 2, 3]);
        Assert.Equal(guideId, file.GuideId);
        Assert.Equal("نمونه.PDF", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(3, file.SizeBytes);
        Assert.Equal<byte[]>([1, 2, 3], file.Content);
        Assert.NotEqual(Guid.Empty, file.Id);
        Assert.Throws<ArgumentException>(() => new UserGuideAttachment(guideId, "a.exe", [1]));
    }
}
