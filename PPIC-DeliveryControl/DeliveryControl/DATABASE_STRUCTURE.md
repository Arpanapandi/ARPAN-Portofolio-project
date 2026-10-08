# Struktur Database - Delivery Control System

## Entity Relationship Diagram (ERD)

```
┌─────────────────┐         ┌──────────────────┐
│   Customers     │────┬───>│     Docks        │
│                 │    │    │                  │
│ - CustomerId    │    │    │ - DockId         │
│ - CustomerCode  │    │    │ - CustomerId (FK)│
│ - CustomerName  │    │    │ - DockCode       │
│ - Address       │    │    │ - DockName       │
│ - ContactPerson │    │    │ - Location       │
│ - Phone         │    │    │ - IsActive       │
│ - Email         │    │    └──────────────────┘
│ - IsActive      │    │             │
└─────────────────┘    │             │
         │             │             │
         │             │             V
         │             │    ┌──────────────────────┐
         │             └───>│ DeliverySchedules    │
         │                  │                      │
         └─────────────────>│ - ScheduleId         │
                            │ - ScheduleNumber     │
                            │ - CustomerId (FK)    │
                            │ - DockId (FK)        │
                            │ - ScheduledDate      │
                            │ - ScheduledTimeStart │
                            │ - ScheduledTimeEnd   │
                            │ - ActualStartTime    │
                            │ - ActualEndTime      │
                            │ - Status             │
                            │ - VehicleNumber      │
                            │ - DriverName         │
                            │ - DriverPhone        │
                            │ - Notes              │
                            └──────────────────────┘
                                     │
                                     │
                                     V
                            ┌──────────────────────┐
                            │   DeliveryItems      │
                            │                      │
                            │ - DeliveryItemId     │
┌──────────────────┐       │ - ScheduleId (FK)    │
│     Items        │<──────│ - ItemId (FK)        │
│                  │       │ - Quantity           │
│ - ItemId         │       │ - ActualQuantity     │
│ - ItemCode       │       │ - Unit               │
│ - ItemName       │       │ - TotalWeight        │
│ - Description    │       │ - TotalVolume        │
│ - Unit           │       │ - Notes              │
│ - Category       │       │ - IsCompleted        │
│ - Weight         │       └──────────────────────┘
│ - Volume         │
│ - IsActive       │
└──────────────────┘
```

## Detail Tabel

### 1. Customers
**Deskripsi**: Master data customer/pelanggan

| Column Name    | Data Type    | Constraint    | Description                |
|----------------|--------------|---------------|----------------------------|
| CustomerId     | int          | PK, Identity  | Primary Key                |
| CustomerCode   | varchar(20)  | NOT NULL, UQ  | Kode unique customer       |
| CustomerName   | varchar(200) | NOT NULL      | Nama customer              |
| Address        | varchar(500) | NULL          | Alamat                     |
| ContactPerson  | varchar(100) | NULL          | Nama contact person        |
| Phone          | varchar(50)  | NULL          | Nomor telepon              |
| Email          | varchar(100) | NULL          | Email address              |
| IsActive       | bit          | NOT NULL      | Status aktif (default: 1)  |
| CreatedDate    | datetime     | NOT NULL      | Tanggal dibuat             |
| UpdatedDate    | datetime     | NULL          | Tanggal update             |

**Indexes**:
- IX_Customers_CustomerCode (Unique)

---

### 2. Docks
**Deskripsi**: Area loading/dock per customer

| Column Name | Data Type    | Constraint    | Description                |
|-------------|--------------|---------------|----------------------------|
| DockId      | int          | PK, Identity  | Primary Key                |
| CustomerId  | int          | FK, NOT NULL  | Foreign Key ke Customers   |
| DockCode    | varchar(50)  | NOT NULL, UQ  | Kode unique dock           |
| DockName    | varchar(200) | NOT NULL      | Nama dock                  |
| Location    | varchar(500) | NULL          | Lokasi dock                |
| Description | varchar(1000)| NULL          | Deskripsi                  |
| IsActive    | bit          | NOT NULL      | Status aktif (default: 1)  |
| CreatedDate | datetime     | NOT NULL      | Tanggal dibuat             |
| UpdatedDate | datetime     | NULL          | Tanggal update             |

**Indexes**:
- IX_Docks_DockCode (Unique)
- IX_Docks_CustomerId

**Foreign Keys**:
- FK_Docks_Customers (CustomerId → Customers.CustomerId) ON DELETE RESTRICT

---

