# Panduan: Menyimpan Image Machine di Database

Dokumen ini menjelaskan implementasi penyimpanan gambar mesin di database agar Frontend dan Backend bisa menggunakan data yang sama.

## 🎯 Konsep

Gambar mesin sekarang disimpan sebagai **byte array** di database (kolom `ImageData`), bukan di filesystem. Ini memungkinkan:
- ✅ Frontend dan Backend menggunakan database yang sama
- ✅ Tidak perlu sync file antara Frontend dan Backend
- ✅ Data terpusat di satu tempat
- ✅ Backward compatible dengan file yang sudah ada

## 📋 Struktur Database

### Kolom Baru di Tabel Machines

1. **ImageData** (VARBINARY(MAX))
   - Menyimpan gambar sebagai byte array
   - Maksimal 2GB (disarankan max 5MB per file)

2. **ImageContentType** (NVARCHAR(100))
   - Menyimpan content type (image/jpeg, image/png, dll)
   - Untuk menentukan MIME type saat serve image

3. **ImageUrl** (tetap ada)
   - Digunakan sebagai identifier untuk API endpoint
   - Format: `/api/machines/{machineId}/image`
   - Backward compatible dengan file yang sudah ada

## 🚀 Setup

### 1. Jalankan Migration SQL

Jalankan script `AddMachineImageColumns.sql` di SQL Server Management Studio:

```sql
-- Script akan menambahkan kolom ImageData dan ImageContentType
-- Jika kolom sudah ada, akan di-skip
```

Atau jalankan via sqlcmd:

```powershell
sqlcmd -S 10.14.149.34 -d OeeSystemDb -U usrvelasto -P "H1s@na2025!!" -i AddMachineImageColumns.sql
```

### 2. Verifikasi Kolom

Cek apakah kolom sudah ditambahkan:

```sql
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Machines'
    AND COLUMN_NAME IN ('ImageData', 'ImageContentType', 'ImageUrl')
ORDER BY COLUMN_NAME;
```

## 📝 Cara Menggunakan

### Upload Image (Create/Edit Machine)

1. Buka **Admin Panel → Machines → Create/Edit Machine**
2. Pilih file gambar (max 5MB)
3. Klik **Save**
4. Image akan otomatis tersimpan di database sebagai byte array

### Serve Image

Image akan otomatis di-serve melalui endpoint:
```
GET /api/machines/{machineId}/image
```

**Prioritas:**
1. ✅ Serve dari database (ImageData) - **PRIORITAS UTAMA**
2. ✅ Fallback ke filesystem (jika ImageData null) - untuk backward compatibility

### View Image

Image akan otomatis tampil di:
- **Admin Panel → Machines** (kolom Image)
- **Dashboard → Machine Cards**
- **Semua view yang menggunakan endpoint API**

## 🔄 Alur Kerja

### Upload Image
```
User Upload Image
    ↓
Validasi (max 5MB)
    ↓
Convert ke Byte Array
    ↓
Simpan ke Database (ImageData, ImageContentType)
    ↓
Set ImageUrl = "/api/machines/{machineId}/image"
    ↓
Save ke Database
```

### Serve Image
```
Request: /api/machines/{machineId}/image
    ↓
Cek ImageData di Database
    ↓
Ada? → Serve dari Database ✅
    ↓
Tidak Ada? → Cek Filesystem (backward compatibility)
    ↓
Ada? → Serve dari Filesystem ✅
    ↓
Tidak Ada? → Return 404 ❌
```

## ✅ Keuntungan

1. **Sinkronisasi Otomatis**
   - Frontend dan Backend menggunakan database yang sama
   - Tidak perlu sync file manual
   - Data selalu konsisten

2. **Mudah di-Maintain**
   - Tidak perlu manage file di filesystem
   - Backup database = backup semua data termasuk gambar
   - Tidak ada masalah path atau file missing

3. **Backward Compatible**
   - File lama yang masih di filesystem tetap bisa diakses
   - Endpoint otomatis fallback ke filesystem jika ImageData null

4. **Scalable**
   - Bisa digunakan untuk multiple frontend/backend
   - Semua menggunakan database yang sama

## ⚠️ Catatan Penting

1. **Ukuran File**
   - Maksimal 5MB per file (validasi di controller)
   - SQL Server bisa handle hingga 2GB, tapi tidak disarankan
   - Untuk file besar, pertimbangkan resize sebelum upload

2. **Performance**
   - Image disimpan di database bisa sedikit lebih lambat untuk file besar
   - Tapi lebih mudah di-manage dan sinkronisasi otomatis
   - Untuk production, pertimbangkan CDN atau blob storage

3. **Migration Data Lama**
   - Data lama yang masih menggunakan ImageUrl ke filesystem tetap berfungsi
   - Endpoint akan otomatis fallback ke filesystem
   - Bisa migrate secara bertahap dengan upload ulang image

## 🔍 Troubleshooting

### Image Tidak Tampil

1. **Cek ImageData di Database:**
   ```sql
   SELECT Id, Name, 
          CASE WHEN ImageData IS NULL THEN 'NULL' ELSE 'HAS DATA' END AS ImageStatus,
          ImageContentType, ImageUrl
   FROM Machines
   WHERE Id = 'M001';
   ```

2. **Cek API Endpoint:**
   - Buka: `http://localhost:6001/api/machines/M001/image`
   - Ganti `M001` dengan Machine ID yang benar

3. **Cek Browser Console:**
   - Buka Developer Tools (F12)
   - Cek tab Network untuk melihat error loading image

### Error "Ukuran file maksimal 5MB"

- Resize image sebelum upload
- Atau update validasi di controller (tidak disarankan)

### ImageData NULL tapi ImageUrl Ada

- Ini normal untuk data lama yang masih menggunakan filesystem
- Endpoint akan otomatis fallback ke filesystem
- Upload ulang image untuk migrate ke database

## 📚 Referensi

- **Model**: `Models/Machine.cs`
- **Controller**: `Controllers/AdminController.cs`
- **API Endpoint**: `/api/machines/{machineId}/image`
- **Database**: `OeeSystemDb` → Table `Machines`
- **Migration Script**: `AddMachineImageColumns.sql`

---

**Catatan:** Setelah implementasi ini, setiap upload image baru akan otomatis tersimpan di database dan bisa diakses oleh Frontend dan Backend yang menggunakan database yang sama.

