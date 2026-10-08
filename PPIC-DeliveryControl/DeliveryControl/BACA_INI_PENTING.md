# ⚠️ CARA MELIHAT PERUBAHAN KODE DI BROWSER

## 🔴 MASALAH UTAMA:

**Perubahan kode tidak terlihat di browser karena:**

1. ❌ **Aplikasi masih menggunakan file LAMA yang sudah di-compile**
2. ❌ **Browser menyimpan cache (HTML/CSS/JS lama)**
3. ❌ **Aplikasi tidak di-restart setelah edit kode**

---

## ✅ SOLUSI MUDAH (IKUTI INI!):

### **Cara 1: Pakai Batch File (TERMUDAH!)**

Setiap kali habis edit kode:

1. **Double-click file:** `START_APP.bat`
2. **Tunggu** sampai muncul: "Now listening on: http://localhost:5206"
3. **Buka browser** → Tekan `Ctrl + Shift + R`

**SELESAI!** ✅

---

### **Cara 2: Manual di Terminal**

Buka **PowerShell** atau **Command Prompt** di folder project, lalu:

```batch
REM Stop aplikasi (Tekan Ctrl+C jika ada terminal yang jalan)

REM Lalu jalankan:
dotnet build
dotnet run
```

Setelah aplikasi jalan, buka browser dan tekan: **`Ctrl + Shift + R`**

---

## 🎯 YANG WAJIB DIINGAT:

### **Setiap Edit File .cs (Controller/Model):**
```
1. Save file (Ctrl+S)
2. Double-click START_APP.bat
3. Tunggu sampai "Now listening..."
4. Browser: Ctrl + Shift + R
```

### **Setiap Edit File .cshtml (View):**
```
1. Save file (Ctrl+S)
2. Refresh browser (Ctrl + Shift + R)
```
*Note: Kadang perlu restart juga kalau tidak berubah*

### **Setiap Edit File .css atau .js:**
```
1. Save file (Ctrl+S)
2. Browser: Ctrl + Shift + R (WAJIB!)
```

---

## 🔍 CEK APAKAH APLIKASI SEDANG JALAN:

Buka **Command Prompt** atau **PowerShell**, ketik:

```powershell
netstat -ano | findstr ":5206"
```

**Hasil:**
- ✅ **Ada "LISTENING"** = Aplikasi sedang jalan
- ❌ **Kosong/tidak ada** = Aplikasi tidak jalan

---

## 🚨 KALAU MASIH TIDAK BERUBAH:

### **Opsi 1: Nuclear Restart**

1. **Stop semua aplikasi** (Ctrl+C di terminal)
2. **Cari di Task Manager:**
   - Tekan `Ctrl + Shift + Esc`
   - Cari process: **dotnet.exe** atau **DeliveryControl.exe**
   - Klik kanan → **End Task**
3. **Hapus cache:**
   ```batch
   rmdir /s /q bin
   rmdir /s /q obj
   ```
4. **Build ulang:**
   ```batch
   dotnet clean
   dotnet build
   dotnet run
   ```
5. **Browser: Incognito Mode + Ctrl + Shift + R**

### **Opsi 2: Browser Incognito**

Buka browser dalam mode **Incognito/Private** (Ctrl + Shift + N di Chrome/Edge)

Ini memastikan **TIDAK ADA CACHE** sama sekali!

### **Opsi 3: Disable Cache di Browser**

1. Tekan `F12` (Developer Tools)
2. Klik tab **Network**
3. ✅ Centang: **"Disable cache"**
4. Biarkan Developer Tools tetap terbuka

---

## 📋 CHECKLIST SEBELUM KOMPLAIN "TIDAK BERUBAH":

- [ ] ✅ Aplikasi sudah di-**restart** (double-click START_APP.bat)?
- [ ] ✅ Muncul "**Now listening on: http://localhost:5206**"?
- [ ] ✅ Browser sudah **Hard Refresh** (`Ctrl + Shift + R`)?
- [ ] ✅ Coba buka **Incognito Mode**?
- [ ] ✅ Developer Tools → Network → **Disable cache** dicentang?
- [ ] ✅ File sudah di-**save** (`Ctrl + S`)?

Kalau semua sudah ✅, harusnya **PASTI BERUBAH**!

---

## 💡 TIPS TAMBAHAN:

### **Gunakan Visual Studio?**

1. Tekan `Shift + F5` (Stop)
2. Tekan `Ctrl + Shift + B` (Build)
3. Tekan `F5` (Run)
4. Browser: `Ctrl + Shift + R`

### **Cek Log Error:**

Kalau aplikasi tidak mau start, lihat **output di terminal**:
- Build error? Perbaiki kode dulu!
- Port sudah dipakai? Stop aplikasi lama!

### **Port Alternatif:**

Kalau port 5206 bermasalah, edit file `Properties/launchSettings.json`:

```json
"applicationUrl": "http://localhost:GANTI_PORT_INI"
```

---

## 🎯 KESIMPULAN SINGKAT:

**SETIAP KALI EDIT KODE:**

```
1. Save (Ctrl+S)
2. Restart App (START_APP.bat)
3. Hard Refresh Browser (Ctrl+Shift+R)
```

**ITU SAJA!** Jangan lupakan step 2 dan 3! 🚀

---

## 📞 MASIH BERMASALAH?

Kalau sudah ikuti semua step di atas tapi tetap tidak berubah:

1. Screenshot error/log di terminal
2. Screenshot browser (F12 → Console)
3. Tunjukkan file mana yang diubah
4. Kasih tau proses mana yang sudah dilakukan

Pasti ada solusinya! 😊

---

**FILE PENTING:**
- ✅ `START_APP.bat` - Klik ini untuk restart aplikasi
- ✅ `BACA_INI_PENTING.md` - Dokumentasi ini
- ✅ `CARA_MELIHAT_PERUBAHAN.md` - Dokumentasi lengkap

**SEMOGA MEMBANTU! 🎉**

