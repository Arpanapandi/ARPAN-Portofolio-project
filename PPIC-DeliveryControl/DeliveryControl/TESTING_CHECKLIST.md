# ✅ TESTING CHECKLIST - Delivery Control System
## Pengujian Relasi Database & CRUD Operations

---

## 🎯 CUSTOMER MODULE

### ✅ CREATE Customer
- [ ] Create customer baru dengan data valid
- [ ] Validasi: CustomerCode harus unik
- [ ] Validasi: CustomerName wajib diisi
- [ ] **Expected**: Customer berhasil dibuat

### ✅ EDIT Customer
- [ ] Edit customer existing
- [ ] Update CustomerName, Route, SKID
- [ ] **Expected**: Data berhasil diupdate, UpdatedDate terisi

### ❌ DELETE Customer (Dengan Relasi)
**Test Case 1: Customer punya Dock**
- [ ] Buat Customer baru
- [ ] Buat Dock untuk Customer tersebut
- [ ] Coba delete Customer
- [ ] **Expected**: Error - "Tidak dapat menghapus customer! Masih ada X dock yang terkait"

**Test Case 2: Customer punya DeliverySchedule**
- [ ] Buat Customer baru
- [ ] Buat Dock untuk Customer tersebut
- [ ] Buat DeliverySchedule untuk Customer tersebut
- [ ] Coba delete Customer
- [ ] **Expected**: Error - "Tidak dapat menghapus customer! Masih ada X schedule yang terkait"

### ✅ DELETE Customer (Tanpa Relasi)
- [ ] Buat Customer baru tanpa Dock dan Schedule
- [ ] Delete Customer
- [ ] **Expected**: Customer berhasil dihapus

### 📊 IMPORT Excel Customer
- [ ] Download template Excel
- [ ] Isi data customer di Excel (2-3 rows)
- [ ] Import Excel
- [ ] **Expected**: Data berhasil diimport
- [ ] Validasi: Tidak ada duplicate CustomerCode

---

## 🚪 DOCK MODULE

### ✅ CREATE Dock
- [ ] Pilih Customer dari dropdown
- [ ] Isi DockCode dan DockName
- [ ] **Expected**: Dock berhasil dibuat

### ✅ EDIT Dock
- [ ] Edit dock existing
- [ ] Update DockName, Location
- [ ] Ganti CustomerId (jika perlu)
- [ ] **Expected**: Data berhasil diupdate

### ❌ DELETE Dock (Dengan Relasi)
**Test Case: Dock punya DeliverySchedule**
- [ ] Buat Dock baru
- [ ] Buat DeliverySchedule yang menggunakan Dock ini
- [ ] Coba delete Dock
- [ ] **Expected**: Error - "Tidak dapat menghapus dock! Masih ada X schedule yang terkait"

### ✅ DELETE Dock (Tanpa Relasi)
- [ ] Buat Dock baru tanpa Schedule
- [ ] Delete Dock
- [ ] **Expected**: Dock berhasil dihapus

### 🔍 FILTER Dock by Customer
- [ ] Filter Dock berdasarkan Customer tertentu
- [ ] **Expected**: Hanya tampil Dock dari Customer yang dipilih

---

## 📦 ITEM MODULE

### ✅ CREATE Item
- [ ] Create item baru dengan data valid
- [ ] Validasi: ItemCode harus unik
- [ ] **Expected**: Item berhasil dibuat

### ✅ EDIT Item
- [ ] Edit item existing
- [ ] Update ItemName, Unit, Category
- [ ] **Expected**: Data berhasil diupdate

### ❌ DELETE Item (Dengan Relasi)
**Test Case: Item punya DeliveryItem**
- [ ] Buat Item baru
- [ ] Buat DeliverySchedule
- [ ] Tambah DeliveryItem dengan Item ini
- [ ] Coba delete Item
- [ ] **Expected**: Error - "Tidak dapat menghapus item! Masih ada X delivery item yang terkait"

### ✅ DELETE Item (Tanpa Relasi)
- [ ] Buat Item baru tanpa DeliveryItem
- [ ] Delete Item
- [ ] **Expected**: Item berhasil dihapus