### 3. Items
**Deskripsi**: Master data item/produk

| Column Name | Data Type     | Constraint    | Description                |
|-------------|---------------|---------------|----------------------------|
| ItemId      | int           | PK, Identity  | Primary Key                |
| ItemCode    | varchar(50)   | NOT NULL, UQ  | Kode unique item           |
| ItemName    | varchar(200)  | NOT NULL      | Nama item                  |
| Description | varchar(1000) | NULL          | Deskripsi item             |
| Unit        | varchar(20)   | NULL          | Satuan (PCS, BOX, KG, dll) |
| Category    | varchar(100)  | NULL          | Kategori item              |
| Weight      | decimal(18,2) | NULL          | Berat dalam KG             |
| Volume      | decimal(18,2) | NULL          | Volume dalam M3            |
| IsActive    | bit           | NOT NULL      | Status aktif (default: 1)  |
| CreatedDate | datetime      | NOT NULL      | Tanggal dibuat             |
| UpdatedDate | datetime      | NULL          | Tanggal update             |

**Indexes**:
- IX_Items_ItemCode (Unique)

---

### 4. DeliverySchedules
**Deskripsi**: Jadwal delivery

| Column Name        | Data Type    | Constraint    | Description                    |
|--------------------|--------------|---------------|--------------------------------|
| ScheduleId         | int          | PK, Identity  | Primary Key                    |
| ScheduleNumber     | varchar(50)  | NOT NULL, UQ  | Nomor schedule unique          |
| CustomerId         | int          | FK, NOT NULL  | Foreign Key ke Customers       |
| DockId             | int          | FK, NOT NULL  | Foreign Key ke Docks           |
| ScheduledDate      | date         | NOT NULL      | Tanggal schedule               |
| ScheduledTimeStart | time         | NOT NULL      | Waktu mulai schedule           |
| ScheduledTimeEnd   | time         | NOT NULL      | Waktu selesai schedule         |
| ActualStartTime    | datetime     | NULL          | Waktu mulai actual             |
| ActualEndTime      | datetime     | NULL          | Waktu selesai actual           |
| Status             | varchar(20)  | NOT NULL      | Status schedule                |
| VehicleNumber      | varchar(100) | NULL          | Nomor kendaraan                |
| DriverName         | varchar(100) | NULL          | Nama driver                    |
| DriverPhone        | varchar(50)  | NULL          | Telepon driver                 |
| Notes              | varchar(1000)| NULL          | Catatan                        |
| CreatedDate        | datetime     | NOT NULL      | Tanggal dibuat                 |
| CreatedBy          | varchar(100) | NULL          | User yang membuat              |
| UpdatedDate        | datetime     | NULL          | Tanggal update                 |
| UpdatedBy          | varchar(100) | NULL          | User yang update               |

**Indexes**:
- IX_DeliverySchedules_ScheduleNumber (Unique)
- IX_DeliverySchedules_ScheduledDate
- IX_DeliverySchedules_Status
- IX_DeliverySchedules_CustomerId_ScheduledDate

**Foreign Keys**:
- FK_DeliverySchedules_Customers (CustomerId → Customers.CustomerId) ON DELETE RESTRICT
- FK_DeliverySchedules_Docks (DockId → Docks.DockId) ON DELETE RESTRICT

**Status Values**:
- `Scheduled` - Delivery belum dimulai
- `In Progress` - Delivery sedang berjalan
- `Completed` - Delivery selesai
- `Cancelled` - Delivery dibatalkan
- `Delayed` - Delivery terlambat

---

### 5. DeliveryItems
**Deskripsi**: Detail item per schedule delivery

| Column Name     | Data Type     | Constraint    | Description                |
|-----------------|---------------|---------------|----------------------------|
| DeliveryItemId  | int           | PK, Identity  | Primary Key                |
| ScheduleId      | int           | FK, NOT NULL  | Foreign Key ke Schedules   |
| ItemId          | int           | FK, NOT NULL  | Foreign Key ke Items       |
| Quantity        | decimal(18,2) | NOT NULL      | Quantity yang direncanakan |
| ActualQuantity  | decimal(18,2) | NULL          | Quantity actual            |
| Unit            | varchar(20)   | NULL          | Satuan                     |
| TotalWeight     | decimal(18,2) | NULL          | Total berat                |
| TotalVolume     | decimal(18,2) | NULL          | Total volume               |
| Notes           | varchar(1000) | NULL          | Catatan                    |
| IsCompleted     | bit           | NOT NULL      | Status completed           |
| CreatedDate     | datetime      | NOT NULL      | Tanggal dibuat             |
| UpdatedDate     | datetime      | NULL          | Tanggal update             |

