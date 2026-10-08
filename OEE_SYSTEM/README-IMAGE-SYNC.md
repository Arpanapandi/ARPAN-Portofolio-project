# Panduan Sinkronisasi Image Machine

Dokumen ini menjelaskan cara sinkronisasi gambar mesin antara Frontend dan Backend.

## 🎯 Solusi yang Diimplementasikan

### 1. API Endpoint untuk Serve Image
- **Endpoint**: `/api/machines/{machineId}/image`
- **Fungsi**: Serve gambar mesin dengan fallback ke ImageUrl langsung
- **Keuntungan**: 
  - Gambar bisa diakses dari Frontend dan Backend
  - Fallback otomatis jika file tidak ada
  - Centralized image serving

### 2. Script PowerShell untuk Manual Sync
- **File**: `sync-images.ps1`
- **Fungsi**: Sync file gambar antara Frontend dan Backend
- **Usage**:
  ```powershell
  # Sync dari Frontend ke Backend
  .\sync-images.ps1 -FrontendPath "C:\path\to\frontend\wwwroot\images\machines"
  
  # Sync bidirectional (dua arah)
  .\sync-images.ps1 -FrontendPath "C:\path\to\frontend\wwwroot\images\machines" -Bidirectional
  ```

## 📋 Cara Menggunakan

### A. Menggunakan API Endpoint (Otomatis)

Gambar akan otomatis di-serve melalui API endpoint. View sudah di-update untuk menggunakan endpoint ini dengan fallback.

**Tidak perlu action tambahan** - gambar akan otomatis tampil jika:
1. File ada di `wwwroot/images/machines/`
2. ImageUrl ada di database

### B. Manual Sync dengan Script

Jika file gambar belum ada di Backend:

1. **Tentukan path Frontend**:
   ```powershell
   $frontendPath = "C:\path\to\frontend-repo\wwwroot\images\machines"
   ```

2. **Jalankan script**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\sync-images.ps1 -FrontendPath $frontendPath
   ```

3. **Untuk sync dua arah**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\sync-images.ps1 -FrontendPath $frontendPath -Bidirectional
   ```

### C. Sync via GitHub Actions (Opsional)

Jika ingin auto-sync saat commit, tambahkan workflow di `.github/workflows/sync-images.yml`:

```yaml
name: Sync Machine Images

on:
  push:
    branches:
      - Nesha
      - Arpan
    paths:
      - 'wwwroot/images/machines/**'

jobs:
  sync-images:
    runs-on: ubuntu-latest
    steps:
    - name: Checkout Backend
      uses: actions/checkout@v4
      with:
        ref: Arpan
    
    - name: Sync from Frontend
      run: |
        git fetch origin Nesha:frontend-branch
        git checkout frontend-branch -- wwwroot/images/machines/ || true
        git add wwwroot/images/machines/
        git commit -m "Auto-sync: Update machine images [skip ci]" || true
        git push origin Arpan || true
```

## 🔍 Troubleshooting

### Gambar Tidak Tampil

1. **Cek file ada di folder**:
   ```powershell
   Get-ChildItem "wwwroot\images\machines"
   ```

2. **Cek ImageUrl di database**:
   - Buka: `http://localhost:6001/Admin/CheckMachineImages`
   - Atau jalankan query SQL dari `check-machine-images.sql`

3. **Cek API endpoint**:
   - Buka: `http://localhost:6001/api/machines/{machineId}/image`
   - Ganti `{machineId}` dengan ID mesin yang benar

4. **Cek browser console**:
   - Buka Developer Tools (F12)
   - Cek tab Network untuk melihat error loading image

### File Tidak Ter-sync

1. **Pastikan path Frontend benar**
2. **Pastikan file ada di Frontend**
3. **Cek permission folder** - pastikan bisa write ke `wwwroot/images/machines/`

## 📝 Catatan Penting

1. **ImageUrl di Database**: Harus sesuai dengan nama file di folder `wwwroot/images/machines/`
2. **File Naming**: File menggunakan GUID untuk menghindari konflik nama
3. **Sync Frequency**: 
   - Manual sync: Saat diperlukan
   - Auto sync: Setiap commit (jika menggunakan GitHub Actions)

## 🔗 Referensi

- Repository: https://github.com/Arpanapandi/OEE_SYSTEM
- Branch Backend: `Arpan`
- Branch Frontend: `Nesha`
- API Endpoint: `/api/machines/{machineId}/image`