### 🔍 FILTER Item
- [ ] Filter Item by Category
- [ ] Search Item by Code/Name
- [ ] **Expected**: Filtering berjalan normal

---

## 📅 DELIVERY SCHEDULE MODULE

### ✅ CREATE DeliverySchedule
**Test Case 1: Create Manual**
- [ ] Pilih Customer dari dropdown
- [ ] Dropdown Dock otomatis terisi berdasarkan Customer (AJAX)
- [ ] Isi tanggal schedule, ETD, dll
- [ ] **Expected**: Schedule berhasil dibuat

**Test Case 2: Import Excel**
- [ ] Download template Excel
- [ ] Isi data schedule (2-3 rows)
- [ ] Import Excel
- [ ] **Expected**: Schedule berhasil diimport
- [ ] Validasi: CustomerCode dan DockCode harus valid

### ✅ EDIT DeliverySchedule
- [ ] Edit schedule existing
- [ ] Update Status, Date, Driver info
- [ ] **Expected**: Data berhasil diupdate

### ✅ DELETE DeliverySchedule (CASCADE)
**Test Case: Schedule punya DeliveryItem**
- [ ] Buat DeliverySchedule baru
- [ ] Tambah 2-3 DeliveryItem ke Schedule
- [ ] Delete DeliverySchedule
- [ ] **Expected**: 
  - ✅ Schedule berhasil dihapus
  - ✅ Semua DeliveryItem OTOMATIS terhapus (Cascade)

### 🔄 UPDATE Status
- [ ] Start Delivery → Status "In Progress", ActualStartTime terisi
- [ ] Complete Delivery → Status "Completed", ActualEndTime terisi
- [ ] **Expected**: Status dan timestamp terupdate

### 🔍 FILTER Schedule
- [ ] Filter by Date Range
- [ ] Filter by Customer
- [ ] Filter by Status
- [ ] **Expected**: Filtering berjalan normal

### 📊 DASHBOARD
- [ ] Lihat Today's Schedule count
- [ ] Lihat In Progress count
- [ ] Lihat Completed count
- [ ] Lihat Delayed count
- [ ] **Expected**: Statistik tampil sesuai data

---

## 📦 DELIVERY ITEM MODULE

### ✅ CREATE DeliveryItem
- [ ] Dari halaman DeliverySchedule Details
- [ ] Klik "Add Item"
- [ ] Pilih Item dari dropdown
- [ ] Isi Quantity
- [ ] **Expected**: DeliveryItem berhasil ditambahkan

### ✅ EDIT DeliveryItem
- [ ] Edit delivery item existing
- [ ] Update Quantity, ActualQuantity
- [ ] Mark as Completed
- [ ] **Expected**: Data berhasil diupdate

### ✅ DELETE DeliveryItem
- [ ] Delete delivery item
- [ ] **Expected**: Item terhapus tanpa error
- [ ] Cek Schedule masih ada (tidak ikut terhapus)

---

## 🔗 RELASI TESTING (Integration)

### Test Case 1: Full Flow Create → Delete
```
1. ✅ Create Customer
2. ✅ Create Dock untuk Customer
3. ✅ Create Item
4. ✅ Create DeliverySchedule (pilih Customer & Dock)
5. ✅ Add DeliveryItem (pilih Item)
6. ❌ Coba delete Customer → HARUS ERROR
7. ❌ Coba delete Dock → HARUS ERROR
8. ❌ Coba delete Item → HARUS ERROR
9. ✅ Delete DeliverySchedule → OK (cascade delete DeliveryItem)
10. ✅ Delete Dock → OK (sudah tidak ada schedule)
11. ✅ Delete Customer → OK (sudah tidak ada dock & schedule)
12. ✅ Delete Item → OK (sudah tidak ada delivery item)
```

### Test Case 2: Cascade Delete Verification
```
1. ✅ Create Schedule dengan 5 DeliveryItem
2. ✅ Count total DeliveryItem di database
3. ✅ Delete Schedule
4. ✅ Verify semua 5 DeliveryItem otomatis terhapus
5. ✅ Verify Item master masih ada (tidak ikut terhapus)
```

