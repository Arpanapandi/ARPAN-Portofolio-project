# 📊 DOKUMENTASI RELASI DATABASE - Delivery Control System

## 🔗 Struktur Relasi Database

### 1. **Customer** (Parent/Utama)
- **Primary Key**: `CustomerId`
- **Relasi Keluar**:
  - ✅ **One-to-Many** → `Dock` (Satu Customer bisa punya banyak Dock)
  - ✅ **One-to-Many** → `DeliverySchedule` (Satu Customer bisa punya banyak Schedule)

### 2. **Dock** (Child dari Customer)
- **Primary Key**: `DockId`
- **Foreign Key**: `CustomerId` → `Customer`
- **Relasi Keluar**:
  - ✅ **One-to-Many** → `DeliverySchedule` (Satu Dock bisa punya banyak Schedule)
- **Delete Behavior**: `Restrict` (Tidak bisa hapus Customer jika masih ada Dock)

### 3. **Item** (Master Independent)
- **Primary Key**: `ItemId`
- **Relasi Keluar**:
  - ✅ **One-to-Many** → `DeliveryItem` (Satu Item bisa ada di banyak Delivery)

### 4. **DeliverySchedule** (Child dari Customer & Dock)
- **Primary Key**: `ScheduleId`
- **Foreign Keys**: 
  - `CustomerId` → `Customer`
  - `DockId` → `Dock`
- **Relasi Keluar**:
  - ✅ **One-to-Many** → `DeliveryItem` (Satu Schedule bisa punya banyak Item)
- **Delete Behavior**: 
  - Customer → `Restrict` (Tidak bisa hapus Customer jika masih ada Schedule)
  - Dock → `Restrict` (Tidak bisa hapus Dock jika masih ada Schedule)

### 5. **DeliveryItem** (Child dari DeliverySchedule & Item)
- **Primary Key**: `DeliveryItemId`
- **Foreign Keys**:
  - `ScheduleId` → `DeliverySchedule`
  - `ItemId` → `Item`
- **Delete Behavior**:
  - Schedule → `Cascade` (Hapus Schedule = Hapus semua DeliveryItem-nya)
  - Item → `Restrict` (Tidak bisa hapus Item jika masih ada DeliveryItem)

---

## 🛡️ PROTEKSI DELETE OPERATIONS

### ❌ **Tidak Bisa Delete Jika:**

#### 1. **Customer**
- ✗ Masih memiliki **Dock** yang terkait
- ✗ Masih memiliki **DeliverySchedule** yang terkait
- **Pesan Error**: "Tidak dapat menghapus customer! Masih ada X dock/schedule yang terkait."

#### 2. **Dock**
- ✗ Masih memiliki **DeliverySchedule** yang terkait
- **Pesan Error**: "Tidak dapat menghapus dock! Masih ada X schedule yang terkait."

#### 3. **Item**
- ✗ Masih memiliki **DeliveryItem** yang terkait
- **Pesan Error**: "Tidak dapat menghapus item! Masih ada X delivery item yang terkait."

#### 4. **DeliverySchedule**
- ✅ **BISA DELETE** - Akan otomatis menghapus semua **DeliveryItem** yang terkait (Cascade Delete)

#### 5. **DeliveryItem**
- ✅ **BISA DELETE** - Tidak mempengaruhi tabel lain

---

## ✅ VALIDASI PADA OPERASI CREATE & EDIT

### **Create Operations**
Semua operasi CREATE sudah dilindungi dengan:
- ✅ Model validation (`[Required]`, `[StringLength]`, dll)
- ✅ Removal navigation properties dari ModelState
- ✅ Foreign key validation otomatis dari database

### **Edit Operations**
Semua operasi EDIT sudah dilindungi dengan:
- ✅ Concurrency check (`DbUpdateConcurrencyException`)
- ✅ Removal navigation properties dari ModelState
- ✅ Update tracking (`UpdatedDate`, `UpdatedBy`)

---

## 🔄 ALUR OPERASI DELETE YANG AMAN

### **Scenario 1: Hapus Customer**
```
1. Cek apakah Customer punya Dock
2. Cek apakah Customer punya DeliverySchedule
3. Jika ADA → TOLAK DELETE dengan pesan error
4. Jika TIDAK ADA → HAPUS Customer
```

### **Scenario 2: Hapus Dock**
```
1. Cek apakah Dock punya DeliverySchedule
2. Jika ADA → TOLAK DELETE dengan pesan error
3. Jika TIDAK ADA → HAPUS Dock
```

