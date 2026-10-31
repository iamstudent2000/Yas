-- YasPortal: audit-log archive. Idempotent; safe to run more than once.
-- RUN THIS BEFORE deploying the version that archives audit rows: every audit read and write now touches AuditEntries.ArchivedAtUtc.
-- Adds the ArchivedAtUtc column (null = a current event; a time = moved to the archive, still kept and searchable), an index for it,
-- and the permission Admin.AuditLog.Manage (the "بایگانی رویدادهای قدیمی" button on the audit page).
-- The permission is NOT granted to anyone here: assign it on the access page to the people who may archive by hand.
-- Also creates the AuditArchiveSettings table: the administrator chooses on the audit page whether old rows are archived
-- automatically (daily) or only by hand, and how many months old a row must be. The daily job needs no permission.
-- Until a setting is saved, appsettings Audit:ArchiveAfterMonths gives the starting values (default 12; 0 = manual only).
-- The development database applies the column and index itself at startup and creates the permission row from the catalog.
SET XACT_ABORT ON;
BEGIN TRAN;

IF COL_LENGTH(N'dbo.AuditEntries', N'ArchivedAtUtc') IS NULL
    ALTER TABLE [dbo].[AuditEntries] ADD [ArchivedAtUtc] datetime2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AuditEntries') AND name = N'IX_AuditEntries_ArchivedAtUtc_OccurredAtUtc')
    EXEC(N'CREATE INDEX [IX_AuditEntries_ArchivedAtUtc_OccurredAtUtc] ON [dbo].[AuditEntries] ([ArchivedAtUtc], [OccurredAtUtc])');

IF OBJECT_ID(N'dbo.AuditArchiveSettings', N'U') IS NULL
    CREATE TABLE [dbo].[AuditArchiveSettings] (
        [Id] int NOT NULL,
        [AutomaticEnabled] bit NOT NULL,
        [AfterMonths] int NOT NULL,
        [LastAutomaticRunAtUtc] datetime2 NULL,
        [LastAutomaticRunCount] int NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditArchiveSettings] PRIMARY KEY ([Id]));

INSERT INTO Permissions (Id, Code, Name)
SELECT NEWID(), v.Code, v.Name
FROM (VALUES
    (N'Admin.AuditLog.Manage', N'مدیریت گزارش رویدادها (بایگانی رویدادهای قدیمی)')
) AS v(Code, Name)
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code = v.Code);

COMMIT;
