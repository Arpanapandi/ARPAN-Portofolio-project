-- Script untuk membuat tabel ProductNgTypes
-- Tabel junction untuk relasi many-to-many antara Product dan NgType

USE [OeeSystemDb]
GO

-- Cek apakah tabel sudah ada
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ProductNgTypes]') AND type in (N'U'))
BEGIN
    -- Buat tabel ProductNgTypes
    CREATE TABLE [dbo].[ProductNgTypes] (
        [ProductId] INT NOT NULL,
        [NgTypeId] INT NOT NULL,
        CONSTRAINT [PK_ProductNgTypes] PRIMARY KEY CLUSTERED ([ProductId] ASC, [NgTypeId] ASC),
        CONSTRAINT [FK_ProductNgTypes_Products] FOREIGN KEY ([ProductId]) 
            REFERENCES [dbo].[Products] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductNgTypes_NgTypes] FOREIGN KEY ([NgTypeId]) 
            REFERENCES [dbo].[NgTypes] ([Id]) ON DELETE NO ACTION
    )
    
    PRINT 'Tabel ProductNgTypes berhasil dibuat!'
END
ELSE
BEGIN
    PRINT 'Tabel ProductNgTypes sudah ada.'
END
GO

-- Buat index untuk performa query (jika belum ada)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductNgTypes_ProductId' AND object_id = OBJECT_ID('ProductNgTypes'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ProductNgTypes_ProductId] 
        ON [dbo].[ProductNgTypes] ([ProductId] ASC)
    PRINT 'Index IX_ProductNgTypes_ProductId berhasil dibuat!'
END
ELSE
BEGIN
    PRINT 'Index IX_ProductNgTypes_ProductId sudah ada.'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ProductNgTypes_NgTypeId' AND object_id = OBJECT_ID('ProductNgTypes'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_ProductNgTypes_NgTypeId] 
        ON [dbo].[ProductNgTypes] ([NgTypeId] ASC)
    PRINT 'Index IX_ProductNgTypes_NgTypeId berhasil dibuat!'
END
ELSE
BEGIN
    PRINT 'Index IX_ProductNgTypes_NgTypeId sudah ada.'
END
GO

