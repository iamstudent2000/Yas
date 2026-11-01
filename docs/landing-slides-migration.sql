-- YasPortal: admin-managed slides of the public landing page. Idempotent; safe to run more than once.
-- RUN THIS BEFORE deploying: the landing page ("/") reads the HomeSlides table (if it cannot, the page shows no slides).
-- Adds the LinkGroups / PortalLinks tables, the HomeSlides table, the HomeSlideAttachments table (the optional file of a slide, up to 5 MB, stored in the database) and the permissions Admin.LandingSlides.View / Admin.LandingSlides.Manage (not granted to anyone: assign them on the access page).
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

IF OBJECT_ID(N'dbo.HomeSlideAttachments', N'U') IS NULL
    CREATE TABLE [dbo].[HomeSlideAttachments] (
        [SlideId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(150) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_HomeSlideAttachments] PRIMARY KEY ([SlideId]),
        CONSTRAINT [FK_HomeSlideAttachments_HomeSlides_SlideId] FOREIGN KEY ([SlideId]) REFERENCES [dbo].[HomeSlides] ([Id]) ON DELETE CASCADE);

-- A fresh table gets the three starter slides (change or delete them on the admin page; the page keeps at least one valid slide).
IF NOT EXISTS (SELECT 1 FROM HomeSlides)
    INSERT INTO HomeSlides (Id, Title, Text, Icon, LinkText, LinkUrl, SortOrder, IsActive, StartsOn, ExpiresOn, CreatedAtUtc)
    VALUES
    (NEWID(), N'به یاس پورتال خوش آمدید', N'سامانه یکپارچه مدیریت سازمان، سمت‌ها، درخواست‌ها و دسترسی‌های کارکنان در یک بستر واحد.', N'ti-building', N'ورود به سامانه', N'/login', 1, 1, NULL, NULL, SYSUTCDATETIME()),
    (NEWID(), N'درخواست‌ها بدون کاغذ', N'ثبت، پیگیری و تأیید درخواست‌ها به‌صورت برخط و با مسیر تأیید مشخص برای هر سمت.', N'ti-file-check', NULL, NULL, 2, 1, NULL, NULL, SYSUTCDATETIME()),
    (NEWID(), N'اعلان‌ها و رویدادها', N'از تصمیم‌ها و پیام‌های مهم بلافاصله باخبر شوید؛ همه رویدادهای سامانه ثبت و قابل پیگیری است.', N'ti-bell-ringing', NULL, NULL, 3, 1, NULL, NULL, SYSUTCDATETIME());

-- Portal links (the home page's quick access and grouped links, managed on /admin/links) and their permissions.
IF OBJECT_ID(N'dbo.LinkGroups', N'U') IS NULL
    CREATE TABLE [dbo].[LinkGroups] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [ParentId] uniqueidentifier NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_LinkGroups] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LinkGroups_LinkGroups_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[LinkGroups] ([Id]));

IF OBJECT_ID(N'dbo.PortalLinks', N'U') IS NULL
    CREATE TABLE [dbo].[PortalLinks] (
        [Id] uniqueidentifier NOT NULL,
        [Title] nvarchar(150) NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [Description] nvarchar(300) NULL,
        [GroupId] uniqueidentifier NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [IsQuickAccess] bit NOT NULL,
        [QuickOrder] int NOT NULL,
        CONSTRAINT [PK_PortalLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PortalLinks_LinkGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[LinkGroups] ([Id]));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.LinkGroups') AND name = N'IX_LinkGroups_ParentId')
    EXEC(N'CREATE INDEX [IX_LinkGroups_ParentId] ON [dbo].[LinkGroups] ([ParentId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PortalLinks') AND name = N'IX_PortalLinks_GroupId')
    EXEC(N'CREATE INDEX [IX_PortalLinks_GroupId] ON [dbo].[PortalLinks] ([GroupId])');

-- A fresh system gets five quick-access links to pages of this site (change or delete them on the admin page).
IF NOT EXISTS (SELECT 1 FROM PortalLinks) AND NOT EXISTS (SELECT 1 FROM LinkGroups)
    INSERT INTO PortalLinks (Id, Title, Url, Description, GroupId, SortOrder, IsActive, IsQuickAccess, QuickOrder)
    VALUES
    (NEWID(), N'ورود به سامانه', N'/login', N'با نام کاربری و رمز عبور', NULL, 1, 1, 1, 1),
    (NEWID(), N'ثبت درخواست جدید', N'/requests/new', N'پس از ورود', NULL, 2, 1, 1, 2),
    (NEWID(), N'کارتابل تاییدات', N'/requests/inbox', N'پس از ورود', NULL, 3, 1, 1, 3),
    (NEWID(), N'اعلان‌ها', N'/notifications', N'پس از ورود', NULL, 4, 1, 1, 4),
    (NEWID(), N'پروفایل من', N'/my-profile', N'پس از ورود', NULL, 5, 1, 1, 5);

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.LandingSlides.View', N'مشاهده اسلایدهای صفحه اصلی (فقط خواندنی)'),
    (N'Admin.LandingSlides.Manage', N'مدیریت اسلایدهای صفحه اصلی (افزودن، ویرایش، حذف، تاریخ انقضا)'),
    (N'Admin.Links.View', N'مشاهده پیوندهای صفحه اصلی (فقط خواندنی)'),
    (N'Admin.Links.Manage', N'مدیریت پیوندهای صفحه اصلی (گروه‌ها، پیوندها، دسترسی سریع، ورود گروهی)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
