CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;

CREATE TABLE "ActivityLogs" (
    "LogId" INTEGER NOT NULL CONSTRAINT "PK_ActivityLogs" PRIMARY KEY AUTOINCREMENT,
    "Module" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "EntityName" TEXT NULL,
    "EntityId" INTEGER NULL,
    "Description" TEXT NULL,
    "OldData" TEXT NULL,
    "NewData" TEXT NULL,
    "Timestamp" TEXT NOT NULL,
    "PerformedBy" TEXT NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL
);

CREATE TABLE "Customers" (
    "CustomerId" INTEGER NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY AUTOINCREMENT,
    "AutoCode" TEXT NULL,
    "CustomerCode" TEXT NOT NULL,
    "CustomerName" TEXT NOT NULL,
    "Route" TEXT NULL,
    "Cycle" TEXT NULL,
    "Docking" TEXT NULL,
    "Pickup" TEXT NULL,
    "ETD" TEXT NULL,
    "Range" TEXT NULL,
    "SKID" TEXT NULL,
    "Area" TEXT NULL,
    "RemarkOrder" TEXT NULL,
    "StdPrepareTime" INTEGER NOT NULL,
    "StartPrepareTime" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL
);

CREATE TABLE "ItemMappings" (
    "MappingId" INTEGER NOT NULL CONSTRAINT "PK_ItemMappings" PRIMARY KEY AUTOINCREMENT,
    "VIN" TEXT NOT NULL,
    "Customer" TEXT NOT NULL,
    "CustomerPartNumber" TEXT NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL
);

CREATE TABLE "Items" (
    "ItemId" INTEGER NOT NULL CONSTRAINT "PK_Items" PRIMARY KEY AUTOINCREMENT,
    "ItemCode" TEXT NOT NULL,
    "ItemName" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Unit" TEXT NULL,
    "Category" TEXT NULL,
    "Weight" decimal(18,2) NULL,
    "Volume" decimal(18,2) NULL,
    "MinStock" INTEGER NOT NULL,
    "MaxStock" INTEGER NOT NULL,
    "Plant" TEXT NULL,
    "Rack" TEXT NULL,
    "NoRack" INTEGER NULL,
    "Customer" TEXT NULL,
    "VIN" TEXT NULL,
    "QtyLot" INTEGER NULL,
    "RackMin" INTEGER NULL,
    "ROP" INTEGER NULL,
    "RackMax" INTEGER NULL,
    "CustomerPartNumber" TEXT NULL,
    "KanbanType" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL
);

CREATE TABLE "ScanNGLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ScanNGLogs" PRIMARY KEY AUTOINCREMENT,
    "Module" TEXT NOT NULL,
    "Tag" TEXT NOT NULL,
    "Label" TEXT NOT NULL,
    "Kanban" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "CreatedDate" TEXT NOT NULL
);

CREATE TABLE "StockSnapshots" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StockSnapshots" PRIMARY KEY AUTOINCREMENT,
    "SnapshotDate" TEXT NOT NULL,
    "SnapshotTime" TEXT NOT NULL,
    "ItemCode" TEXT NOT NULL,
    "ItemName" TEXT NOT NULL,
    "Plant" TEXT NOT NULL,
    "StockQty" INTEGER NOT NULL,
    "DaysCoverage" decimal(10,4) NOT NULL,
    "StockLevel" TEXT NOT NULL,
    "IsManual" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "SystemSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SystemSettings" PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Description" TEXT NULL
);

CREATE TABLE "Users" (
    "UserId" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "Username" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "FullName" TEXT NOT NULL,
    "Email" TEXT NULL,
    "Role" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "HasAllDockAccess" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL
);

CREATE TABLE "Docks" (
    "DockId" INTEGER NOT NULL CONSTRAINT "PK_Docks" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "DockCode" TEXT NOT NULL,
    "DockName" TEXT NOT NULL,
    "Location" TEXT NULL,
    "Description" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL,
    CONSTRAINT "FK_Docks_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE CASCADE
);

CREATE TABLE "PullingRecords" (
    "PullingId" INTEGER NOT NULL CONSTRAINT "PK_PullingRecords" PRIMARY KEY AUTOINCREMENT,
    "Plant" TEXT NOT NULL,
    "Rack" TEXT NOT NULL,
    "Column" INTEGER NOT NULL,
    "Tag" TEXT NOT NULL,
    "Label" TEXT NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "CreatedBy" TEXT NULL,
    "ItemId" INTEGER NULL,
    "Quantity" decimal(18,2) NOT NULL,
    "IsManualAdjust" INTEGER NOT NULL,
    "AdjustNote" TEXT NULL,
    "Remark" TEXT NOT NULL,
    CONSTRAINT "FK_PullingRecords_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("ItemId") ON DELETE CASCADE
);

