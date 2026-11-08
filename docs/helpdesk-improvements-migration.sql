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
