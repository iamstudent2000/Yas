-- YasPortal: admin-managed slides of the public landing page. Idempotent; safe to run more than once.
-- RUN THIS BEFORE deploying: the landing page ("/") reads the HomeSlides table (if it cannot, the page shows no slides).
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

-- A fresh table gets the three starter slides (change or delete them on the admin page; the page keeps at least one valid slide).
IF NOT EXISTS (SELECT 1 FROM HomeSlides)
    INSERT INTO HomeSlides (Id, Title, Text, Icon, LinkText, LinkUrl, SortOrder, IsActive, StartsOn, ExpiresOn, CreatedAtUtc)
    VALUES
    (NEWID(), N'به یاس پورتال خوش آمدید', N'سامانه یکپارچه مدیریت سازمان، سمت‌ها، درخواست‌ها و دسترسی‌های کارکنان در یک بستر واحد.', N'ti-building', N'ورود به سامانه', N'/login', 1, 1, NULL, NULL, SYSUTCDATETIME()),
    (NEWID(), N'درخواست‌ها بدون کاغذ', N'ثبت، پیگیری و تأیید درخواست‌ها به‌صورت برخط و با مسیر تأیید مشخص برای هر سمت.', N'ti-file-check', NULL, NULL, 2, 1, NULL, NULL, SYSUTCDATETIME()),
    (NEWID(), N'اعلان‌ها و رویدادها', N'از تصمیم‌ها و پیام‌های مهم بلافاصله باخبر شوید؛ همه رویدادهای سامانه ثبت و قابل پیگیری است.', N'ti-bell-ringing', NULL, NULL, 3, 1, NULL, NULL, SYSUTCDATETIME());

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.LandingSlides.View', N'مشاهده اسلایدهای صفحه اصلی (فقط خواندنی)'),
    (N'Admin.LandingSlides.Manage', N'مدیریت اسلایدهای صفحه اصلی (افزودن، ویرایش، حذف، تاریخ انقضا)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
