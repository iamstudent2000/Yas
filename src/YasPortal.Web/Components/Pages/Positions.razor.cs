using Microsoft.AspNetCore.Components;

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

        _employeeToAssign = employeeId;
        _employeeToAssignName = employee.FullName;
    }
}
