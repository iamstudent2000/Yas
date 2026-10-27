-- YasPortal: notification center (send a notification by hand, all-notifications report). Idempotent; safe to run more than once.
-- Run once on production BEFORE deploying this version. The development database is patched automatically at startup.
-- Users must sign out and sign in again afterwards (permissions are snapshotted into the sign-in cookie).
SET XACT_ABORT ON;
BEGIN TRAN;

-- 1) Who sent a hand-written notification (NULL for the ones the system produced).
IF COL_LENGTH(N'dbo.Notifications', N'SenderEmployeeId') IS NULL
BEGIN
    ALTER TABLE dbo.Notifications ADD SenderEmployeeId uniqueidentifier NULL;
    EXEC(N'ALTER TABLE dbo.Notifications ADD CONSTRAINT FK_Notifications_Employees_SenderEmployeeId FOREIGN KEY (SenderEmployeeId) REFERENCES dbo.Employees (Id)');
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_SenderEmployeeId_CreatedAtUtc')
    EXEC(N'CREATE INDEX IX_Notifications_SenderEmployeeId_CreatedAtUtc ON dbo.Notifications (SenderEmployeeId, CreatedAtUtc)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Notifications') AND name = N'IX_Notifications_CreatedAtUtc')
    EXEC(N'CREATE INDEX IX_Notifications_CreatedAtUtc ON dbo.Notifications (CreatedAtUtc)');

-- 2) The three new permissions (the application also creates missing ones at development startup).
INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Notifications.Message.Send', N'ارسال اعلان به کارمندان و مشاهده اعلان‌های ارسالی خود'),
    (N'Admin.Notifications.View', N'مشاهده گزارش همه اعلان‌ها (فقط خواندنی)'),
    (N'Reports.Notifications.Export', N'خروجی گزارش اعلان‌ها')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

-- 3) Give every administrator account the new permissions (administrators hold permissions only through direct grants).
--    Employees get them from /admin/access (per person, position or permission group), like any other permission.
INSERT INTO EmployeePermissions (EmployeeId, PermissionId)
SELECT e.Id, p.Id
FROM Employees e
CROSS JOIN Permissions p
WHERE e.IsAdmin = 1
  AND p.Code IN (N'Notifications.Message.Send', N'Admin.Notifications.View', N'Reports.Notifications.Export')
  AND NOT EXISTS (SELECT 1 FROM EmployeePermissions x WHERE x.EmployeeId = e.Id AND x.PermissionId = p.Id);

COMMIT;
