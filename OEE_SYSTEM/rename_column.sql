-- Script untuk mengubah nama kolom IdealCycleTimeSeconds menjadi StandarCycleTime
-- Database: OeeSystemDb
-- Table: Products

USE OeeSystemDb;
GO

-- Rename kolom IdealCycleTimeSeconds menjadi StandarCycleTime
EXEC sp_rename 'Products.IdealCycleTimeSeconds', 'StandarCycleTime', 'COLUMN';
GO

-- Verifikasi perubahan
SELECT COLUMN_NAME, DATA_TYPE 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = 'Products' 
AND COLUMN_NAME IN ('IdealCycleTimeSeconds', 'StandarCycleTime');
GO

