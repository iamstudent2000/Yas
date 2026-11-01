-- YasPortal: admin-managed slides of the public landing page. Idempotent; safe to run more than once.
-- RUN THIS BEFORE deploying: the landing page ("/") reads the HomeSlides table (if it cannot, it shows the built-in slide, but fix it).
-- Adds the HomeSlides table and the permissions Admin.LandingSlides.View / Admin.LandingSlides.Manage (not granted to anyone: assign them on the access page).
-- The development database creates the table itself at startup and the permission rows from the catalog.
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.HomeSlides', N'U') IS NULL
    CREATE TABLE [dbo].[HomeSlides] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Text] nvarchar(600) NOT NULL,
        [Icon] nvarchar(50) NOT NULL,
        [LinkText] nvarchar(100) NULL,
        [LinkUrl] nvarchar(300) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [StartsOn] date NULL,
        [ExpiresOn] date NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_HomeSlides] PRIMARY KEY ([Id]));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.HomeSlides') AND name = N'IX_HomeSlides_IsActive_SortOrder')
    EXEC(N'CREATE INDEX [IX_HomeSlides_IsActive_SortOrder] ON [dbo].[HomeSlides] ([IsActive], [SortOrder])');

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.LandingSlides.View', N'مشاهده اسلایدهای صفحه اصلی (فقط خواندنی)'),
    (N'Admin.LandingSlides.Manage', N'مدیریت اسلایدهای صفحه اصلی (افزودن، ویرایش، حذف، تاریخ انقضا)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
