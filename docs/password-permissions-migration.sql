-- YasPortal: password permissions. Idempotent; safe to run more than once.
-- Adds Admin.PasswordPolicy.View / Admin.PasswordPolicy.Manage (the password policy page) and Admin.Users.PasswordSet (setting another
-- user's password on the users page). Everyone who holds Admin.Users.Manage today (directly, through a position, or through a group)
-- is given all three, so nobody loses what they could do. The development database creates the permission rows itself at startup.
-- Users must sign out and sign in again afterwards (permissions are snapshotted into the sign-in cookie).
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.PasswordPolicy.View', N'مشاهده سیاست رمز عبور (فقط خواندنی)'),
    (N'Admin.PasswordPolicy.Manage', N'مدیریت سیاست رمز عبور (ویرایش قوانین)'),
    (N'Admin.Users.PasswordSet', N'تعیین یا تغییر رمز عبور سایر کاربران')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

DECLARE @old uniqueidentifier = (SELECT Id FROM Permissions WHERE Code = N'Admin.Users.Manage');

IF @old IS NOT NULL
BEGIN
    INSERT INTO EmployeePermissions (EmployeeId, PermissionId)
    SELECT x.EmployeeId, n.Id
    FROM EmployeePermissions x CROSS JOIN Permissions n
    WHERE x.PermissionId = @old AND n.Code IN (N'Admin.PasswordPolicy.View', N'Admin.PasswordPolicy.Manage', N'Admin.Users.PasswordSet')
      AND NOT EXISTS (SELECT 1 FROM EmployeePermissions y WHERE y.EmployeeId = x.EmployeeId AND y.PermissionId = n.Id);

    INSERT INTO UserPositionPermissions (EmployeeId, PositionId, PermissionId)
    SELECT x.EmployeeId, x.PositionId, n.Id
    FROM UserPositionPermissions x CROSS JOIN Permissions n
    WHERE x.PermissionId = @old AND n.Code IN (N'Admin.PasswordPolicy.View', N'Admin.PasswordPolicy.Manage', N'Admin.Users.PasswordSet')
      AND NOT EXISTS (SELECT 1 FROM UserPositionPermissions y WHERE y.EmployeeId = x.EmployeeId AND y.PositionId = x.PositionId AND y.PermissionId = n.Id);

    INSERT INTO PermissionGroupPermissions (GroupId, PermissionId)
    SELECT x.GroupId, n.Id
    FROM PermissionGroupPermissions x CROSS JOIN Permissions n
    WHERE x.PermissionId = @old AND n.Code IN (N'Admin.PasswordPolicy.View', N'Admin.PasswordPolicy.Manage', N'Admin.Users.PasswordSet')
      AND NOT EXISTS (SELECT 1 FROM PermissionGroupPermissions y WHERE y.GroupId = x.GroupId AND y.PermissionId = n.Id);
END;

COMMIT;