CREATE TABLE "UserDocks" (
    "UserDockId" INTEGER NOT NULL CONSTRAINT "PK_UserDocks" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "CustomerId" INTEGER NOT NULL,
    "AssignedDate" TEXT NOT NULL,
    "UserId1" INTEGER NULL,
    CONSTRAINT "FK_UserDocks_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE CASCADE,
    CONSTRAINT "FK_UserDocks_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("UserId") ON DELETE CASCADE,
    CONSTRAINT "FK_UserDocks_Users_UserId1" FOREIGN KEY ("UserId1") REFERENCES "Users" ("UserId")
);

CREATE TABLE "DeliverySchedules" (
    "ScheduleId" INTEGER NOT NULL CONSTRAINT "PK_DeliverySchedules" PRIMARY KEY AUTOINCREMENT,
    "ScheduleNumber" TEXT NULL,
    "CustomerId" INTEGER NOT NULL,
    "Route" TEXT NULL,
    "Cycle" TEXT NULL,
    "EnterDockTime" TEXT NULL,
    "PickupTime" TEXT NULL,
    "ETD" TEXT NULL,
    "Range" TEXT NULL,
    "SKID" TEXT NULL,
    "Area" TEXT NULL,
    "TotalTargetQuantity" decimal(18,2) NOT NULL,
    "TotalActualQuantity" decimal(18,2) NOT NULL,
    "ActualPickupTime" TEXT NULL,
    "ScheduledDate" TEXT NOT NULL,
    "ActualEnterDockTime" TEXT NULL,
    "ActualStartTime" TEXT NULL,
    "ActualEndTime" TEXT NULL,
    "ReadyToDockTime" TEXT NULL,
    "ActPrepareTime" REAL NULL,
    "StartPrepareTime" INTEGER NOT NULL,
    "StdPrepareTime" INTEGER NOT NULL,
    "PreparationStatus" TEXT NULL,
    "DriverStatus" TEXT NULL,
    "Status" TEXT NULL,
    "VehicleNumber" TEXT NULL,
    "DriverName" TEXT NULL,
    "DriverPhone" TEXT NULL,
    "Notes" TEXT NULL,
    "CreatedDate" TEXT NOT NULL,
    "CreatedBy" TEXT NULL,
    "UpdatedDate" TEXT NULL,
    "UpdatedBy" TEXT NULL,
    "IsLeaderVerified" INTEGER NOT NULL,
    "LeaderVerifiedAt" TEXT NULL,
    "LeaderVerifiedBy" TEXT NULL,
    "LeaderVerifiedKanbanCount" INTEGER NOT NULL,
    "DockId" INTEGER NULL,
    CONSTRAINT "FK_DeliverySchedules_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE RESTRICT,
    CONSTRAINT "FK_DeliverySchedules_Docks_DockId" FOREIGN KEY ("DockId") REFERENCES "Docks" ("DockId")
);

CREATE TABLE "UserDockAccesses" (
    "UserDockAccessId" INTEGER NOT NULL CONSTRAINT "PK_UserDockAccesses" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "DockId" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    CONSTRAINT "FK_UserDockAccesses_Docks_DockId" FOREIGN KEY ("DockId") REFERENCES "Docks" ("DockId") ON DELETE CASCADE,
    CONSTRAINT "FK_UserDockAccesses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("UserId") ON DELETE CASCADE
);

CREATE TABLE "DeliveryItems" (
    "DeliveryItemId" INTEGER NOT NULL CONSTRAINT "PK_DeliveryItems" PRIMARY KEY AUTOINCREMENT,
    "ScheduleId" INTEGER NOT NULL,
    "ItemId" INTEGER NOT NULL,
    "Quantity" decimal(18,2) NOT NULL,
    "ActualQuantity" decimal(18,2) NULL,
    "Unit" TEXT NULL,
    "TotalWeight" decimal(18,2) NULL,
    "TotalVolume" decimal(18,2) NULL,
    "Notes" TEXT NULL,
    "IsCompleted" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL,
    CONSTRAINT "FK_DeliveryItems_DeliverySchedules_ScheduleId" FOREIGN KEY ("ScheduleId") REFERENCES "DeliverySchedules" ("ScheduleId") ON DELETE CASCADE,
    CONSTRAINT "FK_DeliveryItems_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("ItemId") ON DELETE CASCADE
);

