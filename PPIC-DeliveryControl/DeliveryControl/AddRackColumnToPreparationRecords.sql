-- Script untuk menambahkan kolom Rack dan Column ke tabel PreparationRecords
-- Jalankan script ini di SQL Server Management Studio sebelum deploy versi baru

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[PreparationRecords]') 
    AND name = 'Rack'
)
BEGIN
    ALTER TABLE [PreparationRecords] 
    ADD [Rack] NVARCHAR(5) NULL;
    
    PRINT 'Kolom Rack berhasil ditambahkan ke PreparationRecords';
END
ELSE
BEGIN
    PRINT 'Kolom Rack sudah ada di PreparationRecords';
END
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[PreparationRecords]') 
    AND name = 'Column'
)
BEGIN
    ALTER TABLE [PreparationRecords] 
    ADD [Column] INT NULL;
    
    PRINT 'Kolom Column berhasil ditambahkan ke PreparationRecords';
END
ELSE
BEGIN
    PRINT 'Kolom Column sudah ada di PreparationRecords';
END
GO
