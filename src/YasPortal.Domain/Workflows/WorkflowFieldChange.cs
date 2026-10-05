namespace YasPortal.Domain.Workflows;

/// <summary>
/// A permanent record of one request field changing value (spec §11.3): which field, the old
/// and new value, who changed it, from which position, and when. Rows are only ever appended —
/// they are never updated or deleted — so <see cref="WorkflowRequest.FieldValuesJson"/> (which
/// always holds just the latest values) can never silently destroy what a field used to say.
///
/// Today the only way field values change after submission is a requester editing a returned
/// request and resubmitting it, so each row belongs to the <see cref="Round"/> that edit opened.
/// </summary>
public sealed class WorkflowFieldChange
{
    private WorkflowFieldChange()
    {
    }

    internal WorkflowFieldChange(
        Guid requestId,
        int round,
        string fieldKey,
        string? oldValue,
        string? newValue,
        Guid changedByEmployeeId,
        Guid changedByPositionId)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request is required.", nameof(requestId));
        if (round < 1)
            throw new ArgumentOutOfRangeException(nameof(round), "Round must start at 1.");
        if (string.IsNullOrWhiteSpace(fieldKey))
            throw new ArgumentException("Field key is required.", nameof(fieldKey));
        if (changedByEmployeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(changedByEmployeeId));
        if (changedByPositionId == Guid.Empty)
            throw new ArgumentException("Position is required.", nameof(changedByPositionId));

        RequestId = requestId;
        Round = round;
        FieldKey = fieldKey;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedByEmployeeId = changedByEmployeeId;
        ChangedByPositionId = changedByPositionId;
        ChangedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }

    /// <summary>The round the edit opened (the round that was started by the resubmission carrying this change).</summary>
    public int Round { get; private set; }

    /// <summary>The field's stable key from <see cref="WorkflowFieldCatalog"/> (e.g. "startDate").</summary>
    public string FieldKey { get; private set; } = null!;

    /// <summary>Null when the field had no value before (it was added, or left blank).</summary>
    public string? OldValue { get; private set; }

    /// <summary>Null when the field was cleared.</summary>
    public string? NewValue { get; private set; }
    public Guid ChangedByEmployeeId { get; private set; }
    public Guid ChangedByPositionId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
}
