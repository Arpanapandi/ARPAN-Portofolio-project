# Quick Deploy Guide - Production

## Cara Cepat Deploy Database ke SQL Server Production

### Metode 1: Menggunakan Script PowerShell (Paling Mudah)

```powershell
# Windows Authentication
.\Deploy-ToProduction.ps1 -ServerName "localhost" -DatabaseName "PPIC_DeliveryControl" -UseWindowsAuth -CreateDatabase -BackupFirst

# SQL Authentication
.\Deploy-ToProduction.ps1 -ServerName "localhost" -DatabaseName "PPIC_DeliveryControl" -Username "sa" -Password "YourPassword" -CreateDatabase -BackupFirst
```

### Metode 2: Menggunakan SQL Server Management Studio

1. Buka SQL Server Management Studio
2. Connect ke SQL Server production
3. Buat database (jika belum ada):
   ```sql
   CREATE DATABASE PPIC_DeliveryControl;
   ```
4. Buka file `Migrations\Production_Migration.sql`
5. Pastikan database `PPIC_DeliveryControl` dipilih
6. Klik **Execute** (F5)

### Metode 3: Menggunakan Command Line

```powershell
# Windows Authentication
sqlcmd -S localhost -E -d PPIC_DeliveryControl -i "Migrations\Production_Migration.sql"

# SQL Authentication
sqlcmd -S localhost -U sa -P YourPassword -d PPIC_DeliveryControl -i "Migrations\Production_Migration.sql"
```

---

## Update Connection String

Edit `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=NAMA_SERVER;Database=PPIC_DeliveryControl;User Id=USERNAME;Password=PASSWORD;TrustServerCertificate=true;MultipleActiveResultSets=true"
  }
}
```

**Contoh:**
- Windows Auth: `Server=localhost;Database=PPIC_DeliveryControl;Trusted_Connection=true;TrustServerCertificate=true;MultipleActiveResultSets=true`
- SQL Auth: `Server=localhost;Database=PPIC_DeliveryControl;User Id=sa;Password=Pass123;TrustServerCertificate=true;MultipleActiveResultSets=true`

---

## Set Environment Production

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

---

## Verifikasi

```sql
-- Cek migrasi
SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;

-- Cek tabel
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE';

-- Cek user default
SELECT Username, FullName, Role FROM Users;
```

---

## User Default

- **admin** / **admin123** (Role: Admin)
- **user** / **user123** (Role: User)

**⚠️ PENTING: Ganti password setelah deployment pertama!**

---

Lihat `DEPLOYMENT_PRODUCTION.md` untuk panduan lengkap.

