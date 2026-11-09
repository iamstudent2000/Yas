using System.Text.Json;

namespace YasPortal.Domain.Workflows;

/// <summary>
/// The priority levels a form can ask for with a field keyed "priority", and their rank (1 = lowest). A request stores the rank in
/// its own column (<see cref="WorkflowRequest.PriorityRank"/>), so lists can sort and filter by it in the database. Request types
/// without a priority field, and requests whose value is not one of these labels, have no rank (null).
/// </summary>
public static class WorkflowPriority
{
    public const string FieldKey = "priority";

    /// <summary>Labels from lowest to highest; the position plus one is the rank.</summary>
    public static readonly IReadOnlyList<string> Labels = new[] { "کم", "متوسط", "زیاد", "بحرانی" };

    public static int? RankOf(string? label)
    {
        var index = label is null ? -1 : Labels.ToList().IndexOf(label.Trim());
        return index < 0 ? null : index + 1;
    }

    public static string? LabelOf(int? rank) => rank is >= 1 and <= 4 ? Labels[rank.Value - 1] : null;

    /// <summary>The rank in a request's stored field values, or null when there is none.</summary>
    public static int? FromFieldValues(string? fieldValuesJson)
    {
        if (string.IsNullOrWhiteSpace(fieldValuesJson))
            return null;
        try
        {
            return RankOf(JsonSerializer.Deserialize<Dictionary<string, string?>>(fieldValuesJson)?.GetValueOrDefault(FieldKey));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
