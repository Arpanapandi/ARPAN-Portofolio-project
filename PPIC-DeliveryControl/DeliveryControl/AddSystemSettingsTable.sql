-- Script untuk menambahkan tabel SystemSettings
-- Jalankan script ini di SQL Server Management Studio atau melalui command line

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SystemSettings]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SystemSettings] (
        [Id] int IDENTITY(1,1) NOT NULL,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(500) NOT NULL,
        [Description] nvarchar(200) NULL,
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [dbo].[SystemSettings] ([Key]);

    PRINT 'Tabel SystemSettings berhasil dibuat.';
END
ELSE
BEGIN
    PRINT 'Tabel SystemSettings sudah ada.';
END
GO