CREATE TABLE "PreparationRecords" (
    "PreparationId" INTEGER NOT NULL CONSTRAINT "PK_PreparationRecords" PRIMARY KEY AUTOINCREMENT,
    "Plant" TEXT NOT NULL,
    "Tag" TEXT NOT NULL,
    "Label" TEXT NOT NULL,
    "Kanban" TEXT NOT NULL,
    "ScheduleId" INTEGER NULL,
    "CreatedDate" TEXT NOT NULL,
    "CreatedBy" TEXT NULL,
    "Remark" TEXT NOT NULL,
    CONSTRAINT "FK_PreparationRecords_DeliverySchedules_ScheduleId" FOREIGN KEY ("ScheduleId") REFERENCES "DeliverySchedules" ("ScheduleId") ON DELETE CASCADE
);

INSERT INTO "Customers" ("CustomerId", "Area", "AutoCode", "CreatedDate", "CustomerCode", "CustomerName", "Cycle", "Docking", "ETD", "IsActive", "Pickup", "Range", "RemarkOrder", "Route", "SKID", "StartPrepareTime", "StdPrepareTime", "UpdatedDate")
VALUES (1, NULL, NULL, '2026-03-12 16:17:07.1393278', 'CUST001', 'PT ABC Manufacturing', NULL, NULL, NULL, 1, NULL, NULL, NULL, 'Route A', '10 SKID', 0, 0, NULL);
SELECT changes();

INSERT INTO "Customers" ("CustomerId", "Area", "AutoCode", "CreatedDate", "CustomerCode", "CustomerName", "Cycle", "Docking", "ETD", "IsActive", "Pickup", "Range", "RemarkOrder", "Route", "SKID", "StartPrepareTime", "StdPrepareTime", "UpdatedDate")
VALUES (2, NULL, NULL, '2026-03-12 16:17:07.139328', 'CUST002', 'PT XYZ Industries', NULL, NULL, NULL, 1, NULL, NULL, NULL, 'Route B', '15 SKID', 0, 0, NULL);
SELECT changes();


INSERT INTO "Items" ("ItemId", "Category", "CreatedDate", "Customer", "CustomerPartNumber", "Description", "IsActive", "ItemCode", "ItemName", "KanbanType", "MaxStock", "MinStock", "NoRack", "Plant", "QtyLot", "ROP", "Rack", "RackMax", "RackMin", "Unit", "UpdatedDate", "VIN", "Volume", "Weight")
VALUES (1, 'Raw Material', '2026-03-12 16:17:07.1393384', NULL, NULL, 'Raw material untuk produksi', 1, 'ITM001', 'Raw Material A', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'KG', NULL, NULL, NULL, '1.0');
SELECT changes();

INSERT INTO "Items" ("ItemId", "Category", "CreatedDate", "Customer", "CustomerPartNumber", "Description", "IsActive", "ItemCode", "ItemName", "KanbanType", "MaxStock", "MinStock", "NoRack", "Plant", "QtyLot", "ROP", "Rack", "RackMax", "RackMin", "Unit", "UpdatedDate", "VIN", "Volume", "Weight")
VALUES (2, 'Finished Goods', '2026-03-12 16:17:07.1393407', NULL, NULL, 'Produk jadi siap kirim', 1, 'ITM002', 'Finished Product B', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'PCS', NULL, NULL, NULL, '2.5');
SELECT changes();

INSERT INTO "Items" ("ItemId", "Category", "CreatedDate", "Customer", "CustomerPartNumber", "Description", "IsActive", "ItemCode", "ItemName", "KanbanType", "MaxStock", "MinStock", "NoRack", "Plant", "QtyLot", "ROP", "Rack", "RackMax", "RackMin", "Unit", "UpdatedDate", "VIN", "Volume", "Weight")
VALUES (3, 'Packaging', '2026-03-12 16:17:07.1393409', NULL, NULL, 'Material packaging', 1, 'ITM003', 'Packaging Material', NULL, 20, 5, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'BOX', NULL, NULL, NULL, '0.5');
SELECT changes();


CREATE INDEX "IX_ActivityLogs_Module" ON "ActivityLogs" ("Module");

CREATE INDEX "IX_ActivityLogs_Module_Action" ON "ActivityLogs" ("Module", "Action");

