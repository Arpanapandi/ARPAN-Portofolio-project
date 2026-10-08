-- Script untuk menambahkan kolom EnterDockTime ke tabel DeliverySchedules
-- Script ini aman dijalankan berulang kali (idempotent)
-- Gunakan script ini jika terjadi error "kolom EnterDockTime tidak ada" saat deployment

USE [PPIC_DeliveryControl];
GO

-- Cek apakah tabel DeliverySchedules ada
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DeliverySchedules' AND type = 'U')
BEGIN
    PRINT 'Tabel DeliverySchedules ditemukan.';
    
    -- Cek apakah kolom EnterDockTime sudah ada
    IF NOT EXISTS (
        SELECT * 
        FROM sys.columns 
        WHERE object_id = OBJECT_ID('DeliverySchedules') 
        AND name = 'EnterDockTime'
    )
    BEGIN
        PRINT 'Kolom EnterDockTime belum ada. Menambahkan kolom...';
        
        -- Tambahkan kolom EnterDockTime
        ALTER TABLE [DeliverySchedules] 
        ADD [EnterDockTime] datetime2 NULL;
        
        PRINT 'Kolom EnterDockTime berhasil ditambahkan.';
    END
    ELSE
    BEGIN
        PRINT 'Kolom EnterDockTime sudah ada. Tidak perlu ditambahkan.';
    END
    
    -- Update migration history jika belum ada
    IF NOT EXISTS (
        SELECT * FROM [__EFMigrationsHistory]
        WHERE [MigrationId] = N'20250120000000_AddEnterDockTimeToDeliverySchedule'
    )
    BEGIN
        PRINT 'Menambahkan record ke migration history...';
        INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20250120000000_AddEnterDockTimeToDeliverySchedule', N'8.0.0');
        PRINT 'Migration history berhasil diupdate.';
    END
    ELSE
    BEGIN
        PRINT 'Migration history sudah ada.';
    END
END
ELSE
BEGIN
    PRINT 'ERROR: Tabel DeliverySchedules tidak ditemukan.';
    PRINT 'Pastikan tabel DeliverySchedules sudah dibuat terlebih dahulu.';
END
GO

-- Verifikasi
PRINT '';
PRINT '=== Verifikasi ===';
IF EXISTS (
    SELECT * 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID('DeliverySchedules') 
    AND name = 'EnterDockTime'
)
BEGIN
    PRINT 'SUKSES: Kolom EnterDockTime sudah ada di tabel DeliverySchedules.';
END
ELSE
BEGIN
    PRINT 'GAGAL: Kolom EnterDockTime tidak ditemukan.';
END
GO

