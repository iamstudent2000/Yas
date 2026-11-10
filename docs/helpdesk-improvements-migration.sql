-- YasPortal: helpdesk improvements. Idempotent; safe to run more than once. Run on production before deploying this version.
-- 1) The table for files attached to a request (helpdesk requests accept up to 3 files, 5 MB each).
-- 2) The helpdesk form now has a second part (اقدام پشتیبانی) that the support handler fills when approving. A request can only be
--    submitted when a step of the path is set to fill it, so this assigns it to the helpdesk path's last step when none is set.
--    Check the result in /admin/workflows (type: درخواست پشتیبانی); change the step there if another one should fill it.
--    Requests already in progress are not touched: their steps keep what they had, so they can still be approved without the new part.
-- New form fields (category, affected system, location, contact phone) are stored inside each request's field values: no column changes.
IF OBJECT_ID(N'dbo.WorkflowRequestAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowRequestAttachments] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [UploadedByEmployeeId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(150) NOT NULL,
        [SizeBytes] bigint NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkflowRequestAttachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowRequestAttachments_WorkflowRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[WorkflowRequests] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WorkflowRequestAttachments_Employees_UploadedByEmployeeId] FOREIGN KEY ([UploadedByEmployeeId]) REFERENCES [dbo].[Employees] ([Id])
    );
    CREATE INDEX [IX_WorkflowRequestAttachments_RequestId] ON [dbo].[WorkflowRequestAttachments] ([RequestId]);
END;

IF NOT EXISTS (SELECT 1 FROM [dbo].[WorkflowStepDefinitions] WHERE [WorkflowType] = N'Helpdesk' AND [FillsPart] = 2)
    UPDATE d SET d.[FillsPart] = 2
    FROM [dbo].[WorkflowStepDefinitions] d
    WHERE d.[WorkflowType] = N'Helpdesk'
      AND d.[Order] = (SELECT MAX(x.[Order]) FROM [dbo].[WorkflowStepDefinitions] x WHERE x.[WorkflowType] = N'Helpdesk')
      AND (SELECT COUNT(*) FROM [dbo].[WorkflowStepDefinitions] y WHERE y.[WorkflowType] = N'Helpdesk' AND y.[Order] = d.[Order]) = 1;

-- 3) Priority rank on requests (1 = کم, 2 = متوسط, 3 = زیاد, 4 = بحرانی; NULL for types without a priority). Lets the inbox, my requests and the
--    request filter sort and filter by priority in SQL. Existing requests are filled from their stored "priority" field value.
IF COL_LENGTH(N'dbo.WorkflowRequests', N'PriorityRank') IS NULL
    ALTER TABLE [dbo].[WorkflowRequests] ADD [PriorityRank] int NULL;
GO
UPDATE [dbo].[WorkflowRequests]
SET [PriorityRank] = CASE JSON_VALUE([FieldValuesJson], '$.priority') WHEN N'کم' THEN 1 WHEN N'متوسط' THEN 2 WHEN N'زیاد' THEN 3 WHEN N'بحرانی' THEN 4 END
WHERE [PriorityRank] IS NULL AND ISJSON([FieldValuesJson]) = 1 AND JSON_VALUE([FieldValuesJson], '$.priority') IS NOT NULL;

-- 4) Per-person "waiting too long" reminder threshold (set on the profile page). No row = no reminders.
--    The notification kind WaitingReminder needs no schema change (kinds are stored as text).
IF OBJECT_ID(N'dbo.WaitingReminderSettings', N'U') IS NULL
    CREATE TABLE [dbo].[WaitingReminderSettings] (
        [EmployeeId] uniqueidentifier NOT NULL,
        [AfterHours] int NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WaitingReminderSettings] PRIMARY KEY ([EmployeeId]),
        CONSTRAINT [FK_WaitingReminderSettings_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE CASCADE
    );
