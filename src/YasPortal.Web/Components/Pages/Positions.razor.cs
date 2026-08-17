using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace YasPortal.Web.Components.Pages;

public partial class Positions
{
    [SupplyParameterFromQuery(Name = "employee")]
    public Guid? EmployeeQuery { get; set; }

    private Guid? _lastAppliedEmployeeQuery;

    protected override async Task OnParametersSetAsync()
    {
        if (EmployeeQuery is not Guid employeeId || employeeId == _lastAppliedEmployeeQuery)
            return;

        _lastAppliedEmployeeQuery = employeeId;

        var employee = await AdminQueries.GetEmployeeAsync(employeeId);
        if (employee is null || employee.IsAdmin)
            return;

        await using var db = await DbFactory.CreateDbContextAsync();

        var positionIds = await db.EmployeePositions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.EndedAt == null)
            .Select(x => x.PositionId)
            .ToListAsync();

        var selectedPositionId = positionIds.FirstOrDefault(id => _positions.Any(p => p.Id == id));
        if (selectedPositionId != Guid.Empty)
        {
            _selectedPositionId = selectedPositionId;
            _employeeToAssign = null;
            _employeeToAssignName = "";

            var current = _positions.FirstOrDefault(x => x.Id == selectedPositionId);
            while (current?.ParentPositionId is Guid parentId)
            {
                _collapsedNodeIds.Remove(parentId);
                current = _positions.FirstOrDefault(x => x.Id == parentId);
            }

            await LoadSelectedAssignmentAsync(selectedPositionId);
        }
        else
        {
            _selectedPositionId = null;
            _employeeToAssign = employeeId;
            _employeeToAssignName = employee.FullName;
        }
    }
}
