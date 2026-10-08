# Quick Start Guide - Delivery Control System

## 🚀 Langkah Cepat Memulai

### 1️⃣ Setup Database (5 menit)

```bash
# Buka terminal di folder project
cd C:\Users\fio.vlt00122\DeliveryControl

# Create database dengan migration
dotnet ef migrations add InitialCreate
dotnet ef database update
```

✅ Database `PPIC_DeliveryControl` akan dibuat dengan sample data!

### 2️⃣ Jalankan Aplikasi (1 menit)

```bash
dotnet run
```

Atau tekan **F5** di Visual Studio.

### 3️⃣ Akses Aplikasi

Buka browser dan akses:
- **HTTPS**: https://localhost:5001
- **HTTP**: http://localhost:5000

---

## 📋 First Time Setup Checklist

- [x] ✅ Project sudah dibuat
- [ ] 🔧 Install .NET 8.0 SDK (jika belum)
- [ ] 🗄️ Install SQL Server LocalDB (jika belum)
- [ ] 🔨 Build project: `dotnet build`
- [ ] 📊 Create database: `dotnet ef database update`
- [ ] ▶️ Run aplikasi: `dotnet run`
- [ ] 🌐 Akses di browser: https://localhost:5001

---

## 🎯 Getting Started Tutorial

### Langkah 1: Lihat Dashboard
1. Akses https://localhost:5001
2. Dashboard akan menampilkan statistik dan jadwal hari ini
3. Sample data sudah tersedia!

### Langkah 2: Explore Master Data
1. Klik menu **Master Data** > **Customer**
2. Lihat 2 customer sample: PT ABC Manufacturing dan PT XYZ Industries
3. Explore menu **Dock** dan **Item**

### Langkah 3: Buat Schedule Delivery Pertama
1. Klik menu **Jadwal Delivery**
2. Klik tombol **Tambah Schedule**
3. Isi form:
   - **No. Schedule**: SCH-2025001
   - **Customer**: PT ABC Manufacturing
   - **Dock**: Dock A1 - Raw Material
   - **Tanggal**: Pilih hari ini
   - **Waktu Mulai**: 08:00
   - **Waktu Selesai**: 10:00
   - **Nomor Kendaraan**: B 1234 XYZ
   - **Driver**: John Doe
4. Klik **Simpan**

### Langkah 4: Tambah Item ke Schedule
1. Klik **Detail** pada schedule yang baru dibuat
2. Klik tombol **Tambah Item**
3. Pilih item dan isi quantity
4. Klik **Simpan**

### Langkah 5: Monitoring Delivery
1. Di halaman detail schedule, klik **Mulai Delivery**
2. Sistem akan record waktu mulai actual
3. Setelah selesai, klik **Selesaikan Delivery**
4. Lihat perbandingan waktu schedule vs actual

---

## 🔧 Konfigurasi Database

### Default Connection String
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PPIC_DeliveryControl;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true"
  }
}
```

### Menggunakan SQL Server Lain

Edit file `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=NAMA_SERVER;Database=PPIC_DeliveryControl;User Id=username;Password=password;TrustServerCertificate=true"
  }
}
```

Kemudian jalankan migration:
```bash
dotnet ef database update
```

---

## 📦 Sample Data yang Tersedia

### Customers
- **CUST001**: PT ABC Manufacturing
- **CUST002**: PT XYZ Industries

### Docks
- **DOCK-A1**: Dock A1 - Raw Material (Customer: PT ABC)
- **DOCK-A2**: Dock A2 - Finished Goods (Customer: PT ABC)
- **DOCK-B1**: Dock B1 - Main Loading (Customer: PT XYZ)

### Items
- **ITM001**: Raw Material A
- **ITM002**: Finished Product B
- **ITM003**: Packaging Material

---

## 🎨 Fitur Utama yang Bisa Dicoba

### 1. Dashboard Monitoring
- Real-time view jadwal hari ini
- Statistics cards
- Quick access ke detail schedule

### 2. Schedule Management
- Create schedule dengan customer & dock selection
- Auto-filter dock berdasarkan customer
- Start/Complete delivery tracking

### 3. Delay Detection
- Automatic delay calculation
- Visual indicator untuk keterlambatan
- Delay duration display

### 4. Item Tracking
- Add multiple items per schedule
- Plan vs actual quantity tracking
- Variance calculation

### 5. Filtering & Search
- Filter by date range
- Filter by customer
- Filter by status
- Search functionality

---

## 🛠️ Development Tools

### Recommended IDE
- **Visual Studio 2022** (Community/Professional)
- **Visual Studio Code** dengan C# extension
- **JetBrains Rider**

### Browser DevTools
- F12 untuk inspect
- Console untuk debug JavaScript
- Network tab untuk monitor AJAX calls

### Database Tools
- **SQL Server Management Studio (SSMS)**
- **Azure Data Studio**
- Visual Studio SQL Server Object Explorer

---

## 📱 Menu Navigation

```
Delivery Control System
├── 🏠 Dashboard
│   └── Overview, Today's schedules, Statistics
├── 📅 Jadwal Delivery
│   ├── List semua schedule
│   ├── Filter & Search
│   ├── Create new schedule
│   ├── Start/Complete delivery
│   └── View details & items
└── 📊 Master Data
    ├── 🏢 Customer
    │   └── CRUD customer
    ├── 📦 Dock
    │   └── CRUD dock per customer
    └── 📋 Item
        └── CRUD item/produk
```

---

## 🚨 Troubleshooting Quick Fix

### Problem: Port sudah digunakan
```bash
# Edit Properties/launchSettings.json
# Ubah port di "applicationUrl"
```

### Problem: Database connection error
```bash
# Drop dan recreate database
dotnet ef database drop --force
dotnet ef database update
```

### Problem: Migration error
```bash
# Remove migration dan buat ulang
dotnet ef migrations remove
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Problem: Build error
```bash
# Clean dan rebuild
dotnet clean
dotnet build
```

---

## 📚 Next Steps

Setelah familiar dengan aplikasi:

1. 📖 Baca **README.md** untuk dokumentasi lengkap
2. 🗄️ Baca **DATABASE_STRUCTURE.md** untuk detail database
3. 🔄 Baca **MIGRATION_GUIDE.md** untuk migration workflow
4. 🎨 Customize UI sesuai kebutuhan
5. ➕ Tambah fitur baru sesuai requirement

---

## 💡 Tips & Tricks

### Keyboard Shortcuts (Browser)
- **F5**: Refresh page
- **Ctrl + Shift + R**: Hard refresh (clear cache)
- **F12**: Open DevTools

### Development Workflow
1. Update Model → Create Migration → Update Database → Test
2. Selalu test di development sebelum production
3. Gunakan sample data untuk testing

### Best Practices
- Backup database sebelum migration
- Gunakan descriptive names untuk schedule
- Monitor delay patterns untuk improvement
- Regular database maintenance

---

## 📞 Need Help?

### Documentation
- **README.md**: Overview & installation
- **DATABASE_STRUCTURE.md**: Database schema
- **MIGRATION_GUIDE.md**: Migration commands
- **QUICK_START.md**: This file

### Resources
- [ASP.NET Core Docs](https://learn.microsoft.com/aspnet/core/)
- [Entity Framework Core Docs](https://learn.microsoft.com/ef/core/)
- [Bootstrap 5 Docs](https://getbootstrap.com/docs/5.0/)

---

## ✨ Selamat Mencoba!

Aplikasi Delivery Control System siap digunakan. Jika ada pertanyaan atau masalah, silakan hubungi tim development.

**Happy Coding! 🚀**

