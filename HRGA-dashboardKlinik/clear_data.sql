CREATE TABLE [ActivityLog] (
    [Id] int NOT NULL IDENTITY,
    [Action] nvarchar(100) NOT NULL,
    [Description] nvarchar(500) NOT NULL,
    [EntityType] nvarchar(100) NOT NULL,
    [EntityId] int NULL,
    [Timestamp] datetime2 NOT NULL,
    CONSTRAINT [PK_ActivityLog] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Pasien] (
    [Id] int NOT NULL IDENTITY,
    [NamaPasien] nvarchar(200) NOT NULL,
    [NPK] nvarchar(20) NOT NULL,
    [Plant] nvarchar(50) NOT NULL,
    [Departemen] nvarchar(100) NOT NULL,
    [JenisKelamin] nvarchar(1) NOT NULL,
    [TanggalTerdaftar] datetime2 NOT NULL,
    CONSTRAINT [PK_Pasien] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [KunjunganKlinik] (
    [Id] int NOT NULL IDENTITY,
    [Timestamp] datetime2 NOT NULL,
    [TanggalKunjungan] datetime2 NOT NULL,
    [NamaPasien] nvarchar(200) NOT NULL,
    [NPK] nvarchar(20) NOT NULL,
    [Plant] nvarchar(50) NOT NULL,
    [Departemen] nvarchar(100) NOT NULL,
    [JenisKelamin] nvarchar(1) NOT NULL,
    [Keluhan] nvarchar(500) NOT NULL,
    [Diagnosa] nvarchar(500) NOT NULL,
    [Dokter] nvarchar(200) NOT NULL,
    [Status] nvarchar(50) NOT NULL,
    [PasienId] int NULL,
    CONSTRAINT [PK_KunjunganKlinik] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_KunjunganKlinik_Pasien_PasienId] FOREIGN KEY ([PasienId]) REFERENCES [Pasien] ([Id]) ON DELETE SET NULL
);
GO


CREATE TABLE [Users] (
    [Id] int NOT NULL IDENTITY,
    [Nama] nvarchar(100) NOT NULL,
    [Username] nvarchar(50) NOT NULL,
    [Password] nvarchar(255) NOT NULL,
    [NPK] nvarchar(4) NOT NULL,
    [Role] nvarchar(50) NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [TanggalDibuat] datetime2 NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AntrianKlinik] (
    [Id] int NOT NULL IDENTITY,
    [NomorAntrian] nvarchar(20) NOT NULL,
    [NomorUrut] int NOT NULL,
    [Tanggal] datetime2 NOT NULL,
    [WaktuDaftar] datetime2 NOT NULL,
    [NPK] nvarchar(4) NOT NULL,
    [NamaPasien] nvarchar(200) NOT NULL,
    [Plant] nvarchar(50) NOT NULL,
    [Departemen] nvarchar(100) NOT NULL,
    [JenisKelamin] nvarchar(1) NOT NULL,
    [Keluhan] nvarchar(500) NOT NULL,
    [StatusAntrian] nvarchar(50) NOT NULL,
    [WaktuDipanggil] datetime2 NULL,
    [WaktuSelesai] datetime2 NULL,
    [PasienId] int NULL,
    [KunjunganId] int NULL,
    CONSTRAINT [PK_AntrianKlinik] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AntrianKlinik_Pasien_PasienId] FOREIGN KEY ([PasienId]) REFERENCES [Pasien] ([Id]),
    CONSTRAINT [FK_AntrianKlinik_KunjunganKlinik_KunjunganId] FOREIGN KEY ([KunjunganId]) REFERENCES [KunjunganKlinik] ([Id])
);
GO


CREATE INDEX [IX_ActivityLog_Timestamp] ON [ActivityLog] ([Timestamp]);
GO


CREATE INDEX [IX_AntrianKlinik_NPK] ON [AntrianKlinik] ([NPK]);
GO


CREATE INDEX [IX_AntrianKlinik_StatusAntrian] ON [AntrianKlinik] ([StatusAntrian]);
GO


CREATE INDEX [IX_AntrianKlinik_Tanggal] ON [AntrianKlinik] ([Tanggal]);
GO


CREATE INDEX [IX_KunjunganKlinik_NPK] ON [KunjunganKlinik] ([NPK]);
GO


CREATE INDEX [IX_KunjunganKlinik_PasienId] ON [KunjunganKlinik] ([PasienId]);
GO


CREATE INDEX [IX_KunjunganKlinik_Status] ON [KunjunganKlinik] ([Status]);
GO


CREATE INDEX [IX_KunjunganKlinik_TanggalKunjungan] ON [KunjunganKlinik] ([TanggalKunjungan]);
GO


CREATE INDEX [IX_Pasien_Departemen] ON [Pasien] ([Departemen]);
GO


CREATE INDEX [IX_Pasien_NamaPasien] ON [Pasien] ([NamaPasien]);
GO


CREATE UNIQUE INDEX [IX_Pasien_NPK] ON [Pasien] ([NPK]);
GO


CREATE INDEX [IX_Pasien_Plant] ON [Pasien] ([Plant]);
GO


CREATE INDEX [IX_Users_NPK] ON [Users] ([NPK]);
GO


CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
GO


-- Seed default user
IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [Username] = 'admin')
BEGIN
    INSERT INTO [Users] ([Nama], [Username], [Password], [NPK], [Role], [Status], [TanggalDibuat])
    VALUES ('Administrator', 'admin', 'admin123', '1001', 'Admin', 'Aktif', GETDATE());
END
GO



