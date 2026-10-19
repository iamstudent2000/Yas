using System.Text.Json;
using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>
/// Multi-part forms: part 1 is filled by the requester, every later part by the approver at the
/// workflow step that owns it. The Vehicle Request type is the first such form (part 2 at step 1,
/// part 3 at step 2).
/// </summary>
public class WorkflowMultiPartFormTests
{
    private const string RequesterJson =
        "{\"destination\":\"تبریز\",\"tripDate\":\"2026-11-01\",\"returnDate\":\"2026-11-02\",\"passengers\":\"3\",\"purpose\":\"بازدید\"}";

    private static Dictionary<string, string?> Part2(string plate = "12ب345") => new()
    {
        ["vehicleType"] = "سواری",
        ["plateNumber"] = plate,
        ["driverName"] = "علی",
    };

    private static Dictionary<string, string?> Part3() => new()
    {
        ["gatePassNumber"] = "G-100",
    };

    private static WorkflowRequest CreateVehicleRequest(out Guid step1Id, out Guid step2Id, out Guid step1Position)
    {
        step1Position = Guid.NewGuid();
        var request = new WorkflowRequest(
            WorkflowTypeCode.VehicleRequest,
            requesterEmployeeId: Guid.NewGuid(),
            requesterPositionId: Guid.NewGuid(),
            fieldValuesJson: RequesterJson,
            resolvedSteps: new[]
            {
                (Guid.NewGuid(), 1, "تخصیص خودرو", step1Position),
                (Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid()),
            });
        step1Id = request.Steps.First(x => x.Order == 1).Id;
        step2Id = request.Steps.First(x => x.Order == 2).Id;
        return request;
    }

