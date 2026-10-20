using System.Text.Json;
using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>
/// Multi-part forms: part 1 is filled by the requester, every later part by the approver at the
/// workflow step an admin assigned it to (<see cref="WorkflowStepDefinition.FillsPart"/>). The Vehicle
/// Request type is the first such form (three parts).
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
                new WorkflowResolvedStep(Guid.NewGuid(), 1, "تخصیص خودرو", step1Position, FillsPart: 2),
                new WorkflowResolvedStep(Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid(), FillsPart: 3),
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
            new WorkflowResolvedStep(Guid.NewGuid(), 1, "تخصیص خودرو", Guid.NewGuid(), FillsPart: 2),
            new WorkflowResolvedStep(Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid(), FillsPart: 3),
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
            new WorkflowResolvedStep(Guid.NewGuid(), 1, "تخصیص خودرو", Guid.NewGuid(), FillsPart: 2),
            new WorkflowResolvedStep(Guid.NewGuid(), 2, "ثبت حراست", Guid.NewGuid(), FillsPart: 3),
        }));
        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
        Assert.Equal(1, request.CurrentRound);
        Assert.Equal("12ب345", Stored(request)["plateNumber"]);
    }

    private static WorkflowResolvedStep Step(int order, int? fills = null) =>
        new(Guid.NewGuid(), order, $"s{order}", Guid.NewGuid(), FillsPart: fills);

    [Fact]
    public void Every_part_needs_exactly_one_step_assigned_to_it()
    {
        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1, 2), Step(2, 3)]));

        // Which step fills which part is the admin's choice: the reverse assignment and a longer path are fine.
        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1, 3), Step(2), Step(3, 2)]));

        // Nothing assigned: both parts are unfillable.
        var none = WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1), Step(2)]);
        Assert.Equal(new[] { 2, 3 }, none.Select(p => p.Part).ToArray());

        // One part assigned to two steps.
        var twice = Assert.Single(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1, 2), Step(2, 2), Step(3, 3)]));
        Assert.Equal(2, twice.Part);

        // Ordinary types never have a problem.
        Assert.Empty(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.Leave, [Step(1)]));
    }

    [Fact]
    public void A_step_in_a_parallel_group_cannot_fill_a_part()
    {
        // Two steps share order 1, one of them assigned part 2: who would fill it is unclear.
        var parallel = WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, [Step(1, 2), Step(1), Step(2, 3)]);
        Assert.Equal(2, Assert.Single(parallel).Part);
    }

    [Fact]
    public void Assignment_is_copied_onto_the_requests_steps_and_decides_who_fills_what()
    {
        // Swapped relative to the default: step 1 fills part 3, step 2 fills part 2.
        var request = new WorkflowRequest(
            WorkflowTypeCode.VehicleRequest, Guid.NewGuid(), Guid.NewGuid(), RequesterJson,
            new[]
            {
                new WorkflowResolvedStep(Guid.NewGuid(), 1, "حراست", Guid.NewGuid(), FillsPart: 3),
                new WorkflowResolvedStep(Guid.NewGuid(), 2, "تخصیص", Guid.NewGuid(), FillsPart: 2),
            });
        var first = request.Steps.Single(x => x.Order == 1);
        var second = request.Steps.Single(x => x.Order == 2);
        Assert.Equal(3, first.FillsPart);
        Assert.Equal(2, second.FillsPart);

        // Step 1 owns part 3, so part 2's values are refused there.
        Assert.Throws<ArgumentException>(() => request.Approve(first.Id, Guid.NewGuid(), null, partValues: Part2()));
        request.Approve(first.Id, Guid.NewGuid(), null, partValues: Part3());
        request.Approve(second.Id, Guid.NewGuid(), null, partValues: Part2());

        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
        Assert.Equal("G-100", Stored(request)["gatePassNumber"]);
        Assert.Equal("12ب345", Stored(request)["plateNumber"]);
    }

    [Fact]
    public void A_step_with_no_assigned_part_just_approves_and_refuses_values()
    {
        var request = new WorkflowRequest(
            WorkflowTypeCode.VehicleRequest, Guid.NewGuid(), Guid.NewGuid(), RequesterJson,
            new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "تایید", Guid.NewGuid()) });
        var step = request.Steps.Single();

        Assert.Throws<InvalidOperationException>(() => request.Approve(step.Id, Guid.NewGuid(), null, partValues: Part2()));
        request.Approve(step.Id, Guid.NewGuid(), null);
        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void A_step_definition_can_only_be_set_to_fill_a_real_later_part()
    {
        var step = new WorkflowStepDefinition(WorkflowTypeCode.VehicleRequest, 1, "تخصیص", 1);
        Assert.Null(step.FillsPart);

        step.SetFillsPart(2);
        Assert.Equal(2, step.FillsPart);
        step.SetFillsPart(3);
        Assert.Equal(3, step.FillsPart);
        step.SetFillsPart(null);
        Assert.Null(step.FillsPart);

        Assert.Throws<ArgumentOutOfRangeException>(() => step.SetFillsPart(1)); // part 1 is the requester's
        Assert.Throws<ArgumentOutOfRangeException>(() => step.SetFillsPart(4)); // no such part

        var leave = new WorkflowStepDefinition(WorkflowTypeCode.Leave, 1, "مدیر", 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => leave.SetFillsPart(2)); // single-part type
    }

    [Fact]
    public void The_resolver_carries_the_assignment_and_an_escalation_cannot_hold_a_part()
    {
        var requester = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var top = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [requester] = manager, [manager] = top, [top] = null };

        var fixedStep = new WorkflowStepDefinition(WorkflowTypeCode.VehicleRequest, 1, "ثابت", top);
        fixedStep.SetFillsPart(2);
        var ok = WorkflowStepResolver.Resolve(new[] { fixedStep }, requester, parents);
        Assert.True(ok.Success);
        Assert.Equal(2, Assert.Single(ok.Steps).FillsPart);

        var escalating = WorkflowStepDefinition.EscalatingToTreeLevel(WorkflowTypeCode.VehicleRequest, 1, "پله‌به‌پله", 1);
        escalating.SetFillsPart(2);
        var resolved = WorkflowStepResolver.Resolve(new[] { escalating }, requester, parents);
        Assert.True(resolved.Success);
        Assert.True(resolved.Steps.Count > 1);
        // The expansion carries the part on every step, so the path is refused at submission.
        Assert.Contains(WorkflowFieldCatalog.UnfillableParts(WorkflowTypeCode.VehicleRequest, resolved.Steps), p => p.Part == 2);
    }
}
