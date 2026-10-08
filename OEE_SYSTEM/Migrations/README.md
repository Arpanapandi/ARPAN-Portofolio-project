# Migration: Add ManPower Feature

## Deskripsi
Migration ini menambahkan:
1. Tabel `ManPowers` untuk master data Man Power
2. Kolom `ManPowerId` di tabel `JobRuns`
3. Foreign Key constraint antara `JobRuns` dan `ManPowers`
4. Data default Man Power (1-5 Orang)

## Cara Menjalankan

### Opsi 1: Menggunakan PowerShell Script (Otomatis)
```powershell
cd D:\OEE_SYSTEM\OEE_SYSTEM
.\Migrations\RunManPowerMigration.ps1
```

### Opsi 2: Menggunakan SQL Server Management Studio (Manual)
1. Buka SQL Server Management Studio
2. Connect ke database: `10.14.149.34` / `OeeSystemDb`
3. Buka file `AddManPowerFeature.sql`
4. Execute script

### Opsi 3: Menggunakan dotnet ef (Jika aplikasi di-stop dulu)
```bash
# Stop aplikasi terlebih dahulu
dotnet ef migrations add AddManPowerFeature
dotnet ef database update
```

## Verifikasi
Setelah migration berhasil, verifikasi dengan query:
```sql
-- Cek tabel ManPowers
SELECT * FROM ManPowers;

-- Cek kolom ManPowerId di JobRuns
SELECT TOP 5 Id, MachineId, ManPowerId FROM JobRuns;
```

## Rollback (Jika diperlukan)
```sql
-- Hapus Foreign Key
ALTER TABLE JobRuns DROP CONSTRAINT FK_JobRuns_ManPowers_ManPowerId;

-- Hapus kolom
ALTER TABLE JobRuns DROP COLUMN ManPowerId;

-- Hapus tabel
DROP TABLE ManPowers;
```