### **Scenario 3: Hapus Item**
```
1. Cek apakah Item punya DeliveryItem
2. Jika ADA → TOLAK DELETE dengan pesan error
3. Jika TIDAK ADA → HAPUS Item
```

### **Scenario 4: Hapus DeliverySchedule**
```
1. Include DeliveryItems (untuk cascade delete)
2. HAPUS Schedule
3. Database otomatis hapus semua DeliveryItems (Cascade)
```

### **Scenario 5: Hapus DeliveryItem**
```
1. HAPUS DeliveryItem
2. Tidak ada efek ke tabel lain
```

---

## 📋 URUTAN DELETE YANG BENAR

Jika ingin menghapus **Customer** secara manual:

```
1. Hapus semua DeliverySchedule yang terkait dengan Customer
   └─> Otomatis menghapus semua DeliveryItem (Cascade)
   
2. Hapus semua Dock yang terkait dengan Customer

3. Hapus Customer
```

Jika ingin menghapus **Dock**:

```
1. Hapus atau ubah semua DeliverySchedule yang menggunakan Dock tersebut
   └─> Otomatis menghapus semua DeliveryItem (Cascade)
   
2. Hapus Dock
```

Jika ingin menghapus **Item**:

```
1. Hapus semua DeliveryItem yang menggunakan Item tersebut
   (atau hapus DeliverySchedule-nya langsung)
   
2. Hapus Item
```

---

## 🎯 IMPLEMENTASI DI CONTROLLER

### **CustomersController.cs**
```csharp
✅ Delete: Cek relasi Dock & DeliverySchedule sebelum delete
✅ Edit: Remove navigation properties dari ModelState
```

### **DocksController.cs**
```csharp
✅ Delete: Cek relasi DeliverySchedule sebelum delete
✅ Edit: Remove navigation properties dari ModelState
```

### **ItemsController.cs**
```csharp
✅ Delete: Cek relasi DeliveryItem sebelum delete
✅ Edit: Remove navigation properties dari ModelState
```

### **DeliverySchedulesController.cs**
```csharp
✅ Delete: Include DeliveryItems untuk cascade delete
✅ Create/Edit: Remove navigation properties dari ModelState
```

### **DeliveryItemsController.cs**
```csharp
✅ Delete: Hapus langsung (tidak ada child)
✅ Create/Edit: Remove navigation properties dari ModelState
```

---

## 🔧 KONFIGURASI DI ApplicationDbContext.cs

```csharp
// Customer ↔ Dock: Restrict
entity.HasOne(d => d.Customer)
    .WithMany(c => c.Docks)
    .HasForeignKey(d => d.CustomerId)
    .OnDelete(DeleteBehavior.Restrict);

// Customer ↔ DeliverySchedule: Restrict
entity.HasOne(ds => ds.Customer)
    .WithMany(c => c.DeliverySchedules)
    .HasForeignKey(ds => ds.CustomerId)
    .OnDelete(DeleteBehavior.Restrict);

// Dock ↔ DeliverySchedule: Restrict
entity.HasOne(ds => ds.Dock)
    .WithMany(d => d.DeliverySchedules)
    .HasForeignKey(ds => ds.DockId)
    .OnDelete(DeleteBehavior.Restrict);

// DeliverySchedule ↔ DeliveryItem: CASCADE
entity.HasOne(di => di.DeliverySchedule)
    .WithMany(ds => ds.DeliveryItems)
    .HasForeignKey(di => di.ScheduleId)
    .OnDelete(DeleteBehavior.Cascade);

// Item ↔ DeliveryItem: Restrict
entity.HasOne(di => di.Item)
    .WithMany(i => i.DeliveryItems)
    .HasForeignKey(di => di.ItemId)
    .OnDelete(DeleteBehavior.Restrict);
```

---

## ✨ KESIMPULAN

### ✅ **Sudah Aman dari Error:**
1. ✅ Semua delete operation sudah dicek relasinya
2. ✅ Cascade delete sudah diatur dengan benar (Schedule → DeliveryItem)
3. ✅ Restrict delete sudah diterapkan untuk mencegah orphan data
4. ✅ Navigation properties sudah di-remove dari ModelState saat Create/Edit
5. ✅ Error handling sudah lengkap dengan try-catch dan pesan yang jelas

### 🎉 **Tidak Ada Error Ketika:**
- ✅ Create data baru
- ✅ Edit data existing
- ✅ Delete data (dengan validasi relasi)
- ✅ Import Excel
- ✅ Operasi CRUD normal

---

**📅 Terakhir Diupdate**: 21 November 2025  
**👨‍💻 Status**: ✅ PRODUCTION READY - Siap Digunakan

