-- Script untuk menambahkan kolom ActualEnterDockTime jika belum ada
-- Jalankan script ini di SQL Server Management Studio atau tool database Anda

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[DeliverySchedules]') 
    AND name = 'ActualEnterDockTime'
)
BEGIN
    ALTER TABLE [DeliverySchedules] 
    ADD [ActualEnterDockTime] datetime2 NULL;
    
    PRINT 'Kolom ActualEnterDockTime berhasil ditambahkan';
END
ELSE
BEGIN
    PRINT 'Kolom ActualEnterDockTime sudah ada';
END
GO