CREATE INDEX "IX_ActivityLogs_Timestamp" ON "ActivityLogs" ("Timestamp");

CREATE UNIQUE INDEX "IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area" ON "Customers" ("CustomerCode", "CustomerName", "Route", "Cycle", "Docking", "Pickup", "ETD", "Range", "SKID", "Area");

CREATE INDEX "IX_DeliveryItems_ItemId" ON "DeliveryItems" ("ItemId");

CREATE INDEX "IX_DeliveryItems_ScheduleId_ItemId" ON "DeliveryItems" ("ScheduleId", "ItemId");

CREATE INDEX "IX_DeliverySchedules_CreatedDate" ON "DeliverySchedules" ("CreatedDate");

CREATE INDEX "IX_DeliverySchedules_CustomerId_ScheduledDate" ON "DeliverySchedules" ("CustomerId", "ScheduledDate");

CREATE INDEX "IX_DeliverySchedules_DockId" ON "DeliverySchedules" ("DockId");

CREATE INDEX "IX_DeliverySchedules_ScheduledDate" ON "DeliverySchedules" ("ScheduledDate");

CREATE UNIQUE INDEX "IX_DeliverySchedules_ScheduleNumber" ON "DeliverySchedules" ("ScheduleNumber");

CREATE INDEX "IX_DeliverySchedules_Status" ON "DeliverySchedules" ("Status");

CREATE INDEX "IX_Docks_CustomerId" ON "Docks" ("CustomerId");

CREATE INDEX "IX_Items_CreatedDate" ON "Items" ("CreatedDate");

CREATE UNIQUE INDEX "IX_Items_ItemCode" ON "Items" ("ItemCode");

CREATE INDEX "IX_Items_VIN" ON "Items" ("VIN");

CREATE INDEX "IX_PreparationRecords_CreatedDate" ON "PreparationRecords" ("CreatedDate");

CREATE INDEX "IX_PreparationRecords_Label" ON "PreparationRecords" ("Label");

CREATE INDEX "IX_PreparationRecords_ScheduleId" ON "PreparationRecords" ("ScheduleId");

CREATE INDEX "IX_PreparationRecords_Tag" ON "PreparationRecords" ("Tag");

CREATE INDEX "IX_PreparationRecords_Tag_Label" ON "PreparationRecords" ("Tag", "Label");

CREATE INDEX "IX_PullingRecords_CreatedDate" ON "PullingRecords" ("CreatedDate");

CREATE INDEX "IX_PullingRecords_ItemId" ON "PullingRecords" ("ItemId");

CREATE INDEX "IX_PullingRecords_Label" ON "PullingRecords" ("Label");

CREATE INDEX "IX_PullingRecords_Tag" ON "PullingRecords" ("Tag");

CREATE INDEX "IX_PullingRecords_Tag_Label" ON "PullingRecords" ("Tag", "Label");

CREATE INDEX "IX_ScanNGLogs_CreatedBy" ON "ScanNGLogs" ("CreatedBy");

CREATE INDEX "IX_ScanNGLogs_CreatedDate" ON "ScanNGLogs" ("CreatedDate");

CREATE INDEX "IX_ScanNGLogs_Module" ON "ScanNGLogs" ("Module");

CREATE INDEX "IX_ScanNGLogs_Module_CreatedDate" ON "ScanNGLogs" ("Module", "CreatedDate");

CREATE INDEX "IX_StockSnapshots_ItemCode_SnapshotDate" ON "StockSnapshots" ("ItemCode", "SnapshotDate");

CREATE INDEX "IX_StockSnapshots_Plant_SnapshotDate" ON "StockSnapshots" ("Plant", "SnapshotDate");

CREATE INDEX "IX_StockSnapshots_SnapshotDate" ON "StockSnapshots" ("SnapshotDate");

CREATE UNIQUE INDEX "IX_SystemSettings_Key" ON "SystemSettings" ("Key");

CREATE INDEX "IX_UserDockAccesses_DockId" ON "UserDockAccesses" ("DockId");

CREATE UNIQUE INDEX "IX_UserDockAccesses_UserId_DockId" ON "UserDockAccesses" ("UserId", "DockId");

CREATE INDEX "IX_UserDocks_CustomerId" ON "UserDocks" ("CustomerId");

CREATE INDEX "IX_UserDocks_UserId" ON "UserDocks" ("UserId");

CREATE INDEX "IX_UserDocks_UserId1" ON "UserDocks" ("UserId1");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260312091707_InitialProduction', '8.0.0');

COMMIT;