**Indexes**:
- IX_DeliveryItems_ScheduleId_ItemId

**Foreign Keys**:
- FK_DeliveryItems_DeliverySchedules (ScheduleId → DeliverySchedules.ScheduleId) ON DELETE CASCADE
- FK_DeliveryItems_Items (ItemId → Items.ItemId) ON DELETE RESTRICT

---

## Calculated Fields (Not Mapped)

### DeliverySchedule
- **DelayStatus** (string): Calculated from ActualStartTime vs ScheduledDate + ScheduledTimeStart
  - "Late" - Jika actual start > scheduled start
  - "On Time" - Jika actual start <= scheduled start
  
- **DelayDuration** (TimeSpan): Durasi keterlambatan dalam minutes

### DeliveryItem
- **VarianceQuantity** (decimal): ActualQuantity - Quantity (selisih quantity)

---

## Sample Data (Seed Data)

### Customers
- CUST001: PT ABC Manufacturing
- CUST002: PT XYZ Industries

### Docks
- DOCK-A1: Dock A1 - Raw Material (Customer: CUST001)
- DOCK-A2: Dock A2 - Finished Goods (Customer: CUST001)
- DOCK-B1: Dock B1 - Main Loading (Customer: CUST002)

### Items
- ITM001: Raw Material A
- ITM002: Finished Product B
- ITM003: Packaging Material

---

## Query Examples

### 1. Mendapatkan Schedule Hari Ini dengan Delay Info
```sql
SELECT 
    ds.ScheduleNumber,
    c.CustomerName,
    d.DockName,
    ds.ScheduledDate,
    ds.ScheduledTimeStart,
    ds.ActualStartTime,
    CASE 
        WHEN ds.ActualStartTime > DATEADD(MINUTE, 0, CAST(ds.ScheduledDate AS DATETIME) + CAST(ds.ScheduledTimeStart AS DATETIME))
        THEN 'Late'
        ELSE 'On Time'
    END AS DelayStatus
FROM DeliverySchedules ds
INNER JOIN Customers c ON ds.CustomerId = c.CustomerId
INNER JOIN Docks d ON ds.DockId = d.DockId
WHERE CAST(ds.ScheduledDate AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY ds.ScheduledTimeStart
```

### 2. Mendapatkan Item Delivery dengan Variance
```sql
SELECT 
    ds.ScheduleNumber,
    i.ItemName,
    di.Quantity AS PlannedQty,
    di.ActualQuantity,
    (di.ActualQuantity - di.Quantity) AS Variance
FROM DeliveryItems di
INNER JOIN DeliverySchedules ds ON di.ScheduleId = ds.ScheduleId
INNER JOIN Items i ON di.ItemId = i.ItemId
WHERE ds.ScheduleId = @ScheduleId
```

### 3. Dashboard Statistics
```sql
-- Total Schedule Today
SELECT COUNT(*) FROM DeliverySchedules 
WHERE CAST(ScheduledDate AS DATE) = CAST(GETDATE() AS DATE)

-- In Progress Count
SELECT COUNT(*) FROM DeliverySchedules 
WHERE Status = 'In Progress'

-- Delayed Deliveries Today
SELECT COUNT(*) FROM DeliverySchedules 
WHERE CAST(ScheduledDate AS DATE) = CAST(GETDATE() AS DATE)
AND ActualStartTime > DATEADD(MINUTE, 0, CAST(ScheduledDate AS DATETIME) + CAST(ScheduledTimeStart AS DATETIME))
```

---

## Database Maintenance

### Backup
```sql
BACKUP DATABASE PPIC_DeliveryControl 
TO DISK = 'C:\Backup\PPIC_DeliveryControl.bak'
WITH FORMAT, COMPRESSION
```

### Restore
```sql
RESTORE DATABASE PPIC_DeliveryControl 
FROM DISK = 'C:\Backup\PPIC_DeliveryControl.bak'
WITH REPLACE
```

### Index Maintenance
```sql
-- Rebuild indexes
ALTER INDEX ALL ON Customers REBUILD
ALTER INDEX ALL ON Docks REBUILD
ALTER INDEX ALL ON Items REBUILD
ALTER INDEX ALL ON DeliverySchedules REBUILD
ALTER INDEX ALL ON DeliveryItems REBUILD
```

