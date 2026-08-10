# YasPortal

Clean rebuild of YasPortal.

Authorization model:
- No SuperAdmin role.
- No EMPLOYEE Identity role.
- Admin status is represented by Employee.IsAdmin.
- Permissions are assigned through User + Position.
- The active Position determines the permissions used for authorization.

The project will be rebuilt incrementally with the workflow engine as a core component.
