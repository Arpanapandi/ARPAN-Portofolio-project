-- ============================================================
-- Script verifikasi tabel production vs yang diharapkan
-- Jalankan di SQL Server: ppic_DeliveryControl
-- ============================================================

-- 1. Cek semua tabel yang ada
SELECT 
    t.name AS TableName,
    p.rows AS RowCount
FROM sys.tables t
JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0,1)
ORDER BY t.name;

-- 2. Cek migration history (migration apa saja yang sudah diapply)
SELECT MigrationId, ProductVersion FROM [__EFMigrationsHistory] ORDER BY MigrationId;

-- 3. Cek kolom tabel Customers (termasuk RemarkOrder dari migration terbaru)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Customers'
ORDER BY ORDINAL_POSITION;

-- 4. Cek kolom tabel DeliverySchedules (termasuk IsLeaderVerified, LeaderVerifiedAt, LeaderVerifiedBy)
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'DeliverySchedules'
ORDER BY ORDINAL_POSITION;

-- 5. Cek kolom tabel Users
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Users'
ORDER BY ORDINAL_POSITION;
