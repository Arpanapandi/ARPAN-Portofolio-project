-- Script untuk mengecek data ImageUrl di tabel Machines
-- Jalankan script ini di SQL Server Management Studio atau via sqlcmd

-- 1. Cek semua machines dengan ImageUrl
SELECT 
    Id,
    Name,
    ImageUrl,
    CASE 
        WHEN ImageUrl IS NULL THEN 'NULL'
        WHEN ImageUrl = '' THEN 'EMPTY'
        ELSE 'HAS VALUE'
    END AS ImageUrlStatus,
    LEN(ImageUrl) AS ImageUrlLength
FROM Machines
ORDER BY Id;

-- 2. Cek machines yang punya ImageUrl (tidak null dan tidak kosong)
SELECT 
    Id,
    Name,
    ImageUrl
FROM Machines
WHERE ImageUrl IS NOT NULL 
    AND ImageUrl != ''
    AND LEN(ImageUrl) > 0
ORDER BY Id;

-- 3. Cek machines yang TIDAK punya ImageUrl
SELECT 
    Id,
    Name,
    ImageUrl
FROM Machines
WHERE ImageUrl IS NULL 
    OR ImageUrl = ''
    OR LEN(ImageUrl) = 0
ORDER BY Id;

-- 4. Hitung jumlah machines dengan dan tanpa ImageUrl
SELECT 
    COUNT(*) AS TotalMachines,
    SUM(CASE WHEN ImageUrl IS NOT NULL AND ImageUrl != '' AND LEN(ImageUrl) > 0 THEN 1 ELSE 0 END) AS MachinesWithImage,
    SUM(CASE WHEN ImageUrl IS NULL OR ImageUrl = '' OR LEN(ImageUrl) = 0 THEN 1 ELSE 0 END) AS MachinesWithoutImage
FROM Machines;

