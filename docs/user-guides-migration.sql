-- YasPortal: user guides (UserGuides) and their permissions. Idempotent; safe to run more than once.
-- Run before deploying user guides. Permissions are not granted to anyone: assign them on the access page.
--   Guides.Own.Manage   - write guides and edit / switch off / delete only the guides this person wrote
--   Admin.Guides.View   - see every guide in the guides workspace (/guides/manage) read-only, and preview guides that are off
--   Admin.Guides.Manage - write guides and edit / switch off / delete the guides of everyone (also guides written before authors were recorded)
-- Reading the guides (/guides) needs no permission: a guide marked public (Visibility = 0) is open to everyone, also before
-- sign-in; a guide marked for signed-in people (Visibility = 1) is open to anyone who has signed in.
-- ContentHtml is HTML cleaned by the application (a small set of tags); do not edit it by hand.
-- The development database creates the table itself at startup and the permission rows from the catalog.
-- An earlier draft of this feature had a GuideBlocks table and Direction / RelatedPermissionCode columns; they are dropped here
-- (that draft was never released, so there is nothing to keep).
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.GuideBlocks', N'U') IS NOT NULL DROP TABLE [dbo].[GuideBlocks];

IF OBJECT_ID(N'dbo.UserGuides', N'U') IS NULL
    CREATE TABLE [dbo].[UserGuides] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Summary] nvarchar(300) NULL,
        [ContentHtml] nvarchar(max) NOT NULL,
        [Visibility] int NOT NULL,                -- 0 = public (also before sign-in), 1 = signed-in people only
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [CreatedByEmployeeId] uniqueidentifier NULL,   -- the author; NULL for a guide written before authors were recorded
        CONSTRAINT [PK_UserGuides] PRIMARY KEY ([Id]));

IF COL_LENGTH(N'dbo.UserGuides', N'Direction') IS NOT NULL ALTER TABLE [dbo].[UserGuides] DROP COLUMN [Direction];
IF COL_LENGTH(N'dbo.UserGuides', N'RelatedPermissionCode') IS NOT NULL ALTER TABLE [dbo].[UserGuides] DROP COLUMN [RelatedPermissionCode];
IF COL_LENGTH(N'dbo.UserGuides', N'ContentHtml') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [ContentHtml] nvarchar(max) NOT NULL CONSTRAINT [DF_UserGuides_ContentHtml] DEFAULT N'';
IF COL_LENGTH(N'dbo.UserGuides', N'Visibility') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [Visibility] int NOT NULL CONSTRAINT [DF_UserGuides_Visibility] DEFAULT 1;

IF COL_LENGTH(N'dbo.UserGuides', N'CreatedByEmployeeId') IS NULL ALTER TABLE [dbo].[UserGuides] ADD [CreatedByEmployeeId] uniqueidentifier NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserGuides') AND name = N'IX_UserGuides_IsActive_SortOrder')
    EXEC(N'CREATE INDEX [IX_UserGuides_IsActive_SortOrder] ON [dbo].[UserGuides] ([IsActive], [SortOrder])');

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Guides.Own.Manage', N'نوشتن راهنمای کاربری و ویرایش راهنماهای خودم (غیرفعال‌کردن و حذف راهنماهای خودم)'),
    (N'Admin.Guides.View', N'مشاهده همه راهنماهای کاربری (فقط خواندنی)'),
    (N'Admin.Guides.Manage', N'مدیریت همه راهنماهای کاربری (ویرایش، غیرفعال‌کردن و حذف راهنمای همه نویسنده‌ها)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

-- names from an earlier draft of the two admin permissions
UPDATE Permissions SET Name = N'مشاهده همه راهنماهای کاربری (فقط خواندنی)' WHERE Code = N'Admin.Guides.View';
UPDATE Permissions SET Name = N'مدیریت همه راهنماهای کاربری (ویرایش، غیرفعال‌کردن و حذف راهنمای همه نویسنده‌ها)' WHERE Code = N'Admin.Guides.Manage';

COMMIT;
