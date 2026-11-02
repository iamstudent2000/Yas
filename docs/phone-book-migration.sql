-- YasPortal: phone book (ContactGroups, Contacts) and its permissions. Idempotent; safe to run more than once.
-- Run before deploying the phone book. Permissions are not granted to anyone: assign them on the access page.
--   PhoneBook.Directory.View  - read and search the phone book (/phone-book)
--   Admin.PhoneBook.View / Admin.PhoneBook.Manage - the admin page (/admin/phone-book), read-only / editing
-- The development database creates the tables itself at startup and the permission rows from the catalog.
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.ContactGroups', N'U') IS NULL
    CREATE TABLE [dbo].[ContactGroups] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ContactGroups] PRIMARY KEY ([Id]));

IF OBJECT_ID(N'dbo.Contacts', N'U') IS NULL
    CREATE TABLE [dbo].[Contacts] (
        [Id] uniqueidentifier NOT NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [JobTitle] nvarchar(150) NULL,
        [Extension] nvarchar(30) NULL,
        [Phone] nvarchar(30) NULL,
        [Mobile] nvarchar(30) NULL,
        [Email] nvarchar(150) NULL,
        [Note] nvarchar(300) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Contacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Contacts_ContactGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[ContactGroups] ([Id]));

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Contacts') AND name = N'IX_Contacts_GroupId')
    EXEC(N'CREATE INDEX [IX_Contacts_GroupId] ON [dbo].[Contacts] ([GroupId])');

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'PhoneBook.Directory.View', N'مشاهده و جستجو در دفترچه تلفن'),
    (N'Admin.PhoneBook.View', N'مشاهده مدیریت دفترچه تلفن (فقط خواندنی)'),
    (N'Admin.PhoneBook.Manage', N'مدیریت دفترچه تلفن (گروه‌ها و مخاطبان)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
