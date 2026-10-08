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
    "CreatedDate" TEXT NOT NULL,
    "UpdatedDate" TEXT NULL
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
    CONSTRAINT "FK_DeliverySchedules_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE RESTRICT
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
    CONSTRAINT "FK_PullingRecords_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("ItemId") ON DELETE CASCADE
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
    CONSTRAINT "FK_PreparationRecords_DeliverySchedules_ScheduleId" FOREIGN KEY ("ScheduleId") REFERENCES "DeliverySchedules" ("ScheduleId") ON DELETE CASCADE
);

CREATE INDEX "IX_ActivityLogs_Module" ON "ActivityLogs" ("Module");

CREATE INDEX "IX_ActivityLogs_Module_Action" ON "ActivityLogs" ("Module", "Action");

CREATE INDEX "IX_ActivityLogs_Timestamp" ON "ActivityLogs" ("Timestamp");

CREATE UNIQUE INDEX "IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area" ON "Customers" ("CustomerCode", "CustomerName", "Route", "Cycle", "Docking", "Pickup", "ETD", "Range", "SKID", "Area");

CREATE INDEX "IX_DeliveryItems_ItemId" ON "DeliveryItems" ("ItemId");

CREATE INDEX "IX_DeliveryItems_ScheduleId_ItemId" ON "DeliveryItems" ("ScheduleId", "ItemId");

CREATE INDEX "IX_DeliverySchedules_CreatedDate" ON "DeliverySchedules" ("CreatedDate");

CREATE INDEX "IX_DeliverySchedules_CustomerId_ScheduledDate" ON "DeliverySchedules" ("CustomerId", "ScheduledDate");

CREATE INDEX "IX_DeliverySchedules_ScheduledDate" ON "DeliverySchedules" ("ScheduledDate");

CREATE UNIQUE INDEX "IX_DeliverySchedules_ScheduleNumber" ON "DeliverySchedules" ("ScheduleNumber");

CREATE INDEX "IX_DeliverySchedules_Status" ON "DeliverySchedules" ("Status");

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

CREATE UNIQUE INDEX "IX_SystemSettings_Key" ON "SystemSettings" ("Key");

CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260221042446_InitialSqlite', '8.0.0');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UserDocks" (
    "UserDockId" INTEGER NOT NULL CONSTRAINT "PK_UserDocks" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "CustomerId" INTEGER NOT NULL,
    "AssignedDate" TEXT NOT NULL,
    CONSTRAINT "FK_UserDocks_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE CASCADE,
    CONSTRAINT "FK_UserDocks_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("UserId") ON DELETE CASCADE
);

UPDATE "Customers" SET "CreatedDate" = '2026-02-24 23:51:21.8100865'
WHERE "CustomerId" = 1;
SELECT changes();


UPDATE "Customers" SET "CreatedDate" = '2026-02-24 23:51:21.8100868'
WHERE "CustomerId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-24 23:51:21.8100999'
WHERE "ItemId" = 1;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-24 23:51:21.8101002'
WHERE "ItemId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-24 23:51:21.8101005'
WHERE "ItemId" = 3;
SELECT changes();


CREATE INDEX "IX_UserDocks_CustomerId" ON "UserDocks" ("CustomerId");

