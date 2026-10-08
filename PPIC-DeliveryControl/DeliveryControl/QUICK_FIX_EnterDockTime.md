# Quick Fix - Error Kolom EnterDockTime Tidak Ada

## Masalah
Saat deployment, muncul error bahwa kolom `EnterDockTime` tidak ada di tabel `DeliverySchedules`.

## Solusi Cepat

### Opsi 1: Menggunakan Script Fix (Paling Mudah)

Jalankan salah satu script berikut di SQL Server Management Studio:

#### Script 1: Fix EnterDockTime saja
```sql
-- Buka file: Fix_EnterDockTime_Column.sql
-- Jalankan di SQL Server Management Studio
```

#### Script 2: Fix semua kolom yang mungkin hilang (Direkomendasikan)
```sql
-- Buka file: Fix_All_Missing_Columns.sql
-- Jalankan di SQL Server Management Studio
```

**Cara menggunakan:**
1. Buka SQL Server Management Studio
2. Connect ke database production
3. Pastikan database `PPIC_DeliveryControl` dipilih
4. Buka file `Fix_EnterDockTime_Column.sql` atau `Fix_All_Missing_Columns.sql`
5. Klik **Execute** (F5)

---

### Opsi 2: Query Manual

Jalankan query berikut di SQL Server Management Studio:

```sql
USE [PPIC_DeliveryControl];
GO

-- Cek apakah tabel ada
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DeliverySchedules' AND type = 'U')
BEGIN
    -- Cek apakah kolom sudah ada
    IF NOT EXISTS (
        SELECT * 
        FROM sys.columns 
        WHERE object_id = OBJECT_ID('DeliverySchedules') 
        AND name = 'EnterDockTime'
    )
    BEGIN
        -- Tambahkan kolom
        ALTER TABLE [DeliverySchedules] 
        ADD [EnterDockTime] datetime2 NULL;
        
        PRINT 'Kolom EnterDockTime berhasil ditambahkan.';
    END
    ELSE
    BEGIN
        PRINT 'Kolom EnterDockTime sudah ada.';
    END
END
ELSE
BEGIN
    PRINT 'ERROR: Tabel DeliverySchedules tidak ditemukan.';
END
GO
```

---

### Opsi 3: Menggunakan Command Line

```powershell
sqlcmd -S NAMA_SERVER -d PPIC_DeliveryControl -i "Fix_EnterDockTime_Column.sql" -E
```

Atau dengan SQL Authentication:
```powershell
sqlcmd -S NAMA_SERVER -d PPIC_DeliveryControl -i "Fix_EnterDockTime_Column.sql" -U USERNAME -P PASSWORD
```

---

## Verifikasi

Setelah menjalankan script, verifikasi dengan query berikut:

```sql
USE [PPIC_DeliveryControl];
GO

-- Cek kolom EnterDockTime
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'DeliverySchedules'
AND COLUMN_NAME = 'EnterDockTime';
```

Jika kolom ada, query akan mengembalikan 1 baris dengan informasi kolom.

---

## Catatan

- Script fix sudah **idempotent** (aman dijalankan berulang kali)
- Script akan otomatis cek apakah kolom sudah ada sebelum menambahkan
- Script juga akan update migration history jika diperlukan

---

## Jika Masih Error

Jika masih ada error setelah menjalankan script fix:

1. **Cek apakah tabel DeliverySchedules ada:**
   ```sql
   SELECT * FROM sys.tables WHERE name = 'DeliverySchedules';
   ```

2. **Cek semua kolom di tabel DeliverySchedules:**
   ```sql
   SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
   FROM INFORMATION_SCHEMA.COLUMNS
   WHERE TABLE_NAME = 'DeliverySchedules'
   ORDER BY ORDINAL_POSITION;
   ```

3. **Cek migration history:**
   ```sql
   SELECT * FROM __EFMigrationsHistory 
   ORDER BY MigrationId;
   ```

4. Hubungi tim development dengan informasi error lengkap.

