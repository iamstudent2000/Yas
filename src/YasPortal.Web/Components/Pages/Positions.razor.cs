using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace YasPortal.Web.Components.Pages;

public partial class Positions
{
    [SupplyParameterFromQuery(Name = "employee")]
    public Guid? EmployeeQuery { get; set; }

    // Optional when an administrator wants to open one exact position for a user
    // who has more than one active position.
    [SupplyParameterFromQuery(Name = "position")]
    public Guid? PositionQuery { get; set; }

    private Guid? _lastAppliedEmployeeQuery;
    private Guid? _lastAppliedPositionQuery;

    protected override async Task OnParametersSetAsync()
    {
        if (EmployeeQuery is not Guid employeeId)
            return;

        if (employeeId == _lastAppliedEmployeeQuery && PositionQuery == _lastAppliedPositionQuery)
            return;

        _lastAppliedEmployeeQuery = employeeId;
        _lastAppliedPositionQuery = PositionQuery;

        var employee = await AdminQueries.GetEmployeeAsync(employeeId);
        if (employee is null || employee.IsAdmin)
            return;

        await using var db = await DbFactory.CreateDbContextAsync();

        var activeAssignments = await db.EmployeePositions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.EndedAt == null)
            .Select(x => new
            {
                x.PositionId,
                x.Employee.LastActivePositionId
            })
            .ToListAsync();

        var activePositionIds = activeAssignments
            .Select(x => x.PositionId)
            .ToHashSet();

        // Prefer an explicitly supplied position. This is useful for admin links
        // such as /admin/positions?employee=...&position=....
        // Otherwise use the employee's last active position. This avoids the old
        // FirstOrDefault() ambiguity when a user has multiple active positions.
        Guid? selectedPositionId = null;

        if (PositionQuery is Guid requestedPositionId && activePositionIds.Contains(requestedPositionId))
        {
            selectedPositionId = requestedPositionId;
        }
        else if (employee.LastActivePositionId is Guid lastPositionId && activePositionIds.Contains(lastPositionId))
        {
            selectedPositionId = lastPositionId;
        }
        else if (activePositionIds.Count == 1)
        {
            selectedPositionId = activePositionIds.Single();
        }

        if (selectedPositionId is Guid selected && _positions.Any(p => p.Id == selected))
        {
            _selectedPositionId = selected;
            _employeeToAssign = null;
            _employeeToAssignName = "";

            // Make the selected position visible even when its branch is collapsed.
            var current = _positions.FirstOrDefault(x => x.Id == selected);
            while (current?.ParentPositionId is Guid parentId)
            {
                _collapsedNodeIds.Remove(parentId);
                current = _positions.FirstOrDefault(x => x.Id == parentId);
            }

            await LoadSelectedAssignmentAsync(selected);
        }
        else
        {
            _selectedPositionId = null;
            _selectedPositionEmployeeName = null;

            // The employee remains preselected for assignment. The admin can now
            // choose any position in the tree without losing the target employee.
            _employeeToAssign = employeeId;
            _employeeToAssignName = employee.FullName;
        }

        await InvokeAsync(StateHasChanged);
    }
}
