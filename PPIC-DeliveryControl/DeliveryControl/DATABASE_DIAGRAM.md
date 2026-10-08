# 🗂️ DATABASE RELATIONSHIP DIAGRAM

## 📊 Entity Relationship Diagram (ERD)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         DELIVERY CONTROL SYSTEM                          │
│                         Database Relationships                           │
└─────────────────────────────────────────────────────────────────────────┘

                    ╔════════════════════════╗
                    ║      CUSTOMER         ║ (Parent)
                    ║═══════════════════════║
                    ║ PK: CustomerId        ║
                    ║ • CustomerCode        ║
                    ║ • CustomerName        ║
                    ║ • Route               ║
                    ║ • SKID                ║
                    ╚════════════════════════╝
                           │        │
                           │        │ (One-to-Many)
              ┌────────────┘        └────────────┐
              │ Restrict                    Restrict │
              │                                      │
              ▼                                      ▼
    ╔══════════════════╗              ╔═══════════════════════════╗
    ║      DOCK       ║              ║   DELIVERY SCHEDULE      ║
    ║═════════════════║              ║══════════════════════════║
    ║ PK: DockId      ║              ║ PK: ScheduleId           ║
    ║ FK: CustomerId  ║◄─────┐       ║ FK: CustomerId           ║
    ║ • DockCode      ║      │       ║ FK: DockId               ║
    ║ • DockName      ║      │       ║ • ScheduleNumber         ║
    ║ • Location      ║      │       ║ • ScheduledDate          ║
    ╚══════════════════╝      │       ║ • Status                 ║
              │               │       ║ • ETD, Pickup, etc.      ║
              │               │       ╚═══════════════════════════╝
              │ Restrict      │                    │
              │               │                    │ Cascade
              │ (One-to-Many) │                    │ (One-to-Many)
              └───────────────┘                    │
                                                   ▼
                                     ╔════════════════════════════╗
                                     ║     DELIVERY ITEM         ║
    ╔══════════════════╗             ║═══════════════════════════║
    ║       ITEM      ║             ║ PK: DeliveryItemId        ║
    ║═════════════════║             ║ FK: ScheduleId            ║
    ║ PK: ItemId      ║◄────────────║ FK: ItemId                ║
    ║ • ItemCode      ║   Restrict  ║ • Quantity                ║
    ║ • ItemName      ║             ║ • ActualQuantity          ║
    ║ • Unit          ║             ║ • TotalWeight             ║
    ║ • Category      ║             ╚════════════════════════════╝
    ╚══════════════════╝
```

---

## 🔗 Relationship Details

### 1️⃣ **Customer → Dock** (One-to-Many)
```
Customer [1] ──────┬───── [∞] Dock
                   │
            DeleteBehavior.Restrict
            (Tidak bisa hapus Customer 
             jika masih punya Dock)
```

### 2️⃣ **Customer → DeliverySchedule** (One-to-Many)
```
Customer [1] ──────┬───── [∞] DeliverySchedule
                   │
            DeleteBehavior.Restrict
            (Tidak bisa hapus Customer 
             jika masih punya Schedule)
```

### 3️⃣ **Dock → DeliverySchedule** (One-to-Many)
```
Dock [1] ──────┬───── [∞] DeliverySchedule
               │
        DeleteBehavior.Restrict
        (Tidak bisa hapus Dock 
         jika masih punya Schedule)
```

### 4️⃣ **DeliverySchedule → DeliveryItem** (One-to-Many)
```
DeliverySchedule [1] ──────┬───── [∞] DeliveryItem
                           │
                    DeleteBehavior.CASCADE
                    (Hapus Schedule = Hapus semua Item)
                    ⚠️ OTOMATIS DELETE!
```

### 5️⃣ **Item → DeliveryItem** (One-to-Many)
```
Item [1] ──────┬───── [∞] DeliveryItem
               │
        DeleteBehavior.Restrict
        (Tidak bisa hapus Item 
         jika masih punya DeliveryItem)
```

---

## 🛡️ Delete Behaviors Summary

| Parent Entity      | Child Entity       | Delete Behavior | Aksi Saat Delete Parent          |
|--------------------|--------------------|-----------------|----------------------------------|
| Customer           | Dock               | **Restrict**    | ❌ Tidak bisa delete Customer    |
| Customer           | DeliverySchedule   | **Restrict**    | ❌ Tidak bisa delete Customer    |
| Dock               | DeliverySchedule   | **Restrict**    | ❌ Tidak bisa delete Dock        |
| DeliverySchedule   | DeliveryItem       | **Cascade**     | ✅ Otomatis delete DeliveryItem  |
| Item               | DeliveryItem       | **Restrict**    | ❌ Tidak bisa delete Item        |

---

## 🔄 Data Flow (Normal Operation)

### **Scenario: Buat Schedule Baru**

```
Step 1: User memilih Customer
   └─> Load Docks berdasarkan Customer yang dipilih
        └─> User memilih Dock
             └─> Buat DeliverySchedule
                  └─> Tambah DeliveryItem (Item-item yang dikirim)
