# Panduan Deployment ke Production - Delivery Control System

## Persiapan Sebelum Deployment

### 1. Prasyarat
- SQL Server sudah terinstall dan running di PC production
- SQL Server Management Studio (SSMS) atau akses ke SQL Server via command line
- File aplikasi sudah di-build untuk production
- Connection string untuk SQL Server production sudah diketahui

### 2. Backup Database (Jika Database Sudah Ada)
Sebelum melakukan migrasi, **WAJIB** backup database terlebih dahulu:

```sql
BACKUP DATABASE PPIC_DeliveryControl 
TO DISK = 'C:\Backup\PPIC_DeliveryControl_BeforeMigration_' + CONVERT(VARCHAR, GETDATE(), 112) + '.bak'
WITH FORMAT, COMPRESSION;
```

---

## Langkah-langkah Deployment

### Opsi 1: Migrasi Menggunakan Script SQL (Direkomendasikan)

#### Langkah 1: Siapkan Connection String
1. Buka file `appsettings.Production.json`
2. Edit connection string sesuai dengan SQL Server production Anda:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=NAMA_SERVER;Database=PPIC_DeliveryControl;User Id=USERNAME;Password=PASSWORD;TrustServerCertificate=true;MultipleActiveResultSets=true"
  }
}
```

**Contoh Connection String:**
- **Windows Authentication**: `Server=localhost;Database=PPIC_DeliveryControl;Trusted_Connection=true;TrustServerCertificate=true;MultipleActiveResultSets=true`
- **SQL Authentication**: `Server=localhost;Database=PPIC_DeliveryControl;User Id=sa;Password=YourPassword123;TrustServerCertificate=true;MultipleActiveResultSets=true`
- **Named Instance**: `Server=localhost\SQLEXPRESS;Database=PPIC_DeliveryControl;Trusted_Connection=true;TrustServerCertificate=true;MultipleActiveResultSets=true`

#### Langkah 2: Buat Database (Jika Belum Ada)
Jalankan perintah berikut di SQL Server Management Studio:

```sql
-- Buat database jika belum ada
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'PPIC_DeliveryControl')
BEGIN
    CREATE DATABASE PPIC_DeliveryControl;
END
GO

USE PPIC_DeliveryControl;
GO
```

#### Langkah 3: Jalankan Script Migrasi
1. Buka file `Migrations/Production_Migration.sql`
2. Buka SQL Server Management Studio
3. Connect ke SQL Server production
4. Buka file `Production_Migration.sql` di SSMS
5. Pastikan database `PPIC_DeliveryControl` dipilih
6. Klik **Execute** atau tekan **F5**

**Atau menggunakan command line:**
```powershell
sqlcmd -S NAMA_SERVER -d PPIC_DeliveryControl -i "Migrations\Production_Migration.sql" -U USERNAME -P PASSWORD
```

#### Langkah 4: Verifikasi Migrasi
Jalankan query berikut untuk memastikan semua migrasi sudah ter-apply:

```sql
USE PPIC_DeliveryControl;
SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;
```

Anda seharusnya melihat semua migration yang ada di folder `Migrations/`.

---

### Opsi 2: Migrasi Menggunakan Entity Framework Core (Alternatif)

Jika Anda memiliki akses langsung ke PC production dan .NET SDK terinstall:

#### Langkah 1: Update Connection String
Edit `appsettings.Production.json` dengan connection string production.

#### Langkah 2: Jalankan Migrasi
```powershell
# Set environment ke Production
$env:ASPNETCORE_ENVIRONMENT = "Production"