### Test Case 3: AJAX Dynamic Dropdown
```
1. ✅ Buat 2 Customer: A dan B
2. ✅ Customer A punya Dock A1, A2
3. ✅ Customer B punya Dock B1, B2
4. ✅ Create Schedule → Pilih Customer A
5. ✅ Verify dropdown Dock hanya tampil A1, A2
6. ✅ Ganti ke Customer B
7. ✅ Verify dropdown Dock berubah ke B1, B2
```

---

## 🛡️ ERROR HANDLING TESTING

### Test Case 1: Unique Constraint
- [ ] Create Customer dengan CustomerCode yang sudah ada
- [ ] **Expected**: Error unique constraint
- [ ] Create Dock dengan DockCode yang sudah ada
- [ ] **Expected**: Error unique constraint

### Test Case 2: Foreign Key Validation
- [ ] Edit Dock → Pilih CustomerId yang tidak valid (manual/postman)
- [ ] **Expected**: Foreign key constraint error
- [ ] Create Schedule dengan DockId yang tidak valid
- [ ] **Expected**: Foreign key constraint error

### Test Case 3: Required Field Validation
- [ ] Create Customer tanpa CustomerCode
- [ ] **Expected**: Validation error "wajib diisi"
- [ ] Create Schedule tanpa ScheduleNumber
- [ ] **Expected**: Validation error

---

## 📊 PERFORMANCE TESTING

### Query Performance
- [ ] Load Index page dengan 100+ records
- [ ] Filter dengan date range 1 tahun
- [ ] Include navigation properties (eager loading)
- [ ] **Expected**: Load time < 2 detik

### Import Performance
- [ ] Import Excel dengan 50 rows
- [ ] Import Excel dengan 100 rows
- [ ] **Expected**: Import berhasil tanpa timeout

---

## 🔍 CONCURRENCY TESTING

### Test Case: Concurrent Edit
```
1. User A buka Edit Customer ID=1
2. User B buka Edit Customer ID=1
3. User A submit perubahan → OK
4. User B submit perubahan
5. **Expected**: DbUpdateConcurrencyException handled gracefully
```

---

## 📋 CHECKLIST SUMMARY

| Module              | Create | Edit | Delete (No Rel) | Delete (With Rel) | Import |
|---------------------|--------|------|-----------------|-------------------|--------|
| Customer            | ✅     | ✅   | ✅              | ✅ (Protected)    | ✅     |
| Dock                | ✅     | ✅   | ✅              | ✅ (Protected)    | -      |
| Item                | ✅     | ✅   | ✅              | ✅ (Protected)    | -      |
| DeliverySchedule    | ✅     | ✅   | ✅ (Cascade)    | N/A               | ✅     |
| DeliveryItem        | ✅     | ✅   | ✅              | N/A               | -      |

---

## 🎯 ACCEPTANCE CRITERIA

### ✅ PASS jika:
1. ✅ Semua CRUD operation berjalan tanpa error
2. ✅ Delete dengan relasi memberikan error message yang jelas
3. ✅ Cascade delete berjalan otomatis (Schedule → DeliveryItem)
4. ✅ Navigation properties tidak menyebabkan ModelState invalid
5. ✅ AJAX dynamic dropdown berjalan dengan baik
6. ✅ Import Excel berjalan normal
7. ✅ Validasi form berjalan sesuai rule
8. ✅ Error handling menampilkan pesan yang user-friendly

### ❌ FAIL jika:
1. ❌ Ada DbUpdateException yang tidak tertangani
2. ❌ Foreign key constraint error tidak ter-handle
3. ❌ Bisa delete parent entity padahal ada child
4. ❌ Cascade delete tidak berjalan
5. ❌ ModelState invalid karena navigation properties
6. ❌ AJAX dropdown tidak load data
7. ❌ Import Excel error tanpa pesan yang jelas

---

## 🚀 READY FOR PRODUCTION?

Ceklis semua item di atas. Jika semua ✅, maka sistem siap production!

**Testing Date**: _______________  
**Tested By**: _______________  
**Result**: ☐ PASS  ☐ FAIL  
**Notes**: _______________

---

**📅 Dibuat**: 21 November 2025  
**🔧 Version**: 1.0  
**📝 Status**: Ready for Testing

