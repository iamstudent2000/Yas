using System.Linq.Expressions;
using YasPortal.Domain.Workflows;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// Server-side ordering for the request lists, so sorting happens in SQL before paging and
/// never reorders just the rows of the current page. Unknown keys fall back to newest first.
/// Every ordering ends with the request ID so rows with equal keys keep a stable page order.
/// </summary>
public static class WorkflowRequestSort
{
    public const string Id = "id";
    public const string Type = "type";
    public const string Requester = "requester";
    public const string Position = "position";
    public const string Status = "status";
    public const string Created = "created";
    public const string RejectedAt = "rejectedAt";

    public static IQueryable<WorkflowRequest> Apply(IQueryable<WorkflowRequest> query, string? sortBy, bool descending, ApplicationDbContext db)
    {
        IOrderedQueryable<WorkflowRequest> ordered = sortBy switch
        {
            Id => descending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
            Type => Order(query, TypeRank(), descending),
            Requester => descending
                ? query.OrderByDescending(x => db.Employees.Where(e => e.Id == x.RequesterEmployeeId).Select(e => e.FullName).FirstOrDefault())
                : query.OrderBy(x => db.Employees.Where(e => e.Id == x.RequesterEmployeeId).Select(e => e.FullName).FirstOrDefault()),
            Position => descending
                ? query.OrderByDescending(x => db.Positions.Where(p => p.Id == x.RequesterPositionId).Select(p => p.Name).FirstOrDefault())
                : query.OrderBy(x => db.Positions.Where(p => p.Id == x.RequesterPositionId).Select(p => p.Name).FirstOrDefault()),
            Status => descending ? query.OrderByDescending(x => x.Status) : query.OrderBy(x => x.Status),
            // The date of the rejection in the current round; the same value the rejected-requests list shows.
            RejectedAt => descending
                ? query.OrderByDescending(x => x.StepDecisions.Where(d => d.Round == x.CurrentRound && d.Outcome == WorkflowStepStatus.Rejected).Max(d => (DateTime?)d.ActedAtUtc))
                : query.OrderBy(x => x.StepDecisions.Where(d => d.Round == x.CurrentRound && d.Outcome == WorkflowStepStatus.Rejected).Max(d => (DateTime?)d.ActedAtUtc)),
            Created => descending ? query.OrderByDescending(x => x.CreatedAtUtc) : query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static IOrderedQueryable<WorkflowRequest> Order(IQueryable<WorkflowRequest> query, Expression<Func<WorkflowRequest, int>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);

    /// <summary>
    /// Request types are stored as an enum, but people expect them ordered by the name they see.
    /// This builds a CASE expression that maps each type to its position in the name-sorted list.
    /// </summary>
    private static Expression<Func<WorkflowRequest, int>> TypeRank()
    {
        var request = Expression.Parameter(typeof(WorkflowRequest), "x");
        var type = Expression.Property(request, nameof(WorkflowRequest.WorkflowType));
        var names = WorkflowFieldCatalog.AllTypes
            .OrderBy(t => WorkflowFieldCatalog.GetDisplayName(t), StringComparer.Create(new System.Globalization.CultureInfo("fa-IR"), ignoreCase: false))
            .ToList();
        Expression body = Expression.Constant(names.Count);
        for (var rank = names.Count - 1; rank >= 0; rank--)
            body = Expression.Condition(Expression.Equal(type, Expression.Constant(names[rank])), Expression.Constant(rank), body);
        return Expression.Lambda<Func<WorkflowRequest, int>>(body, request);
    }
}