```

**Code Flow:**
```
1. CustomersController → Show list Customer
2. DeliverySchedulesController.Create → Select Customer
3. AJAX GetDocksByCustomer(customerId) → Load Dock dropdown
4. DeliverySchedulesController.Create → Save Schedule
5. DeliveryItemsController.Create → Add items to schedule
```

---

## 🗑️ Delete Flow (Safe Deletion)

### **Scenario 1: Delete Customer**

```
❌ TIDAK BISA jika:
   ├─ Ada Dock yang terkait
   └─ Ada DeliverySchedule yang terkait

✅ BISA jika:
   └─ Tidak ada Dock DAN DeliverySchedule
```

**Controller Logic:**
```csharp
1. Include Docks & DeliverySchedules
2. IF (customer.Docks.Any()) → Error Message
3. IF (customer.DeliverySchedules.Any()) → Error Message
4. ELSE → Delete Customer ✅
```

---

### **Scenario 2: Delete Dock**

```
❌ TIDAK BISA jika:
   └─ Ada DeliverySchedule yang menggunakan Dock ini

✅ BISA jika:
   └─ Tidak ada DeliverySchedule yang terkait
```

**Controller Logic:**
```csharp
1. Include DeliverySchedules
2. IF (dock.DeliverySchedules.Any()) → Error Message
3. ELSE → Delete Dock ✅
```

---

### **Scenario 3: Delete Item**

```
❌ TIDAK BISA jika:
   └─ Ada DeliveryItem yang menggunakan Item ini

✅ BISA jika:
   └─ Tidak ada DeliveryItem yang terkait
```

**Controller Logic:**
```csharp
1. Include DeliveryItems
2. IF (item.DeliveryItems.Any()) → Error Message
3. ELSE → Delete Item ✅
```

---

### **Scenario 4: Delete DeliverySchedule** ⚠️ CASCADE

```
✅ SELALU BISA DELETE

⚠️ PERHATIAN:
   └─ Semua DeliveryItem akan OTOMATIS TERHAPUS (Cascade)
```

**Controller Logic:**
```csharp
1. Include DeliveryItems (untuk cascade)
2. Delete Schedule
3. Database otomatis hapus semua DeliveryItems ✅
```

---

### **Scenario 5: Delete DeliveryItem**

```
✅ SELALU BISA DELETE
   └─ Tidak mempengaruhi tabel lain
```

**Controller Logic:**
```csharp
1. Delete DeliveryItem langsung ✅
```

---

## 📈 Cardinality Reference

```
Customer [1] ──────── [∞] Dock
   "Satu Customer bisa punya banyak Dock"

Customer [1] ──────── [∞] DeliverySchedule
   "Satu Customer bisa punya banyak Schedule"

Dock [1] ──────── [∞] DeliverySchedule
   "Satu Dock bisa digunakan banyak Schedule"

DeliverySchedule [1] ──────── [∞] DeliveryItem
   "Satu Schedule bisa punya banyak Item yang dikirim"

Item [1] ──────── [∞] DeliveryItem
   "Satu Item bisa ada di banyak Delivery"
```

---

## 🎯 Unique Constraints

| Table              | Field(s)        | Type   |
|--------------------|-----------------|--------|
| Customer           | CustomerCode    | Unique |
| Dock               | DockCode        | Unique |
| Item               | ItemCode        | Unique |
| DeliverySchedule   | ScheduleNumber  | Unique |

---

## 📝 Indexes untuk Performance

### **DeliverySchedule**
```sql
✅ Index pada: ScheduledDate
✅ Index pada: Status
✅ Composite Index: (CustomerId, ScheduledDate)
```

### **DeliveryItem**
```sql
✅ Composite Index: (ScheduleId, ItemId)
```

---

## 🚀 Best Practices

### ✅ DO's:
1. ✅ Selalu cek relasi sebelum delete parent entity
2. ✅ Gunakan Include() untuk eager loading saat cek relasi
3. ✅ Berikan pesan error yang jelas ke user
4. ✅ Gunakan try-catch untuk DbUpdateException
5. ✅ Remove navigation properties dari ModelState saat Create/Edit

### ❌ DON'Ts:
1. ❌ Jangan force delete dengan SQL raw query tanpa cek relasi
2. ❌ Jangan ubah DeleteBehavior menjadi Cascade tanpa pertimbangan
3. ❌ Jangan lupa Include() saat cek relasi (akan null check fail)
4. ❌ Jangan hapus data master (Customer, Item) tanpa data cleanup dulu

---

**📅 Dibuat**: 21 November 2025  
**🔧 Status**: ✅ Verified & Production Ready

