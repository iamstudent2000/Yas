using Microsoft.AspNetCore.Components;

namespace YasPortal.Web.Components.Pages.Admin;

// Code-behind for the access screen: lets other pages open it for a given employee via the web address.
// For example a link from the users list can end with ?employee=ID so that person is preselected.
public partial class AccessManagement
{
    // Optional address parameter named employee holding the person to preselect.
    [SupplyParameterFromQuery(Name = "employee")]
    public Guid? EmployeeQuery
    {
        get; set;
    }

    // Remembers the last employee applied so the same selection is not reloaded repeatedly.
    private Guid? _lastAppliedEmployeeQuery;

    // Runs whenever the address changes: if an employee id is present and new, select that employee.
    protected override async Task OnParametersSetAsync()
    {
        if (EmployeeQuery is not Guid employeeId || employeeId == _lastAppliedEmployeeQuery)
            return;

        _lastAppliedEmployeeQuery = employeeId;
        await EmployeeChanged(employeeId);
    }
}
