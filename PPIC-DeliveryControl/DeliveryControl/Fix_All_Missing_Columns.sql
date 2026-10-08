-- Script untuk menambahkan semua kolom yang mungkin hilang
-- Script ini aman dijalankan berulang kali (idempotent)
-- Gunakan script ini untuk memastikan semua kolom yang diperlukan sudah ada

USE [PPIC_DeliveryControl];
GO

PRINT '========================================';
PRINT '  Fix Missing Columns - Delivery Control';
PRINT '========================================';
PRINT '';

-- Fungsi helper untuk menambahkan kolom jika belum ada
DECLARE @TableName NVARCHAR(128);
DECLARE @ColumnName NVARCHAR(128);
DECLARE @DataType NVARCHAR(128);
DECLARE @IsNullable NVARCHAR(10);
DECLARE @SQL NVARCHAR(MAX);

-- 1. Fix EnterDockTime
SET @TableName = 'DeliverySchedules';
SET @ColumnName = 'EnterDockTime';
SET @DataType = 'datetime2';
SET @IsNullable = 'NULL';

IF EXISTS (SELECT * FROM sys.tables WHERE name = @TableName AND type = 'U')
BEGIN
    IF NOT EXISTS (
        SELECT * 
        FROM sys.columns 
        WHERE object_id = OBJECT_ID(@TableName) 
        AND name = @ColumnName
    )
    BEGIN
        SET @SQL = 'ALTER TABLE [' + @TableName + '] ADD [' + @ColumnName + '] ' + @DataType + ' ' + @IsNullable + ';';
        EXEC sp_executesql @SQL;
        PRINT '✓ Kolom ' + @ColumnName + ' ditambahkan ke tabel ' + @TableName;
    END
    ELSE
    BEGIN
        PRINT '✓ Kolom ' + @ColumnName + ' sudah ada di tabel ' + @TableName;
    END
END
ELSE
BEGIN
    PRINT '✗ Tabel ' + @TableName + ' tidak ditemukan';
END
GO

-- 2. Fix ActualEnterDockTime
DECLARE @TableName2 NVARCHAR(128) = 'DeliverySchedules';
DECLARE @ColumnName2 NVARCHAR(128) = 'ActualEnterDockTime';
DECLARE @DataType2 NVARCHAR(128) = 'datetime2';
DECLARE @IsNullable2 NVARCHAR(10) = 'NULL';
DECLARE @SQL2 NVARCHAR(MAX);

IF EXISTS (SELECT * FROM sys.tables WHERE name = @TableName2 AND type = 'U')
BEGIN
    IF NOT EXISTS (
        SELECT * 
        FROM sys.columns 
        WHERE object_id = OBJECT_ID(@TableName2) 
        AND name = @ColumnName2
    )
    BEGIN
        SET @SQL2 = 'ALTER TABLE [' + @TableName2 + '] ADD [' + @ColumnName2 + '] ' + @DataType2 + ' ' + @IsNullable2 + ';';
        EXEC sp_executesql @SQL2;
        PRINT '✓ Kolom ' + @ColumnName2 + ' ditambahkan ke tabel ' + @TableName2;
    END
    ELSE
    BEGIN
        PRINT '✓ Kolom ' + @ColumnName2 + ' sudah ada di tabel ' + @TableName2;
    END
END
ELSE
BEGIN
    PRINT '✗ Tabel ' + @TableName2 + ' tidak ditemukan';
END
GO

-- Update migration history untuk EnterDockTime
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20250120000000_AddEnterDockTimeToDeliverySchedule'
)
BEGIN
    IF EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory' AND type = 'U')
    BEGIN
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20250120000000_AddEnterDockTimeToDeliverySchedule', N'8.0.0');
        PRINT '✓ Migration history untuk EnterDockTime diupdate';
    END
END
ELSE
BEGIN
    PRINT '✓ Migration history untuk EnterDockTime sudah ada';
END
GO

-- Update migration history untuk ActualEnterDockTime
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20251124161240_AddActualEnterDockTimeToDeliverySchedule'
)
BEGIN
    IF EXISTS (SELECT * FROM sys.tables WHERE name = '__EFMigrationsHistory' AND type = 'U')
    BEGIN
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20251124161240_AddActualEnterDockTimeToDeliverySchedule', N'8.0.0');
        PRINT '✓ Migration history untuk ActualEnterDockTime diupdate';
    END
END
ELSE
BEGIN
    PRINT '✓ Migration history untuk ActualEnterDockTime sudah ada';
END
GO

-- Verifikasi akhir
PRINT '';
PRINT '=== Verifikasi Kolom ===';

IF EXISTS (
    SELECT * 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID('DeliverySchedules') 
    AND name = 'EnterDockTime'
)
BEGIN
    PRINT '✓ EnterDockTime: ADA';
END
ELSE
BEGIN
    PRINT '✗ EnterDockTime: TIDAK ADA';
END

IF EXISTS (
    SELECT * 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID('DeliverySchedules') 
    AND name = 'ActualEnterDockTime'
)
BEGIN
    PRINT '✓ ActualEnterDockTime: ADA';
END
ELSE
BEGIN
    PRINT '✗ ActualEnterDockTime: TIDAK ADA';
END

PRINT '';
PRINT '========================================';
PRINT '  Selesai';
PRINT '========================================';
GO

