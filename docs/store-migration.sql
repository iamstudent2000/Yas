-- Store (فروشگاه), stage 1: catalogue and stock ledger. Idempotent; safe to run more than once.
IF OBJECT_ID(N'dbo.StoreCategories', N'U') IS NULL
    CREATE TABLE [dbo].[StoreCategories] ([Id] uniqueidentifier NOT NULL, [Name] nvarchar(100) NOT NULL, [SortOrder] int NOT NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_StoreCategories] PRIMARY KEY ([Id]));
IF OBJECT_ID(N'dbo.StoreItems', N'U') IS NULL
    CREATE TABLE [dbo].[StoreItems] ([Id] uniqueidentifier NOT NULL, [Code] nvarchar(50) NOT NULL, [Name] nvarchar(200) NOT NULL, [CategoryId] uniqueidentifier NOT NULL, [Unit] nvarchar(30) NOT NULL, [Description] nvarchar(500) NULL, [IsActive] bit NOT NULL, CONSTRAINT [PK_StoreItems] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreItems_StoreCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[StoreCategories] ([Id]));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'IX_StoreItems_Code')
    EXEC(N'CREATE UNIQUE INDEX [IX_StoreItems_Code] ON [dbo].[StoreItems] ([Code])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreItems') AND name = N'IX_StoreItems_CategoryId')
    EXEC(N'CREATE INDEX [IX_StoreItems_CategoryId] ON [dbo].[StoreItems] ([CategoryId])');
IF COL_LENGTH(N'dbo.StoreItems', N'MinStock') IS NOT NULL ALTER TABLE [dbo].[StoreItems] DROP COLUMN [MinStock];
IF COL_LENGTH(N'dbo.StoreItems', N'MaxPerRequest') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [MaxPerRequest] decimal(18,3) NOT NULL CONSTRAINT [DF_StoreItems_MaxPerRequest] DEFAULT 0;
IF COL_LENGTH(N'dbo.StoreItems', N'RequestOpensOn') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [RequestOpensOn] date NULL;
IF COL_LENGTH(N'dbo.StoreItems', N'RequestClosesOn') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [RequestClosesOn] date NULL;
IF COL_LENGTH(N'dbo.StoreItems', N'IsLottery') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [IsLottery] bit NOT NULL CONSTRAINT [DF_StoreItems_IsLottery] DEFAULT 0;
IF COL_LENGTH(N'dbo.StoreItems', N'IsVisible') IS NULL ALTER TABLE [dbo].[StoreItems] ADD [IsVisible] bit NOT NULL CONSTRAINT [DF_StoreItems_IsVisible] DEFAULT 1;
IF OBJECT_ID(N'dbo.StoreStockMovements', N'U') IS NULL
    CREATE TABLE [dbo].[StoreStockMovements] ([Id] uniqueidentifier NOT NULL, [ItemId] uniqueidentifier NOT NULL, [Kind] int NOT NULL, [Delta] decimal(18,3) NOT NULL, [Reference] nvarchar(200) NULL, [Note] nvarchar(300) NULL, [CreatedByEmployeeId] uniqueidentifier NULL, [OrderId] uniqueidentifier NULL, [CreatedAtUtc] datetime2 NOT NULL, CONSTRAINT [PK_StoreStockMovements] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreStockMovements_StoreItems_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[StoreItems] ([Id]));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreStockMovements') AND name = N'IX_StoreStockMovements_ItemId_CreatedAtUtc')
    EXEC(N'CREATE INDEX [IX_StoreStockMovements_ItemId_CreatedAtUtc] ON [dbo].[StoreStockMovements] ([ItemId], [CreatedAtUtc])');

-- Stage 2: orders (the approval itself is a WorkflowRequest of type StoreOrder)
IF OBJECT_ID(N'dbo.StoreOrders', N'U') IS NULL
    CREATE TABLE [dbo].[StoreOrders] ([Id] uniqueidentifier NOT NULL, [RequestId] uniqueidentifier NOT NULL, [EmployeeId] uniqueidentifier NOT NULL, [PositionId] uniqueidentifier NOT NULL, [Note] nvarchar(500) NULL, [CreatedAtUtc] datetime2 NOT NULL, [IssuedAtUtc] datetime2 NULL, [IssuedByEmployeeId] uniqueidentifier NULL, [IssueNote] nvarchar(500) NULL, CONSTRAINT [PK_StoreOrders] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreOrders_WorkflowRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [dbo].[WorkflowRequests] ([Id]));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrders') AND name = N'IX_StoreOrders_RequestId')
    EXEC(N'CREATE UNIQUE INDEX [IX_StoreOrders_RequestId] ON [dbo].[StoreOrders] ([RequestId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrders') AND name = N'IX_StoreOrders_EmployeeId_CreatedAtUtc')
    EXEC(N'CREATE INDEX [IX_StoreOrders_EmployeeId_CreatedAtUtc] ON [dbo].[StoreOrders] ([EmployeeId], [CreatedAtUtc])');
IF OBJECT_ID(N'dbo.StoreOrderLines', N'U') IS NULL
    CREATE TABLE [dbo].[StoreOrderLines] ([Id] uniqueidentifier NOT NULL, [OrderId] uniqueidentifier NOT NULL, [ItemId] uniqueidentifier NOT NULL, [ItemCode] nvarchar(50) NOT NULL, [ItemName] nvarchar(200) NOT NULL, [Unit] nvarchar(30) NOT NULL, [Quantity] decimal(18,3) NOT NULL, CONSTRAINT [PK_StoreOrderLines] PRIMARY KEY ([Id]), CONSTRAINT [FK_StoreOrderLines_StoreOrders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[StoreOrders] ([Id]) ON DELETE CASCADE, CONSTRAINT [FK_StoreOrderLines_StoreItems_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[StoreItems] ([Id]));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.StoreOrderLines') AND name = N'IX_StoreOrderLines_OrderId')
    EXEC(N'CREATE INDEX [IX_StoreOrderLines_OrderId] ON [dbo].[StoreOrderLines] ([OrderId])');
