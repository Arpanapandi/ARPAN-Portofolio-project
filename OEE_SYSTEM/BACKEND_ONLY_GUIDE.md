# Panduan: Perubahan Hanya di Backend

Dokumen ini menjelaskan bagaimana memastikan bahwa setiap perubahan yang dilakukan **hanya di bagian backend** dan tidak menyentuh frontend, untuk menghindari konflik dengan teman yang mengerjakan frontend.

## 📁 Struktur Proyek

### ✅ BACKEND (Boleh Diubah)
File dan direktori berikut adalah bagian **BACKEND** dan boleh diubah:

- `Controllers/` - Semua controller (C#)
- `Models/` - Semua model dan ViewModels (C#)
- `Data/` - Database context dan konfigurasi (C#)
- `Services/` - Business logic services (C#)
- `Hubs/` - SignalR hubs (C#)
- `Program.cs` - Application startup dan konfigurasi
- `appsettings.json` - Konfigurasi aplikasi
- `OeeSystem.csproj` - Project file
- `*.sql` - Script SQL (jika ada)

### ❌ FRONTEND (TIDAK BOLEH Diubah)
File dan direktori berikut adalah bagian **FRONTEND** dan **TIDAK BOLEH** diubah:

- `Views/` - Semua file `.cshtml` (Razor views)
- `wwwroot/` - Static files (CSS, JS, images)

## 🔍 Cara Validasi Sebelum Commit

### Opsi 1: Gunakan Script PowerShell (Recommended)

**Quick Check (Cepat):**
```powershell
powershell -ExecutionPolicy Bypass -File .\check-backend.ps1
```

**Full Validation (Lengkap):**
```powershell
powershell -ExecutionPolicy Bypass -File .\validate-backend-only.ps1
```

**Atau set execution policy sekali (jika sering digunakan):**
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\check-backend.ps1
# atau
.\validate-backend-only.ps1
```

Script ini akan:
- ✅ Mengecek semua file yang berubah (staged dan modified)
- ✅ Memastikan tidak ada perubahan di `Views/` atau `wwwroot/`
- ✅ Menampilkan daftar perubahan backend yang valid
- ❌ Memberikan error jika ada perubahan frontend

### Opsi 2: Manual Check dengan Git

1. **Cek file yang sudah di-stage:**
   ```powershell
   git diff --cached --name-only
   ```

2. **Cek file yang belum di-stage:**
   ```powershell
   git diff --name-only
   ```

3. **Pastikan tidak ada file dari `Views/` atau `wwwroot/`:**
   ```powershell
   git diff --name-only | Select-String -Pattern "Views|wwwroot"
   ```
   
   Jika ada output, berarti ada perubahan frontend yang harus di-restore.

## 🛠️ Workflow yang Disarankan

### 1. Sebelum Mulai Bekerja

```powershell
# Pastikan di branch yang benar (misal: Arpan)
git checkout Arpan

# Pull perubahan terbaru
git pull origin Arpan
```

### 2. Setelah Membuat Perubahan

```powershell
# Jalankan validasi
.\validate-backend-only.ps1
```

### 3. Jika Ada Perubahan Frontend yang Tidak Disengaja

```powershell
# Unstage file frontend
git restore --staged Views
git restore --staged wwwroot

# Restore perubahan frontend (kembalikan ke versi terakhir)
git restore Views
git restore wwwroot
```

### 4. Stage Hanya Backend

```powershell
# Stage hanya file backend
git add Controllers/
git add Models/
git add Data/
git add Services/
git add Hubs/
git add Program.cs
git add appsettings.json
git add OeeSystem.csproj

# Atau jika ada file SQL
git add *.sql
```

### 5. Validasi Lagi Sebelum Commit

```powershell
# Jalankan validasi sekali lagi
.\validate-backend-only.ps1
```

### 6. Commit

```powershell
git commit -m "Backend updates: [deskripsi perubahan]"
```

### 7. Push ke Branch

```powershell
git push origin Arpan
```

## ⚠️ Troubleshooting

### Error: "Ditemukan perubahan di FRONTEND!"

**Solusi:**
1. Unstage file frontend:
   ```powershell
   git restore --staged Views
   git restore --staged wwwroot
   ```

2. Restore perubahan frontend:
   ```powershell
   git restore Views
   git restore wwwroot
   ```

3. Jalankan validasi lagi:
   ```powershell
   .\validate-backend-only.ps1
   ```

### File Backend Tidak Terdeteksi

Jika file backend tidak terdeteksi oleh script, pastikan:
- File berada di direktori yang benar (`Controllers/`, `Models/`, dll)
- File memiliki ekstensi yang benar (`.cs`, `.csproj`, `.json`, `.sql`)

## 📝 Contoh Commit Message

```
Backend updates: Fix LastStatusChangeTime calculation in OperatorController
Backend updates: Add new OEE calculation logic in OeeService
Backend updates: Update machine status synchronization
Backend updates: Fix downtime end time calculation
```

## 🔗 Referensi

- Repository: https://github.com/Arpanapandi/OEE_SYSTEM
- Branch Backend: `Arpan`
- Branch Frontend: `Nesha`

---

**Catatan:** Script `validate-backend-only.ps1` harus dijalankan setiap kali sebelum commit untuk memastikan tidak ada perubahan frontend yang tidak disengaja.

