-- Script untuk update status Machine dari Running/Down/Idle/NoLoading menjadi Aktif/TidakAktif
-- Jalankan script ini di SQL Server Management Studio

USE [OeeSystemDb]
GO

-- Update status: Running, Idle, NoLoading -> Aktif
UPDATE Machines 
SET Status = N'Aktif' 
WHERE Status IN (N'Running', N'Idle', N'NoLoading')
GO

-- Update status: Down -> TidakAktif
UPDATE Machines 
SET Status = N'TidakAktif' 
WHERE Status = N'Down'
GO

-- Verifikasi hasil update
SELECT Id, Name, Status, 
    CASE 
        WHEN Status = N'Aktif' THEN 'Hijau (Aktif)'
        WHEN Status = N'TidakAktif' THEN 'Merah (Tidak Aktif)'
        ELSE 'Unknown'
    END AS StatusDescription
FROM Machines
ORDER BY Id
GO

PRINT 'Update status Machine selesai!'
PRINT 'Running, Idle, NoLoading -> Aktif'
PRINT 'Down -> TidakAktif'
GO

