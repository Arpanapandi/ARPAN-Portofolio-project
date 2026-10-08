-- Script untuk menghapus data mesin lama yang tidak diinginkan
-- Hanya hapus mesin yang tidak digunakan oleh JobRuns atau relasi lainnya

USE [OeeSystemDb]
GO

-- Cek mesin yang ada
SELECT Id, Name, LineId, Status FROM Machines
GO

-- Hapus relasi ProductMachine untuk mesin yang akan dihapus
-- Ganti 'M004', 'M005', dll dengan ID mesin yang ingin dihapus
DELETE FROM ProductMachines WHERE MachineId IN ('M004', 'M005', 'M006', 'M007', 'M008')
GO

-- Hapus relasi MachineDowntimeReason untuk mesin yang akan dihapus
DELETE FROM MachineDowntimeReasons WHERE MachineId IN ('M004', 'M005', 'M006', 'M007', 'M008')
GO

-- Hapus mesin yang tidak memiliki JobRuns (pastikan tidak ada data produksi)
-- HATI-HATI: Pastikan mesin yang dihapus tidak memiliki JobRuns aktif
DELETE FROM Machines 
WHERE Id IN ('M004', 'M005', 'M006', 'M007', 'M008')
  AND Id NOT IN (SELECT DISTINCT MachineId FROM JobRuns WHERE MachineId IS NOT NULL)
GO

-- Verifikasi mesin yang tersisa
SELECT Id, Name, LineId, Status FROM Machines
GO

PRINT 'Data mesin lama berhasil dihapus!'
GO