    private static Dictionary<string, string?> Stored(WorkflowRequest request) =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson)!;

    [Fact]
    public void Vehicle_request_has_three_parts_and_each_field_belongs_to_one_of_them()
    {
        var parts = WorkflowFieldCatalog.GetParts(WorkflowTypeCode.VehicleRequest);
        Assert.Equal(new[] { 1, 2, 3 }, parts.Select(p => p.Part).ToArray());
        Assert.Null(parts[0].FillAtStepOrder);
        Assert.Equal(1, parts[1].FillAtStepOrder);
        Assert.Equal(2, parts[2].FillAtStepOrder);

        var all = WorkflowFieldCatalog.GetFields(WorkflowTypeCode.VehicleRequest);
        var perPart = parts.SelectMany(p => WorkflowFieldCatalog.GetFields(WorkflowTypeCode.VehicleRequest, p.Part)).ToList();
        Assert.Equal(all.Count, perPart.Count);
        Assert.Equal(all.Count, all.Select(f => f.Key).Distinct().Count());
    }

    [Fact]
    public void Ordinary_types_have_a_single_requester_part()
    {
        var part = Assert.Single(WorkflowFieldCatalog.GetParts(WorkflowTypeCode.Purchase));
        Assert.Equal(1, part.Part);
        Assert.False(WorkflowFieldCatalog.IsMultiPart(WorkflowTypeCode.Purchase));
        Assert.Equal(
            WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Purchase).Count,
            WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Purchase, 1).Count);
    }

    [Fact]
    public void Requester_values_are_valid_with_only_part_one_filled()
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(RequesterJson)!;
        Assert.Empty(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.VehicleRequest, values));
    }

    [Fact]
    public void Requester_cannot_submit_a_value_for_an_approvers_part()
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(RequesterJson)!;
        values["plateNumber"] = "12ب345";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.VehicleRequest, values));
        Assert.Equal("plateNumber", error.Key);
    }

    [Fact]
    public void Return_date_before_trip_date_is_refused()
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(RequesterJson)!;
        values["returnDate"] = "2026-10-30";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.VehicleRequest, values));
        Assert.Equal("returnDate", error.Key);
    }

    [Fact]
    public void A_steps_part_requires_its_own_required_fields_only()
    {
        var errors = WorkflowFieldCatalog.ValidatePartValues(WorkflowTypeCode.VehicleRequest, 2, new Dictionary<string, string?>());
        Assert.Equal(new[] { "driverName", "plateNumber", "vehicleType" }, errors.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());

        Assert.Empty(WorkflowFieldCatalog.ValidatePartValues(WorkflowTypeCode.VehicleRequest, 3, Part3()));
    }

    [Fact]
    public void Approving_saves_the_steps_part_and_records_who_filled_it_from_which_position()
    {
        var request = CreateVehicleRequest(out var step1Id, out _, out var step1Position);
        var approver = Guid.NewGuid();

        request.Approve(step1Id, approver, null, partValues: Part2());

        var stored = Stored(request);
        Assert.Equal("12ب345", stored["plateNumber"]);
        Assert.Equal("تبریز", stored["destination"]); // requester's part untouched

        var changes = request.FieldChanges.Where(c => c.FieldKey == "plateNumber").ToList();
        var change = Assert.Single(changes);
        Assert.Null(change.OldValue);
        Assert.Equal("12ب345", change.NewValue);
        Assert.Equal(approver, change.ChangedByEmployeeId);
        Assert.Equal(step1Position, change.ChangedByPositionId);
        Assert.Equal(1, change.Round);
        Assert.Equal(2, request.CurrentStepOrder);
    }

    [Fact]
    public void Approving_without_the_steps_required_part_is_refused_and_changes_nothing()
    {
        var request = CreateVehicleRequest(out var step1Id, out _, out _);

        Assert.Throws<ArgumentException>(() => request.Approve(step1Id, Guid.NewGuid(), null));
        Assert.Throws<ArgumentException>(() => request.Approve(step1Id, Guid.NewGuid(), null, partValues: new Dictionary<string, string?> { ["plateNumber"] = "x" }));

        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.First(x => x.Id == step1Id).Status);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Empty(request.FieldChanges);
        Assert.Empty(request.StepDecisions);
        Assert.Equal(RequesterJson, request.FieldValuesJson);
    }

    [Fact]
    public void A_step_cannot_write_into_another_steps_part()
    {
        var request = CreateVehicleRequest(out var step1Id, out _, out _);
        var values = Part2();
        values["gatePassNumber"] = "G-1"; // belongs to part 3, owned by step 2

        Assert.Throws<ArgumentException>(() => request.Approve(step1Id, Guid.NewGuid(), null, partValues: values));
        Assert.Empty(request.FieldChanges);
    }

    [Fact]
    public void A_step_that_owns_no_part_refuses_values()
    {
        var request = new WorkflowRequest(
            WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{\"startDate\":\"2026-10-01\"}",
            new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        Assert.Throws<InvalidOperationException>(() =>
            request.Approve(request.Steps.Single().Id, Guid.NewGuid(), null, partValues: new Dictionary<string, string?> { ["reason"] = "x" }));
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
    }

    [Fact]
    public void Every_part_filled_and_every_step_approved_completes_the_request()
    {
        var request = CreateVehicleRequest(out var step1Id, out var step2Id, out _);

        request.Approve(step1Id, Guid.NewGuid(), null, partValues: Part2());
        request.Approve(step2Id, Guid.NewGuid(), null, partValues: Part3());

        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
        var stored = Stored(request);
        Assert.Equal("G-100", stored["gatePassNumber"]);
        Assert.Null(stored["securityNote"]); // an optional field left blank is stored as null
    }

    [Fact]
    public void A_part_is_locked_once_its_step_approved_and_reopens_only_when_returned_to_that_step()
    {
        var request = CreateVehicleRequest(out var step1Id, out var step2Id, out _);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null, partValues: Part2("OLD"));

        // Step 1 has approved: it can no longer be acted on, so its part cannot be edited any more.
        Assert.Throws<InvalidOperationException>(() => request.Approve(step1Id, approver, null, partValues: Part2("NEW")));
        Assert.Equal("OLD", Stored(request)["plateNumber"]);

        // Step 2 returns the request to step 1, which fills its part again.
        request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), "پلاک اشتباه است");
        request.Approve(step1Id, approver, null, partValues: Part2("NEW"));

        Assert.Equal("NEW", Stored(request)["plateNumber"]);
        var changes = request.FieldChanges.Where(c => c.FieldKey == "plateNumber").OrderBy(c => c.ChangedAtUtc).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal("OLD", changes[1].OldValue);
        Assert.Equal("NEW", changes[1].NewValue);
    }

    [Fact]
    public void Resubmitting_may_change_the_requesters_part_but_keeps_the_approvers_part()
    {
        var request = CreateVehicleRequest(out var step1Id, out var step2Id, out _);
        request.Approve(step1Id, Guid.NewGuid(), null, partValues: Part2());
        request.ReturnToRequester(step2Id, Guid.NewGuid(), "مقصد را اصلاح کنید");

        var edited = Stored(request);
        edited["destination"] = "اصفهان";
        request.Resubmit(JsonSerializer.Serialize(edited), new[]
        {
            (Guid.NewGuid(), 1, "تخصیص خودرو", Guid.NewGuid()),
            (Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid()),
        });

        var stored = Stored(request);
        Assert.Equal("اصفهان", stored["destination"]);
        Assert.Equal("12ب345", stored["plateNumber"]);
        Assert.Equal(2, request.CurrentRound);
        Assert.Contains(request.FieldChanges, c => c.FieldKey == "destination" && c.OldValue == "تبریز" && c.NewValue == "اصفهان");
    }

    [Fact]
    public void Resubmitting_cannot_change_a_value_an_approver_filled()
    {
        var request = CreateVehicleRequest(out var step1Id, out var step2Id, out _);
        request.Approve(step1Id, Guid.NewGuid(), null, partValues: Part2());
        request.ReturnToRequester(step2Id, Guid.NewGuid(), null);

        var tampered = Stored(request);
        tampered["plateNumber"] = "HACKED";

        Assert.Throws<InvalidOperationException>(() => request.Resubmit(JsonSerializer.Serialize(tampered), new[]
        {
            (Guid.NewGuid(), 1, "تخصیص خودرو", Guid.NewGuid()),
            (Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid()),
        }));
        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
        Assert.Equal(1, request.CurrentRound);
        Assert.Equal("12ب345", Stored(request)["plateNumber"]);
    }

    [Fact]
    public void A_path_must_have_a_single_approver_at_each_part_owning_step()
    {
        WorkflowResolvedStep Step(int order) => new(Guid.NewGuid(), order, $"s{order}", Guid.NewGuid());

        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1), Step(2)]));

        // Only one step: part 3 (step 2) has nobody to fill it.
        var tooShort = Assert.Single(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1)]));
        Assert.Equal(3, tooShort.Part);

        // A parallel group at step 1: part 2 would have two possible fillers.
        var parallel = Assert.Single(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1), Step(1), Step(2)]));
        Assert.Equal(2, parallel.Part);

        // Ordinary types never have a problem.
        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.Leave, [Step(1)]));
    }
}
