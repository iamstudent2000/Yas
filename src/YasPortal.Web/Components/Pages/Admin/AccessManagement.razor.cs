using Microsoft.AspNetCore.Components;

namespace YasPortal.Web.Components.Pages.Admin;

public partial class AccessManagement
{
    [SupplyParameterFromQuery(Name = "employee")]
    public Guid? EmployeeQuery
    {
        get; set;
    }

    private Guid? _lastAppliedEmployeeQuery;

    protected override async Task OnParametersSetAsync()
    {
        if (EmployeeQuery is not Guid employeeId || employeeId == _lastAppliedEmployeeQuery)
            return;

        _lastAppliedEmployeeQuery = employeeId;
        await EmployeeChanged(employeeId);
    }
}
