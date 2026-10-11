using YasPortal.Domain.Workflows;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// The request-list filters of spec §25.2 (status, request type, requester, date range, current
/// step) plus the request-ID search of §25.3, applied in the database so a list never loads more
/// than one page. Every field is optional; blank means "no filter".
/// </summary>
public sealed class WorkflowRequestFilter
{
    public WorkflowRequestStatus? Status { get; set; }
    public WorkflowTypeCode? Type { get; set; }

    /// <summary>Part of the requester's full name.</summary>
    public string? RequesterName { get; set; }

    /// <summary>Part of the name of the step the request is currently at.</summary>
    public string? StepName { get; set; }

    /// <summary>
    /// The request ID, or the first characters of it. The short ID shown on request cards and in
    /// notifications is the first 8 characters, so that works here too; a leading '#' is ignored.
    /// </summary>
    public string? IdText { get; set; }

    /// <summary>First day to include (the whole day).</summary>
    public DateTime? From { get; set; }

    /// <summary>Last day to include (the whole day).</summary>
    public DateTime? To { get; set; }

    public bool IsEmpty =>
        Status is null && Type is null && From is null && To is null
        && string.IsNullOrWhiteSpace(RequesterName) && string.IsNullOrWhiteSpace(StepName) && string.IsNullOrWhiteSpace(IdText);

    public void Clear()
    {
        Status = null;
        Type = null;
        RequesterName = null;
        StepName = null;
        IdText = null;
        From = null;
        To = null;
    }

    public IQueryable<WorkflowRequest> Apply(IQueryable<WorkflowRequest> query, ApplicationDbContext db)
    {
        if (Status is WorkflowRequestStatus status)
            query = query.Where(x => x.Status == status);
        if (Type is WorkflowTypeCode type)
            query = query.Where(x => x.WorkflowType == type);
        if (From is DateTime from)
        {
            var start = from.Date;
            query = query.Where(x => x.CreatedAtUtc >= start);
        }
        if (To is DateTime to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(x => x.CreatedAtUtc < end);
        }
        if (!string.IsNullOrWhiteSpace(RequesterName))
        {
            var name = RequesterName.Trim();
            query = query.Where(x => db.Employees.Any(e => e.Id == x.RequesterEmployeeId && e.FullName.Contains(name)));
        }
        if (!string.IsNullOrWhiteSpace(StepName))
        {
            var step = StepName.Trim();
            query = query.Where(x => x.Steps.Any(s => s.Round == x.CurrentRound && s.Order == x.CurrentStepOrder && s.Name.Contains(step)));
        }
        if (!string.IsNullOrWhiteSpace(IdText))
        {
            var id = IdText.Trim().TrimStart('#').ToLowerInvariant();
            if (id.Length > 0)
                query = query.Where(x => x.Id.ToString().ToLower().StartsWith(id));
        }
        return query;
    }
}