CREATE UNIQUE INDEX "IX_UserDocks_UserId_CustomerId" ON "UserDocks" ("UserId", "CustomerId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260224165122_AddUserDocks', '8.0.0');

COMMIT;

BEGIN TRANSACTION;

ALTER TABLE "DeliverySchedules" ADD "DockId" INTEGER NULL;

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

CREATE TABLE "UserDockAccesses" (
    "UserDockAccessId" INTEGER NOT NULL CONSTRAINT "PK_UserDockAccesses" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "DockId" INTEGER NOT NULL,
    "CreatedDate" TEXT NOT NULL,
    CONSTRAINT "FK_UserDockAccesses_Docks_DockId" FOREIGN KEY ("DockId") REFERENCES "Docks" ("DockId") ON DELETE CASCADE,
    CONSTRAINT "FK_UserDockAccesses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("UserId") ON DELETE CASCADE
);

UPDATE "Customers" SET "CreatedDate" = '2026-02-25 00:13:42.6014042'
WHERE "CustomerId" = 1;
SELECT changes();


UPDATE "Customers" SET "CreatedDate" = '2026-02-25 00:13:42.6014045'
WHERE "CustomerId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 00:13:42.6014324'
WHERE "ItemId" = 1;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 00:13:42.6014328'
WHERE "ItemId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 00:13:42.6014331'
WHERE "ItemId" = 3;
SELECT changes();


CREATE INDEX "IX_DeliverySchedules_DockId" ON "DeliverySchedules" ("DockId");

CREATE INDEX "IX_Docks_CustomerId" ON "Docks" ("CustomerId");

CREATE INDEX "IX_UserDockAccesses_DockId" ON "UserDockAccesses" ("DockId");

CREATE UNIQUE INDEX "IX_UserDockAccesses_UserId_DockId" ON "UserDockAccesses" ("UserId", "DockId");

CREATE TABLE "ef_temp_DeliverySchedules" (
    "ScheduleId" INTEGER NOT NULL CONSTRAINT "PK_DeliverySchedules" PRIMARY KEY AUTOINCREMENT,
    "ActPrepareTime" REAL NULL,
    "ActualEndTime" TEXT NULL,
    "ActualEnterDockTime" TEXT NULL,
    "ActualPickupTime" TEXT NULL,
    "ActualStartTime" TEXT NULL,
    "Area" TEXT NULL,
    "CreatedBy" TEXT NULL,
    "CreatedDate" TEXT NOT NULL,
    "CustomerId" INTEGER NOT NULL,
    "Cycle" TEXT NULL,
    "DockId" INTEGER NULL,
    "DriverName" TEXT NULL,
    "DriverPhone" TEXT NULL,
    "DriverStatus" TEXT NULL,
    "ETD" TEXT NULL,
    "EnterDockTime" TEXT NULL,
    "Notes" TEXT NULL,
    "PickupTime" TEXT NULL,
    "PreparationStatus" TEXT NULL,
    "Range" TEXT NULL,
    "ReadyToDockTime" TEXT NULL,
    "Route" TEXT NULL,
    "SKID" TEXT NULL,
    "ScheduleNumber" TEXT NULL,
    "ScheduledDate" TEXT NOT NULL,
    "StartPrepareTime" INTEGER NOT NULL,
    "Status" TEXT NULL,
    "StdPrepareTime" INTEGER NOT NULL,
    "TotalActualQuantity" decimal(18,2) NOT NULL,
    "TotalTargetQuantity" decimal(18,2) NOT NULL,
    "UpdatedBy" TEXT NULL,
    "UpdatedDate" TEXT NULL,
    "VehicleNumber" TEXT NULL,
    CONSTRAINT "FK_DeliverySchedules_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("CustomerId") ON DELETE RESTRICT,
    CONSTRAINT "FK_DeliverySchedules_Docks_DockId" FOREIGN KEY ("DockId") REFERENCES "Docks" ("DockId")
);

INSERT INTO "ef_temp_DeliverySchedules" ("ScheduleId", "ActPrepareTime", "ActualEndTime", "ActualEnterDockTime", "ActualPickupTime", "ActualStartTime", "Area", "CreatedBy", "CreatedDate", "CustomerId", "Cycle", "DockId", "DriverName", "DriverPhone", "DriverStatus", "ETD", "EnterDockTime", "Notes", "PickupTime", "PreparationStatus", "Range", "ReadyToDockTime", "Route", "SKID", "ScheduleNumber", "ScheduledDate", "StartPrepareTime", "Status", "StdPrepareTime", "TotalActualQuantity", "TotalTargetQuantity", "UpdatedBy", "UpdatedDate", "VehicleNumber")
SELECT "ScheduleId", "ActPrepareTime", "ActualEndTime", "ActualEnterDockTime", "ActualPickupTime", "ActualStartTime", "Area", "CreatedBy", "CreatedDate", "CustomerId", "Cycle", "DockId", "DriverName", "DriverPhone", "DriverStatus", "ETD", "EnterDockTime", "Notes", "PickupTime", "PreparationStatus", "Range", "ReadyToDockTime", "Route", "SKID", "ScheduleNumber", "ScheduledDate", "StartPrepareTime", "Status", "StdPrepareTime", "TotalActualQuantity", "TotalTargetQuantity", "UpdatedBy", "UpdatedDate", "VehicleNumber"
FROM "DeliverySchedules";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;

DROP TABLE "DeliverySchedules";

ALTER TABLE "ef_temp_DeliverySchedules" RENAME TO "DeliverySchedules";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;

CREATE INDEX "IX_DeliverySchedules_CreatedDate" ON "DeliverySchedules" ("CreatedDate");

CREATE INDEX "IX_DeliverySchedules_CustomerId_ScheduledDate" ON "DeliverySchedules" ("CustomerId", "ScheduledDate");

CREATE INDEX "IX_DeliverySchedules_DockId" ON "DeliverySchedules" ("DockId");

CREATE INDEX "IX_DeliverySchedules_ScheduledDate" ON "DeliverySchedules" ("ScheduledDate");

CREATE UNIQUE INDEX "IX_DeliverySchedules_ScheduleNumber" ON "DeliverySchedules" ("ScheduleNumber");

CREATE INDEX "IX_DeliverySchedules_Status" ON "DeliverySchedules" ("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260224171343_AddUserDockAccess', '8.0.0');

COMMIT;

BEGIN TRANSACTION;

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

UPDATE "Customers" SET "CreatedDate" = '2026-02-25 07:47:34.5951814'
WHERE "CustomerId" = 1;
SELECT changes();


UPDATE "Customers" SET "CreatedDate" = '2026-02-25 07:47:34.5951819'
WHERE "CustomerId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 07:47:34.5952022'
WHERE "ItemId" = 1;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 07:47:34.5952027'
WHERE "ItemId" = 2;
SELECT changes();


UPDATE "Items" SET "CreatedDate" = '2026-02-25 07:47:34.595203'
WHERE "ItemId" = 3;
SELECT changes();


CREATE INDEX "IX_StockSnapshots_SnapshotDate_Plant_ItemCode" ON "StockSnapshots" ("SnapshotDate", "Plant", "ItemCode");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260225004735_AddStockSnapshots', '8.0.0');

COMMIT;

BEGIN TRANSACTION;

ALTER TABLE "PullingRecords" ADD "AdjustNote" TEXT NULL;

ALTER TABLE "PullingRecords" ADD "IsManualAdjust" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260225163818_AddManualAdjustToPullingRecord', '8.0.0');

COMMIT;

