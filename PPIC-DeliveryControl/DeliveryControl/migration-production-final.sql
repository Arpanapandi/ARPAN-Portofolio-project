IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [ActivityLogs] (
        [LogId] int NOT NULL IDENTITY,
        [Module] nvarchar(100) NOT NULL,
        [Action] nvarchar(50) NOT NULL,
        [EntityName] nvarchar(200) NULL,
        [EntityId] int NULL,
        [Description] nvarchar(max) NULL,
        [OldData] nvarchar(500) NULL,
        [NewData] nvarchar(500) NULL,
        [Timestamp] datetime2 NOT NULL,
        [PerformedBy] nvarchar(100) NOT NULL,
        [IpAddress] nvarchar(50) NULL,
        [UserAgent] nvarchar(200) NULL,
        CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([LogId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [Customers] (
        [CustomerId] int NOT NULL IDENTITY,
        [AutoCode] nvarchar(20) NULL,
        [CustomerCode] nvarchar(50) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [Route] nvarchar(100) NULL,
        [Cycle] nvarchar(50) NULL,
        [Docking] nvarchar(100) NULL,
        [Pickup] nvarchar(100) NULL,
        [ETD] nvarchar(100) NULL,
        [Range] nvarchar(100) NULL,
        [SKID] nvarchar(50) NULL,
        [Area] nvarchar(100) NULL,
        [RemarkOrder] nvarchar(500) NULL,
        [StdPrepareTime] int NOT NULL,
        [StartPrepareTime] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([CustomerId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [ItemMappings] (
        [MappingId] int NOT NULL IDENTITY,
        [VIN] nvarchar(50) NOT NULL,
        [Customer] nvarchar(100) NOT NULL,
        [CustomerPartNumber] nvarchar(100) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_ItemMappings] PRIMARY KEY ([MappingId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [Items] (
        [ItemId] int NOT NULL IDENTITY,
        [ItemCode] nvarchar(50) NOT NULL,
        [ItemName] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Unit] nvarchar(20) NULL,
        [Category] nvarchar(100) NULL,
        [Weight] decimal(18,2) NULL,
        [Volume] decimal(18,2) NULL,
        [MinStock] int NOT NULL,
        [MaxStock] int NOT NULL,
        [Plant] nvarchar(20) NULL,
        [Rack] nvarchar(10) NULL,
        [NoRack] int NULL,
        [Customer] nvarchar(100) NULL,
        [VIN] nvarchar(100) NULL,
        [QtyLot] int NULL,
        [RackMin] int NULL,
        [ROP] int NULL,
        [RackMax] int NULL,
        [CustomerPartNumber] nvarchar(50) NULL,
        [KanbanType] nvarchar(50) NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([ItemId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [ScanNGLogs] (
        [Id] int NOT NULL IDENTITY,
        [Module] nvarchar(20) NOT NULL,
        [Tag] nvarchar(100) NOT NULL,
        [Label] nvarchar(100) NOT NULL,
        [Kanban] nvarchar(100) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [CreatedBy] nvarchar(100) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_ScanNGLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [StockSnapshots] (
        [Id] int NOT NULL IDENTITY,
        [SnapshotDate] datetime2 NOT NULL,
        [SnapshotTime] time NOT NULL,
        [ItemCode] nvarchar(100) NOT NULL,
        [ItemName] nvarchar(200) NOT NULL,
        [Plant] nvarchar(50) NOT NULL,
        [StockQty] int NOT NULL,
        [DaysCoverage] decimal(10,4) NOT NULL,
        [StockLevel] nvarchar(20) NOT NULL,
        [IsManual] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_StockSnapshots] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [SystemSettings] (
        [Id] int NOT NULL IDENTITY,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(500) NOT NULL,
        [Description] nvarchar(200) NULL,
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [Users] (
        [UserId] int NOT NULL IDENTITY,
        [Username] nvarchar(50) NOT NULL,
        [Password] nvarchar(255) NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Email] nvarchar(100) NULL,
        [Role] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        [HasAllDockAccess] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [Docks] (
        [DockId] int NOT NULL IDENTITY,
        [CustomerId] int NOT NULL,
        [DockCode] nvarchar(50) NOT NULL,
        [DockName] nvarchar(200) NOT NULL,
        [Location] nvarchar(500) NULL,
        [Description] nvarchar(1000) NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_Docks] PRIMARY KEY ([DockId]),
        CONSTRAINT [FK_Docks_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([CustomerId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [PullingRecords] (
        [PullingId] int NOT NULL IDENTITY,
        [Plant] nvarchar(20) NOT NULL,
        [Rack] nvarchar(5) NOT NULL,
        [Column] int NOT NULL,
        [Tag] nvarchar(100) NOT NULL,
        [Label] nvarchar(100) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [ItemId] int NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [IsManualAdjust] bit NOT NULL,
        [AdjustNote] nvarchar(500) NULL,
        [Remark] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_PullingRecords] PRIMARY KEY ([PullingId]),
        CONSTRAINT [FK_PullingRecords_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([ItemId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [UserDocks] (
        [UserDockId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [CustomerId] int NOT NULL,
        [AssignedDate] datetime2 NOT NULL,
        [UserId1] int NULL,
        CONSTRAINT [PK_UserDocks] PRIMARY KEY ([UserDockId]),
        CONSTRAINT [FK_UserDocks_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([CustomerId]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserDocks_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserDocks_Users_UserId1] FOREIGN KEY ([UserId1]) REFERENCES [Users] ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [DeliverySchedules] (
        [ScheduleId] int NOT NULL IDENTITY,
        [ScheduleNumber] nvarchar(50) NULL,
        [CustomerId] int NOT NULL,
        [Route] nvarchar(100) NULL,
        [Cycle] nvarchar(50) NULL,
        [EnterDockTime] datetime2 NULL,
        [PickupTime] datetime2 NULL,
        [ETD] datetime2 NULL,
        [Range] nvarchar(100) NULL,
        [SKID] nvarchar(max) NULL,
        [Area] nvarchar(100) NULL,
        [TotalTargetQuantity] decimal(18,2) NOT NULL,
        [TotalActualQuantity] decimal(18,2) NOT NULL,
        [ActualPickupTime] datetime2 NULL,
        [ScheduledDate] datetime2 NOT NULL,
        [ActualEnterDockTime] datetime2 NULL,
        [ActualStartTime] datetime2 NULL,
        [ActualEndTime] datetime2 NULL,
        [ReadyToDockTime] datetime2 NULL,
        [ActPrepareTime] float NULL,
        [StartPrepareTime] int NOT NULL,
        [StdPrepareTime] int NOT NULL,
        [PreparationStatus] nvarchar(20) NULL,
        [DriverStatus] nvarchar(20) NULL,
        [Status] nvarchar(20) NULL,
        [VehicleNumber] nvarchar(100) NULL,
        [DriverName] nvarchar(100) NULL,
        [DriverPhone] nvarchar(50) NULL,
        [Notes] nvarchar(1000) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(max) NULL,
        [UpdatedDate] datetime2 NULL,
        [UpdatedBy] nvarchar(max) NULL,
        [IsLeaderVerified] bit NOT NULL,
        [LeaderVerifiedAt] datetime2 NULL,
        [LeaderVerifiedBy] nvarchar(100) NULL,
        [LeaderVerifiedKanbanCount] int NOT NULL,
        [DockId] int NULL,
        CONSTRAINT [PK_DeliverySchedules] PRIMARY KEY ([ScheduleId]),
        CONSTRAINT [FK_DeliverySchedules_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([CustomerId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_DeliverySchedules_Docks_DockId] FOREIGN KEY ([DockId]) REFERENCES [Docks] ([DockId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [UserDockAccesses] (
        [UserDockAccessId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [DockId] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_UserDockAccesses] PRIMARY KEY ([UserDockAccessId]),
        CONSTRAINT [FK_UserDockAccesses_Docks_DockId] FOREIGN KEY ([DockId]) REFERENCES [Docks] ([DockId]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserDockAccesses_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [DeliveryItems] (
        [DeliveryItemId] int NOT NULL IDENTITY,
        [ScheduleId] int NOT NULL,
        [ItemId] int NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [ActualQuantity] decimal(18,2) NULL,
        [Unit] nvarchar(20) NULL,
        [TotalWeight] decimal(18,2) NULL,
        [TotalVolume] decimal(18,2) NULL,
        [Notes] nvarchar(1000) NULL,
        [IsCompleted] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [UpdatedDate] datetime2 NULL,
        CONSTRAINT [PK_DeliveryItems] PRIMARY KEY ([DeliveryItemId]),
        CONSTRAINT [FK_DeliveryItems_DeliverySchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [DeliverySchedules] ([ScheduleId]) ON DELETE CASCADE,
        CONSTRAINT [FK_DeliveryItems_Items_ItemId] FOREIGN KEY ([ItemId]) REFERENCES [Items] ([ItemId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE TABLE [PreparationRecords] (
        [PreparationId] int NOT NULL IDENTITY,
        [Plant] nvarchar(20) NOT NULL,
        [Tag] nvarchar(100) NOT NULL,
        [Label] nvarchar(100) NOT NULL,
        [Kanban] nvarchar(100) NOT NULL,
        [ScheduleId] int NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(100) NULL,
        [Remark] nvarchar(20) NOT NULL,
        CONSTRAINT [PK_PreparationRecords] PRIMARY KEY ([PreparationId]),
        CONSTRAINT [FK_PreparationRecords_DeliverySchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [DeliverySchedules] ([ScheduleId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CustomerId', N'Area', N'AutoCode', N'CreatedDate', N'CustomerCode', N'CustomerName', N'Cycle', N'Docking', N'ETD', N'IsActive', N'Pickup', N'Range', N'RemarkOrder', N'Route', N'SKID', N'StartPrepareTime', N'StdPrepareTime', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[Customers]'))
        SET IDENTITY_INSERT [Customers] ON;
    EXEC(N'INSERT INTO [Customers] ([CustomerId], [Area], [AutoCode], [CreatedDate], [CustomerCode], [CustomerName], [Cycle], [Docking], [ETD], [IsActive], [Pickup], [Range], [RemarkOrder], [Route], [SKID], [StartPrepareTime], [StdPrepareTime], [UpdatedDate])
    VALUES (1, NULL, NULL, ''2026-03-12T16:32:06.9212665+07:00'', N''CUST001'', N''PT ABC Manufacturing'', NULL, NULL, NULL, CAST(1 AS bit), NULL, NULL, NULL, N''Route A'', N''10 SKID'', 0, 0, NULL),
    (2, NULL, NULL, ''2026-03-12T16:32:06.9212667+07:00'', N''CUST002'', N''PT XYZ Industries'', NULL, NULL, NULL, CAST(1 AS bit), NULL, NULL, NULL, N''Route B'', N''15 SKID'', 0, 0, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CustomerId', N'Area', N'AutoCode', N'CreatedDate', N'CustomerCode', N'CustomerName', N'Cycle', N'Docking', N'ETD', N'IsActive', N'Pickup', N'Range', N'RemarkOrder', N'Route', N'SKID', N'StartPrepareTime', N'StdPrepareTime', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[Customers]'))
        SET IDENTITY_INSERT [Customers] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ItemId', N'Category', N'CreatedDate', N'Customer', N'CustomerPartNumber', N'Description', N'IsActive', N'ItemCode', N'ItemName', N'KanbanType', N'MaxStock', N'MinStock', N'NoRack', N'Plant', N'QtyLot', N'ROP', N'Rack', N'RackMax', N'RackMin', N'Unit', N'UpdatedDate', N'VIN', N'Volume', N'Weight') AND [object_id] = OBJECT_ID(N'[Items]'))
        SET IDENTITY_INSERT [Items] ON;
    EXEC(N'INSERT INTO [Items] ([ItemId], [Category], [CreatedDate], [Customer], [CustomerPartNumber], [Description], [IsActive], [ItemCode], [ItemName], [KanbanType], [MaxStock], [MinStock], [NoRack], [Plant], [QtyLot], [ROP], [Rack], [RackMax], [RackMin], [Unit], [UpdatedDate], [VIN], [Volume], [Weight])
    VALUES (1, N''Raw Material'', ''2026-03-12T16:32:06.9212755+07:00'', NULL, NULL, N''Raw material untuk produksi'', CAST(1 AS bit), N''ITM001'', N''Raw Material A'', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, N''KG'', NULL, NULL, NULL, 1.0),
    (2, N''Finished Goods'', ''2026-03-12T16:32:06.9212758+07:00'', NULL, NULL, N''Produk jadi siap kirim'', CAST(1 AS bit), N''ITM002'', N''Finished Product B'', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, N''PCS'', NULL, NULL, NULL, 2.5),
    (3, N''Packaging'', ''2026-03-12T16:32:06.9212761+07:00'', NULL, NULL, N''Material packaging'', CAST(1 AS bit), N''ITM003'', N''Packaging Material'', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, N''BOX'', NULL, NULL, NULL, 0.5)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ItemId', N'Category', N'CreatedDate', N'Customer', N'CustomerPartNumber', N'Description', N'IsActive', N'ItemCode', N'ItemName', N'KanbanType', N'MaxStock', N'MinStock', N'NoRack', N'Plant', N'QtyLot', N'ROP', N'Rack', N'RackMax', N'RackMin', N'Unit', N'UpdatedDate', N'VIN', N'Volume', N'Weight') AND [object_id] = OBJECT_ID(N'[Items]'))
        SET IDENTITY_INSERT [Items] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Module] ON [ActivityLogs] ([Module]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Module_Action] ON [ActivityLogs] ([Module], [Action]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ActivityLogs_Timestamp] ON [ActivityLogs] ([Timestamp]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area] ON [Customers] ([CustomerCode], [CustomerName], [Route], [Cycle], [Docking], [Pickup], [ETD], [Range], [SKID], [Area]) WHERE [Route] IS NOT NULL AND [Cycle] IS NOT NULL AND [Docking] IS NOT NULL AND [Pickup] IS NOT NULL AND [ETD] IS NOT NULL AND [Range] IS NOT NULL AND [SKID] IS NOT NULL AND [Area] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliveryItems_ItemId] ON [DeliveryItems] ([ItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliveryItems_ScheduleId_ItemId] ON [DeliveryItems] ([ScheduleId], [ItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliverySchedules_CreatedDate] ON [DeliverySchedules] ([CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliverySchedules_CustomerId_ScheduledDate] ON [DeliverySchedules] ([CustomerId], [ScheduledDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliverySchedules_DockId] ON [DeliverySchedules] ([DockId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliverySchedules_ScheduledDate] ON [DeliverySchedules] ([ScheduledDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_DeliverySchedules_ScheduleNumber] ON [DeliverySchedules] ([ScheduleNumber]) WHERE [ScheduleNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_DeliverySchedules_Status] ON [DeliverySchedules] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_Docks_CustomerId] ON [Docks] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_Items_CreatedDate] ON [Items] ([CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Items_ItemCode] ON [Items] ([ItemCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_Items_VIN] ON [Items] ([VIN]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PreparationRecords_CreatedDate] ON [PreparationRecords] ([CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PreparationRecords_Label] ON [PreparationRecords] ([Label]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PreparationRecords_ScheduleId] ON [PreparationRecords] ([ScheduleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PreparationRecords_Tag] ON [PreparationRecords] ([Tag]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PreparationRecords_Tag_Label] ON [PreparationRecords] ([Tag], [Label]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PullingRecords_CreatedDate] ON [PullingRecords] ([CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PullingRecords_ItemId] ON [PullingRecords] ([ItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PullingRecords_Label] ON [PullingRecords] ([Label]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PullingRecords_Tag] ON [PullingRecords] ([Tag]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_PullingRecords_Tag_Label] ON [PullingRecords] ([Tag], [Label]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ScanNGLogs_CreatedBy] ON [ScanNGLogs] ([CreatedBy]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ScanNGLogs_CreatedDate] ON [ScanNGLogs] ([CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ScanNGLogs_Module] ON [ScanNGLogs] ([Module]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_ScanNGLogs_Module_CreatedDate] ON [ScanNGLogs] ([Module], [CreatedDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_StockSnapshots_ItemCode_SnapshotDate] ON [StockSnapshots] ([ItemCode], [SnapshotDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_StockSnapshots_Plant_SnapshotDate] ON [StockSnapshots] ([Plant], [SnapshotDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_StockSnapshots_SnapshotDate] ON [StockSnapshots] ([SnapshotDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_UserDockAccesses_DockId] ON [UserDockAccesses] ([DockId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_UserDockAccesses_UserId_DockId] ON [UserDockAccesses] ([UserId], [DockId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_UserDocks_CustomerId] ON [UserDocks] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_UserDocks_UserId] ON [UserDocks] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE INDEX [IX_UserDocks_UserId1] ON [UserDocks] ([UserId1]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260312093207_InitialProductionSQLServer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260312093207_InitialProductionSQLServer', N'8.0.0');
END;
GO

COMMIT;
GO

