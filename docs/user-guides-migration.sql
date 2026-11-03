-- YasPortal: user guides (UserGuides, GuideBlocks) and their permissions. Idempotent; safe to run more than once.
-- Run before deploying user guides. Permissions are not granted to anyone: assign them on the access page.
--   Admin.Guides.View   - see the guides admin list (read-only), including guides that are off or limited to a permission
--   Admin.Guides.Manage - write, edit, switch off and delete guides
-- Reading the guides (/guides) needs no permission: every signed-in person sees the active guides, except a guide that names a
-- related permission, which only people holding that permission see.
-- The development database creates the tables itself at startup and the permission rows from the catalog.
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.UserGuides', N'U') IS NULL
    CREATE TABLE [dbo].[UserGuides] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Summary] nvarchar(300) NULL,
        [Direction] int NOT NULL,                 -- 0 = right-to-left, 1 = left-to-right
        [RelatedPermissionCode] nvarchar(150) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UserGuides] PRIMARY KEY ([Id]));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UserGuides') AND name = N'IX_UserGuides_IsActive_SortOrder')
    EXEC(N'CREATE INDEX [IX_UserGuides_IsActive_SortOrder] ON [dbo].[UserGuides] ([IsActive], [SortOrder])');

IF OBJECT_ID(N'dbo.GuideBlocks', N'U') IS NULL
    CREATE TABLE [dbo].[GuideBlocks] (
        [Id] uniqueidentifier NOT NULL,
        [GuideId] uniqueidentifier NOT NULL,
        [SortOrder] int NOT NULL,
        [Kind] int NOT NULL,                      -- 0 paragraph, 1 heading, 2 subheading, 3 list, 4 note
        [Text] nvarchar(4000) NOT NULL,
        [Direction] int NULL,                     -- NULL = the guide's direction
        [Align] int NOT NULL,                     -- 0 start, 1 center, 2 end, 3 justify
        [Size] int NOT NULL,                      -- 0 normal, 1 small, 2 large
        [Tone] int NOT NULL,                      -- 0 default, 1 accent, 2 success, 3 warning, 4 danger, 5 muted
        [Bold] bit NOT NULL,
        [Italic] bit NOT NULL,
        CONSTRAINT [PK_GuideBlocks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_GuideBlocks_UserGuides_GuideId] FOREIGN KEY ([GuideId]) REFERENCES [dbo].[UserGuides] ([Id]) ON DELETE CASCADE);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GuideBlocks') AND name = N'IX_GuideBlocks_GuideId_SortOrder')
    EXEC(N'CREATE INDEX [IX_GuideBlocks_GuideId_SortOrder] ON [dbo].[GuideBlocks] ([GuideId], [SortOrder])');

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.Guides.View', N'مشاهده مدیریت راهنماهای کاربری (فقط خواندنی)'),
    (N'Admin.Guides.Manage', N'مدیریت راهنماهای کاربری (نوشتن، ویرایش، غیرفعال‌کردن و حذف)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
