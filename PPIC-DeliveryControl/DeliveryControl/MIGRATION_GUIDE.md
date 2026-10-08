# Panduan Migration - Delivery Control System

## Entity Framework Core Migrations

### Prasyarat
Pastikan tools EF Core sudah terinstall:
```bash
dotnet tool install --global dotnet-ef
```

Atau update ke versi terbaru:
```bash
dotnet tool update --global dotnet-ef
```

---

## Langkah-langkah Setup Database

### 1. Membuat Migration Pertama Kali

```bash
# Pindah ke direktori project
cd C:\Users\fio.vlt00122\DeliveryControl

# Membuat migration
dotnet ef migrations add InitialCreate

# Apply migration ke database
dotnet ef database update
```

### 2. Verifikasi Database

Setelah migration berhasil, database `PPIC_DeliveryControl` akan dibuat dengan tables:
- Customers
- Docks
- Items
- DeliverySchedules
- DeliveryItems

Dan akan otomatis terisi dengan sample data (seed data).

---

## Commands Penting

### Membuat Migration Baru
```bash
dotnet ef migrations add NamaMigration
```

Contoh:
```bash
dotnet ef migrations add AddNewFieldToCustomer
```

### Apply Migration
```bash
# Apply semua pending migrations
dotnet ef database update

# Apply ke migration tertentu
dotnet ef database update NamaMigration
```

### Rollback Migration
```bash
# Rollback ke migration sebelumnya
dotnet ef database update MigrationSebelumnya

# Rollback semua (hapus database)
dotnet ef database update 0
```

### Remove Last Migration
```bash
# Hanya jika migration belum di-apply ke database
dotnet ef migrations remove
```

### List Migrations
```bash
dotnet ef migrations list
```

### Generate SQL Script
```bash
# Generate script untuk semua migrations
dotnet ef migrations script

# Generate script untuk migration tertentu
dotnet ef migrations script MigrationFrom MigrationTo

# Save ke file
dotnet ef migrations script > migration.sql
```

### Drop Database
```bash
dotnet ef database drop
```

Dengan konfirmasi:
```bash
dotnet ef database drop --force
```

---

## Troubleshooting

### Error: "Build failed"
**Solution**: Pastikan project bisa di-build terlebih dahulu
```bash
dotnet build
```

### Error: "No DbContext was found"
**Solution**: Spesifikasikan DbContext
```bash
dotnet ef migrations add InitialCreate --context ApplicationDbContext
```

### Error: "Connection string not found"
**Solution**: 
1. Pastikan `appsettings.json` memiliki ConnectionStrings
2. Atau spesifikasi connection string saat runtime:
```bash
dotnet ef database update --connection "Server=(localdb)\\mssqllocaldb;Database=PPIC_DeliveryControl;Trusted_Connection=true"
```

### Error: "Login failed for user"
**Solution**: 
- Pastikan SQL Server LocalDB running
- Atau ganti connection string ke SQL Server yang accessible

### Error: "Database already exists"
**Solution**:
```bash
# Drop dan create ulang
dotnet ef database drop --force
dotnet ef database update
```

---

## Scenario-based Migration

### Scenario 1: Menambah Field Baru

**Contoh**: Menambah field `Fax` ke table `Customers`

1. Update Model:
```csharp
// Models/Customer.cs
public string? Fax { get; set; }
```

2. Create Migration:
```bash
dotnet ef migrations add AddFaxToCustomer
```

3. Apply Migration:
```bash
dotnet ef database update
```

### Scenario 2: Menambah Table Baru

**Contoh**: Menambah table `Vehicles`

1. Create Model:
```csharp
// Models/Vehicle.cs
public class Vehicle
{
    public int VehicleId { get; set; }
    public string VehicleNumber { get; set; }
    // ... other properties
}
```

2. Add DbSet ke DbContext:
```csharp
// Data/ApplicationDbContext.cs
public DbSet<Vehicle> Vehicles { get; set; }
```

3. Create Migration:
```bash
dotnet ef migrations add AddVehicleTable
```

4. Apply Migration:
```bash
dotnet ef database update
```

### Scenario 3: Mengubah Relationship

**Contoh**: Mengubah relationship dari Restrict ke Cascade

1. Update DbContext:
```csharp
// Data/ApplicationDbContext.cs
modelBuilder.Entity<Dock>()
    .HasOne(d => d.Customer)
    .WithMany(c => c.Docks)
    .HasForeignKey(d => d.CustomerId)
    .OnDelete(DeleteBehavior.Cascade); // Changed from Restrict
```

2. Create Migration:
```bash
dotnet ef migrations add UpdateDockCustomerRelationship
```

3. Apply Migration:
```bash
dotnet ef database update
```

---

## Production Deployment

### 1. Generate SQL Script untuk Production
```bash
dotnet ef migrations script --idempotent --output migration-prod.sql
```

Flag `--idempotent` memastikan script bisa dijalankan berulang kali tanpa error.

### 2. Review SQL Script
Buka dan review file `migration-prod.sql` sebelum execute di production.

### 3. Backup Database Production
```sql
BACKUP DATABASE PPIC_DeliveryControl 
TO DISK = 'C:\Backup\PPIC_DeliveryControl_BeforeMigration.bak'
```

### 4. Execute Migration Script
Jalankan script di SQL Server Management Studio atau sqlcmd:
```bash
sqlcmd -S ServerName -d PPIC_DeliveryControl -i migration-prod.sql
```

### 5. Verify Migration
```sql
SELECT * FROM __EFMigrationsHistory
```

---

## Best Practices

### 1. Always Create Migration After Model Changes
Jangan lupa create migration setiap kali mengubah model:
```bash
dotnet ef migrations add DescriptiveName
```

### 2. Review Generated Migration
Selalu review file migration yang digenerate di folder `Migrations/` sebelum apply.

### 3. Use Descriptive Names
Gunakan nama yang deskriptif untuk migration:
- ✅ Good: `AddEmailToCustomer`, `UpdateDeliveryStatusField`
- ❌ Bad: `Migration1`, `Update`, `Fix`

### 4. Test Migration in Development First
Selalu test migration di development environment sebelum production.

### 5. Backup Before Migration
Selalu backup database sebelum apply migration di production.

### 6. Use Idempotent Scripts for Production
Untuk production, gunakan `--idempotent` flag.

### 7. Never Modify Applied Migrations
Jangan edit migration yang sudah di-apply. Buat migration baru untuk perubahan.

---

## Migration Workflow

```
┌─────────────────────────┐
│  Update Model/DbContext │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Create Migration       │
│  dotnet ef migrations   │
│  add MigrationName      │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Review Migration Files │
│  in Migrations folder   │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Test in Development    │
│  dotnet ef database     │
│  update                 │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Generate SQL for Prod  │
│  dotnet ef migrations   │
│  script --idempotent    │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Backup Production DB   │
└───────────┬─────────────┘
            │
            V
┌─────────────────────────┐
│  Apply to Production    │
└─────────────────────────┘
```

---

## Quick Reference

| Task | Command |
|------|---------|
| Create migration | `dotnet ef migrations add Name` |
| Apply migrations | `dotnet ef database update` |
| Rollback | `dotnet ef database update PreviousMigration` |
| Remove last migration | `dotnet ef migrations remove` |
| List migrations | `dotnet ef migrations list` |
| Generate SQL | `dotnet ef migrations script` |
| Drop database | `dotnet ef database drop` |
| Get EF Core info | `dotnet ef --version` |

---

## Contact

Jika mengalami masalah dengan migration, hubungi tim development.

