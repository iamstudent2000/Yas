-- YasPortal: permission to reopen a rejection you made yourself (inbox history). Idempotent; safe to run more than once.
-- The development database creates the permission row itself at startup; grant it to approvers from /admin/access.
-- Users must sign out and sign in again afterwards (permissions are snapshotted into the sign-in cookie).
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), N'Requests.Decision.ReopenOwn', N'بازگشایی درخواستی که خودم رد کرده‌ام'
WHERE NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = N'Requests.Decision.ReopenOwn');

-- Administrators hold permissions only through direct grants.
INSERT INTO EmployeePermissions (EmployeeId, PermissionId)
SELECT e.Id, p.Id
FROM Employees e CROSS JOIN Permissions p
WHERE e.IsAdmin = 1 AND p.Code = N'Requests.Decision.ReopenOwn'
  AND NOT EXISTS (SELECT 1 FROM EmployeePermissions x WHERE x.EmployeeId = e.Id AND x.PermissionId = p.Id);

COMMIT;