# Jalankan migrasi
dotnet ef database update --context ApplicationDbContext
```

**Catatan:** Metode ini memerlukan .NET SDK dan Entity Framework Core tools terinstall di PC production.

---

### Opsi 3: Menggunakan Script PowerShell Otomatis

Gunakan script `Deploy-ToProduction.ps1` yang sudah disediakan:

```powershell
.\Deploy-ToProduction.ps1 -ServerName "NAMA_SERVER" -DatabaseName "PPIC_DeliveryControl" -UseWindowsAuth
```

Lihat detail di file `Deploy-ToProduction.ps1` untuk opsi lengkap.

---

## Konfigurasi Aplikasi Production

### 1. Update appsettings.Production.json
Pastikan connection string sudah benar sesuai dengan SQL Server production.

### 2. Set Environment Variable
Set environment variable `ASPNETCORE_ENVIRONMENT=Production` agar aplikasi menggunakan `appsettings.Production.json`.

**Windows:**
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

**Atau di launchSettings.json:**
```json
{
  "profiles": {
    "Production": {
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Production"
      }
    }
  }
}
```

### 3. Build Aplikasi untuk Production
```powershell
dotnet build -c Release
dotnet publish -c Release -o "C:\Deploy\DeliveryControl"
```

---

## Verifikasi Setelah Deployment

### 1. Cek Database Tables
Pastikan semua tabel sudah dibuat:

```sql
USE PPIC_DeliveryControl;
SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;
```

Tabel yang harus ada:
- `__EFMigrationsHistory`
- `ActivityLogs`
- `Customers`
- `DeliveryItems`
- `DeliverySchedules`
- `Items`
- `SystemSettings`
- `Users`

### 2. Cek Seed Data
Pastikan user default sudah terbuat:

```sql
USE PPIC_DeliveryControl;
SELECT Username, FullName, Role, IsActive FROM Users;
```

User default:
- **Username**: `admin`, **Password**: `admin123`, **Role**: `Admin`
- **Username**: `user`, **Password**: `user123`, **Role**: `User`

**PENTING:** Ganti password default setelah deployment pertama!

### 3. Test Koneksi Aplikasi
1. Jalankan aplikasi
2. Coba login dengan user default
3. Pastikan semua fitur berfungsi dengan baik

---

## Troubleshooting

### Error: "Cannot open database"
**Solusi:**
- Pastikan database sudah dibuat
- Pastikan connection string benar
- Pastikan user memiliki permission untuk mengakses database

### Error: "Login failed for user"
**Solusi:**
- Cek username dan password di connection string
- Pastikan SQL Server authentication mode sudah diaktifkan (jika menggunakan SQL Auth)
- Cek firewall dan network connectivity

### Error: "Migration already applied"
**Solusi:**
- Ini normal jika migrasi sudah pernah dijalankan
- Script menggunakan flag `--idempotent` sehingga aman dijalankan berulang kali

### Error: "Table already exists"
**Solusi:**
- Script migrasi sudah menggunakan `IF NOT EXISTS`, jadi seharusnya tidak terjadi
- Jika terjadi, mungkin ada konflik. Cek tabel `__EFMigrationsHistory` untuk melihat status migrasi

### Error: "Timeout expired"
**Solusi:**
- Tambahkan `Connection Timeout=60` di connection string
- Cek performa server dan network

---

## Rollback (Jika Diperlukan)

Jika terjadi masalah setelah migrasi:

### 1. Restore Database dari Backup
```sql
RESTORE DATABASE PPIC_DeliveryControl 
FROM DISK = 'C:\Backup\PPIC_DeliveryControl_BeforeMigration_YYYYMMDD.bak'
WITH REPLACE;
```

### 2. Atau Rollback ke Migration Tertentu
```powershell
dotnet ef database update NamaMigrationSebelumnya --context ApplicationDbContext
```

---

## Checklist Deployment

- [ ] Backup database (jika database sudah ada)
- [ ] SQL Server sudah running dan accessible
- [ ] Connection string sudah dikonfigurasi dengan benar
- [ ] Database `PPIC_DeliveryControl` sudah dibuat
- [ ] Script migrasi sudah dijalankan
- [ ] Semua tabel sudah terverifikasi
- [ ] User default sudah terbuat
- [ ] Aplikasi bisa connect ke database
- [ ] Login test berhasil
- [ ] Semua fitur utama sudah ditest

---

## Keamanan Production

### 1. Ganti Password Default
Setelah deployment pertama, **WAJIB** ganti password user default:

```sql
USE PPIC_DeliveryControl;
-- Update password admin (gunakan BCrypt hash)
UPDATE Users 
SET Password = '$2a$11$...' -- Generate hash baru dengan BCrypt
WHERE Username = 'admin';
```

### 2. Buat User Database dengan Permission Terbatas
Jangan gunakan `sa` account untuk aplikasi. Buat user khusus:

```sql
-- Buat login
CREATE LOGIN DeliveryControlApp WITH PASSWORD = 'StrongPassword123!';

-- Buat user di database
USE PPIC_DeliveryControl;
CREATE USER DeliveryControlApp FOR LOGIN DeliveryControlApp;

-- Berikan permission
ALTER ROLE db_datareader ADD MEMBER DeliveryControlApp;
ALTER ROLE db_datawriter ADD MEMBER DeliveryControlApp;
ALTER ROLE db_ddladmin ADD MEMBER DeliveryControlApp;
```

### 3. Enable SSL/TLS
Untuk production, gunakan connection string dengan enkripsi:

```
Server=...;Database=...;Encrypt=true;TrustServerCertificate=false;
```

---

## Support

Jika mengalami masalah saat deployment, hubungi tim development dengan menyertakan:
- Error message lengkap
- Log aplikasi
- Status migrasi dari `__EFMigrationsHistory`
- Screenshot error (jika ada)

---

**Selamat Deployment! 🚀**

