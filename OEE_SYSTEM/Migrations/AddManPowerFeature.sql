-- Migration: Add ManPower Feature
-- Date: 2024
-- Description: Menambahkan tabel ManPowers dan kolom ManPowerId di JobRuns

-- 1. Buat tabel ManPowers
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ManPowers]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ManPowers] (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(100) NOT NULL,
        [Value] INT NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_ManPowers] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    
    PRINT 'Tabel ManPowers berhasil dibuat.';
END
ELSE
BEGIN
    PRINT 'Tabel ManPowers sudah ada.';
END
GO

-- 2. Tambahkan kolom ManPowerId di tabel JobRuns
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[JobRuns]') AND name = 'ManPowerId')
BEGIN
    ALTER TABLE [dbo].[JobRuns]
    ADD [ManPowerId] INT NULL;
    
    PRINT 'Kolom ManPowerId berhasil ditambahkan ke tabel JobRuns.';
END
ELSE
BEGIN
    PRINT 'Kolom ManPowerId sudah ada di tabel JobRuns.';
END
GO

-- 3. Tambahkan Foreign Key constraint
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_JobRuns_ManPowers_ManPowerId')
BEGIN
    ALTER TABLE [dbo].[JobRuns]
    ADD CONSTRAINT [FK_JobRuns_ManPowers_ManPowerId]
    FOREIGN KEY ([ManPowerId])
    REFERENCES [dbo].[ManPowers] ([Id])
    ON DELETE SET NULL;
    
    PRINT 'Foreign Key constraint berhasil ditambahkan.';
END
ELSE
BEGIN
    PRINT 'Foreign Key constraint sudah ada.';
END
GO

-- 4. Insert data default ManPower (jika belum ada)
IF NOT EXISTS (SELECT * FROM [dbo].[ManPowers])
BEGIN
    INSERT INTO [dbo].[ManPowers] ([Name], [Value], [IsActive]) VALUES
    ('1 Orang', 1, 1),
    ('2 Orang', 2, 1),
    ('3 Orang', 3, 1),
    ('4 Orang', 4, 1),
    ('5 Orang', 5, 1);
    
    PRINT 'Data default ManPower berhasil ditambahkan.';
END
ELSE
BEGIN
    PRINT 'Data ManPower sudah ada.';
END
GO

PRINT 'Migration AddManPowerFeature selesai!';

