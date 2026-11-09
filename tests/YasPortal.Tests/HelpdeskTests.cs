using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>Helpdesk requests: the richer form, the support-action part, the waiting time and the attachment rules.</summary>
public class HelpdeskTests
{
    private const string Json = "{\"subject\":\"چاپگر کار نمی‌کند\",\"category\":\"چاپگر و اسکنر\",\"priority\":\"زیاد\",\"description\":\"خطا\"}";

    private static Dictionary<string, string?> Part1() => new()
    {
        ["subject"] = "اینترنت قطع است",
        ["category"] = "شبکه و اینترنت",
        ["priority"] = "بحرانی",
        ["description"] = "از صبح قطع است",
    };

    private static WorkflowRequest CreateRequest(string json = Json, int? fillsPart = 2) =>
        new(WorkflowTypeCode.Helpdesk, Guid.NewGuid(), Guid.NewGuid(), json,
            new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "رسیدگی پشتیبانی", Guid.NewGuid(), FillsPart: fillsPart) });

    [Fact]
    public void Form_has_a_requester_part_and_a_support_action_part()
    {
        Assert.True(WorkflowFieldCatalog.IsMultiPart(WorkflowTypeCode.Helpdesk));
        Assert.Equal(new[] { 1, 2 }, WorkflowFieldCatalog.GetParts(WorkflowTypeCode.Helpdesk).Select(p => p.Part));
        Assert.Contains(WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Helpdesk, 2), f => f.Key == "resolution" && f.Required);
        Assert.All(new[] { "category", "affectedSystem", "location", "contactPhone" },
            key => Assert.Contains(WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Helpdesk, 1), f => f.Key == key));
    }

    [Fact]
    public void Requester_part_needs_category_but_not_the_optional_details()
    {
        Assert.Empty(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Helpdesk, Part1()));

        var values = Part1();
        values.Remove("category");
        Assert.Equal("category", Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Helpdesk, values)).Key);

        values["category"] = "چیز دیگر";
        Assert.Equal("category", Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Helpdesk, values)).Key);
    }

    [Fact]
    public void Requester_cannot_fill_the_support_part()
    {
        var values = Part1();
        values["resolution"] = "حل شد";
        Assert.Equal("resolution", Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Helpdesk, values)).Key);
    }

    [Fact]
    public void Support_part_needs_the_resolution_and_a_valid_time()
    {
        Assert.Equal("resolution", Assert.Single(WorkflowFieldCatalog.ValidatePartValues(WorkflowTypeCode.Helpdesk, 2, new Dictionary<string, string?>())).Key);
        Assert.Empty(WorkflowFieldCatalog.ValidatePartValues(WorkflowTypeCode.Helpdesk, 2, new Dictionary<string, string?> { ["resolution"] = "کابل تعویض شد", ["timeSpentMinutes"] = "30" }));
        Assert.Equal("timeSpentMinutes", Assert.Single(WorkflowFieldCatalog.ValidatePartValues(WorkflowTypeCode.Helpdesk, 2,
            new Dictionary<string, string?> { ["resolution"] = "x", ["timeSpentMinutes"] = "-5" })).Key);
    }

    [Fact]
    public void A_path_without_a_step_for_the_support_part_cannot_be_submitted()
    {
        var withPart = new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "s", Guid.NewGuid(), FillsPart: 2) };
        var without = new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "s", Guid.NewGuid()) };
        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.Helpdesk, withPart));
        Assert.Equal(2, Assert.Single(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.Helpdesk, without)).Part);
    }

    [Fact]
    public void Approving_needs_the_resolution_and_stores_it()
    {
        var request = CreateRequest();
        var stepId = request.Steps.Single().Id;

        Assert.ThrowsAny<Exception>(() => request.Approve(stepId, Guid.NewGuid(), null));
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);

        request.Approve(stepId, Guid.NewGuid(), null, partValues: new Dictionary<string, string?> { ["resolution"] = "کابل تعویض شد", ["timeSpentMinutes"] = "20" });
        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
        Assert.Equal("کابل تعویض شد", System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson)!["resolution"]);
    }

    [Fact]
    public void Pending_request_reports_how_long_it_has_waited()
    {
        var request = CreateRequest();
        var since = request.RequesterActions.Single().AtUtc;   // the clock starts at the submission record

        var status = HelpdeskWaiting.For(request, since.AddHours(26));
        Assert.NotNull(status);
        Assert.Equal(TimeSpan.FromHours(26), status!.Waited);
        Assert.Equal("زیاد", status.Priority);
        Assert.Equal(since, status.SinceUtc);
    }

    [Fact]
    public void Waiting_time_is_never_negative_and_works_without_a_priority()
    {
        var request = CreateRequest("{}");
        var since = request.RequesterActions.Single().AtUtc;
        var status = HelpdeskWaiting.For(request, since.AddMinutes(-5));
        Assert.Equal(TimeSpan.Zero, status!.Waited);
        Assert.Null(status.Priority);
    }

    [Fact]
    public void Waiting_time_applies_only_to_pending_helpdesk_requests()
    {
        var request = CreateRequest();
        request.Approve(request.Steps.Single().Id, Guid.NewGuid(), null, partValues: new Dictionary<string, string?> { ["resolution"] = "انجام شد" });
        Assert.Null(HelpdeskWaiting.For(request, DateTime.UtcNow));

        var leave = new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{\"priority\":\"زیاد\"}",
            new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "s", Guid.NewGuid()) });
        Assert.Null(HelpdeskWaiting.For(leave, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("کم", 1)]
    [InlineData("متوسط", 2)]
    [InlineData("زیاد", 3)]
    [InlineData("بحرانی", 4)]
    [InlineData(" زیاد ", 3)]
    [InlineData("فوری", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Priority_label_maps_to_a_rank(string? label, int? rank)
    {
        Assert.Equal(rank, WorkflowPriority.RankOf(label));
        if (rank is not null)
            Assert.Equal(label!.Trim(), WorkflowPriority.LabelOf(rank));
    }

    [Fact]
    public void Request_stores_the_rank_of_its_priority_and_none_for_types_without_one()
    {
        Assert.Equal(3, CreateRequest().PriorityRank);   // "زیاد"
        Assert.Null(CreateRequest("{}").PriorityRank);
        Assert.Null(CreateRequest("not json").PriorityRank);

        var leave = new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{\"reason\":\"x\"}",
            new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "s", Guid.NewGuid()) });
        Assert.Null(leave.PriorityRank);
    }

    [Fact]
    public void Catalog_priority_options_are_the_shared_labels()
    {
        var field = WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Helpdesk, 1).Single(f => f.Key == WorkflowPriority.FieldKey);
        Assert.Equal(WorkflowPriority.Labels, field.Options);
    }

    [Fact]
    public void Only_helpdesk_takes_attachments()
    {
        Assert.True(WorkflowFieldCatalog.SupportsAttachments(WorkflowTypeCode.Helpdesk));
        Assert.False(WorkflowFieldCatalog.SupportsAttachments(WorkflowTypeCode.Leave));
    }

    [Theory]
    [InlineData("screen.png", 1000, true)]
    [InlineData("report.PDF", 1000, true)]
    [InlineData("page.html", 1000, false)]
    [InlineData("image.svg", 1000, false)]
    [InlineData("run.exe", 1000, false)]
    [InlineData("noext", 1000, false)]
    [InlineData("empty.png", 0, false)]
    [InlineData("big.png", WorkflowRequestAttachment.MaxBytes + 1, false)]
    public void Attachment_type_and_size_rules(string name, long size, bool ok) =>
        Assert.Equal(ok, WorkflowRequestAttachment.Validate(name, size) is null);

    [Fact]
    public void Attachment_keeps_only_the_file_name_and_the_matching_content_type()
    {
        var attachment = new WorkflowRequestAttachment(Guid.NewGuid(), Guid.NewGuid(), "C:\\Users\\ali\\Desktop\\خطا.PNG", new byte[] { 1, 2, 3 });
        Assert.Equal("خطا.PNG", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(3, attachment.SizeBytes);
        Assert.Throws<ArgumentException>(() => new WorkflowRequestAttachment(Guid.NewGuid(), Guid.NewGuid(), "x.html", new byte[] { 1 }));
    }
}
