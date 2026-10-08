# 🔧 Cara Memastikan Perubahan Kode Terlihat di Browser

## ⚠️ PENTING! Baca Ini Dulu:

Perubahan kode **TIDAK AKAN LANGSUNG TERLIHAT** karena:
1. **Browser menyimpan cache** (file lama)
2. **Aplikasi perlu di-compile ulang** (untuk file .cs)
3. **Server perlu di-restart** (untuk perubahan Controller)

---

## 📋 Checklist Wajib Setelah Edit Kode:

### ✅ **Step 1: Build Ulang Aplikasi**

Jalankan command ini di terminal:

```powershell
dotnet build
```

Atau kalau pakai Visual Studio: tekan `Ctrl + Shift + B`

**Tunggu sampai selesai!** Lihat output:
- ✅ **Build succeeded** = Bagus, lanjut step 2
- ❌ **Build FAILED** = Ada error, harus diperbaiki dulu!

---

### ✅ **Step 2: Stop Aplikasi yang Sedang Jalan**

**Jika menjalankan di Visual Studio:**
- Tekan tombol **Stop** (kotak merah) atau `Shift + F5`

**Jika menjalankan di Terminal:**
- Tekan `Ctrl + C` untuk stop server

**Jika tidak tahu aplikasi jalan atau tidak:**
```powershell
# Cari process yang jalan
Get-Process | Where-Object {$_.ProcessName -like "*dotnet*"}

# Stop semua dotnet process (hati-hati!)
Get-Process | Where-Object {$_.ProcessName -like "*dotnet*"} | Stop-Process -Force
```

---

### ✅ **Step 3: Jalankan Ulang Aplikasi**

```powershell
dotnet run
```

Atau di Visual Studio: tekan `F5` atau klik tombol **Play (hijau)**

**Tunggu sampai muncul:**
```
Now listening on: http://localhost:5xxx
Application started. Press Ctrl+C to shut down.
```

---

### ✅ **Step 4: Clear Browser Cache & Hard Refresh**

#### **Cara 1: Hard Refresh (Paling Cepat)**
- **Windows**: `Ctrl + Shift + R` atau `Ctrl + F5`
- **Mac**: `Cmd + Shift + R`

#### **Cara 2: Clear Cache Total (Lebih Ampuh)**
1. Tekan `F12` (buka Developer Tools)
2. Klik kanan pada tombol **Refresh** (di address bar)
3. Pilih **"Empty Cache and Hard Reload"**

#### **Cara 3: Clear Cache dari Browser**
**Chrome/Edge:**
- `Ctrl + Shift + Delete`
- Pilih **"Cached images and files"**
- Time range: **Last hour**
- Klik **Clear data**

---

## 🎯 Jenis Perubahan & Cara Handlenya:

| Jenis File | Perlu Build? | Perlu Restart? | Perlu Clear Cache? |
|------------|--------------|----------------|-------------------|
| **.cshtml** (View) | ⚠️ Kadang | ❌ Tidak* | ✅ **YA** |
| **.cs** (Controller/Model) | ✅ **YA** | ✅ **YA** | ✅ **YA** |
| **.css** (Style) | ❌ Tidak | ❌ Tidak | ✅ **YA** |
| **.js** (JavaScript) | ❌ Tidak | ❌ Tidak | ✅ **YA** |
| **appsettings.json** | ❌ Tidak | ✅ **YA** | ❌ Tidak |

*Catatan: Untuk .cshtml, jika pakai **Hot Reload** tidak perlu restart, tapi tetap perlu clear cache browser!

---

## 🔥 Solusi Cepat: One-Line Command

Jalankan ini setiap kali edit kode:

```powershell
# Stop, Build, Run
dotnet build && dotnet run
```

Lalu di browser: `Ctrl + Shift + R`

---

## 🚨 Troubleshooting: Masih Tidak Berubah?

### 1. **Cek Port yang Digunakan**
```powershell
# Lihat aplikasi yang jalan di port tertentu
netstat -ano | findstr :5000
netstat -ano | findstr :5001
```

### 2. **Pastikan Tidak Ada Multiple Instance**
```powershell
# Lihat semua dotnet process
Get-Process dotnet

# Kill semua (HATI-HATI!)
Get-Process dotnet | Stop-Process -Force
```

### 3. **Delete Folder bin & obj, Lalu Build Ulang**
```powershell
# Hapus compiled files
Remove-Item -Recurse -Force bin, obj

# Build dari awal
dotnet build
dotnet run
```

### 4. **Gunakan Incognito/Private Mode**
Buka browser dalam mode **Incognito/Private** untuk testing (tidak ada cache sama sekali)

### 5. **Cek di Browser Lain**
Coba buka di browser berbeda (Chrome, Edge, Firefox) untuk memastikan bukan masalah browser

---

## 💡 Tips Pro:

### **Aktifkan Hot Reload (ASP.NET Core 6+)**
Edit file `Properties/launchSettings.json`:

```json
{
  "profiles": {
    "DeliveryControl": {
      "hotReloadEnabled": true,
      "hotReloadProfile": "aspnetcore"
    }
  }
}
```

Dengan Hot Reload, perubahan di **.cshtml** akan langsung terlihat tanpa restart!

### **Disable Cache Saat Development**
Di Developer Tools (F12):
1. Buka tab **Network**
2. Centang **"Disable cache"**
3. Biarkan Dev Tools tetap terbuka saat browsing

### **Tambahkan Versioning di CSS/JS**
Di `_Layout.cshtml`, ubah:
```html
<!-- Dari -->
<link rel="stylesheet" href="~/css/site.css" />

<!-- Jadi -->
<link rel="stylesheet" href="~/css/site.css?v=@DateTime.Now.Ticks" />
```

Ini akan memaksa browser download file baru setiap kali!

---

## 📌 Kesimpulan:

**UNTUK SETIAP PERUBAHAN KODE:**

1. ✅ Save file
2. ✅ `dotnet build` (jika edit .cs)
3. ✅ Restart aplikasi (`Ctrl+C` lalu `dotnet run`)
4. ✅ Hard refresh browser (`Ctrl + Shift + R`)
5. ✅ Cek perubahan

**Jangan lupa:** Browser cache adalah musuh utama developer! 😅

---

## 🆘 Masih Bermasalah?

Coba urutan ini (Nuclear Option):

```powershell
# 1. Stop semua
Get-Process dotnet | Stop-Process -Force

# 2. Hapus cache
Remove-Item -Recurse -Force bin, obj

# 3. Build fresh
dotnet clean
dotnet build

# 4. Run
dotnet run
```

Lalu di browser: **Incognito Mode** + **Hard Refresh**

---

**Good luck! 🚀**

