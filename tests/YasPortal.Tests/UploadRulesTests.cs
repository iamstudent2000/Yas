using YasPortal.Domain.Files;
using YasPortal.Domain.Guides;
using YasPortal.Domain.Landing;
using YasPortal.Domain.Workflows;
using Xunit;

namespace YasPortal.Tests;

// The upload rules are written once (UploadRules); every kind of attachment follows the same rules.
public sealed class UploadRulesTests
{
    [Fact]
    public void Every_kind_of_attachment_accepts_and_refuses_the_same_files()
    {
        // a PowerPoint file is accepted everywhere (request attachments used to refuse it)
        Assert.NotNull(new WorkflowRequestAttachment(Guid.NewGuid(), Guid.NewGuid(), "talk.pptx", [1]));
        Assert.NotNull(new UserGuideAttachment(Guid.NewGuid(), "talk.pptx", [1]));
        Assert.NotNull(new HomeSlideAttachment(Guid.NewGuid(), "talk.pptx", [1]));
        // a script is refused everywhere
        Assert.Throws<ArgumentException>(() => new WorkflowRequestAttachment(Guid.NewGuid(), Guid.NewGuid(), "x.html", [1]));
        Assert.Throws<ArgumentException>(() => new UserGuideAttachment(Guid.NewGuid(), "x.html", [1]));
        Assert.Throws<ArgumentException>(() => new HomeSlideAttachment(Guid.NewGuid(), "x.html", [1]));
    }

    [Fact]
    public void Size_limit_is_checked_in_one_place()
    {
        Assert.Null(UploadRules.Validate("a.pdf", UploadRules.MaxBytes));
        Assert.NotNull(UploadRules.Validate("a.pdf", UploadRules.MaxBytes + 1));
    }
}
