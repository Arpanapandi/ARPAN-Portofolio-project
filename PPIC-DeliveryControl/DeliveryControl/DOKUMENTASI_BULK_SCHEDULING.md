# Dokumentasi Bulk Scheduling Delivery

## 📋 Ringkasan Fitur

Fitur **Bulk Scheduling** memungkinkan pembuatan jadwal delivery secara massal dengan sistem checklist yang intuitif. Sistem akan otomatis memfilter customer berdasarkan **Cycle** (hari) yang sesuai dengan tanggal yang dipilih.

---

## 🎯 Cara Penggunaan

### 1. Akses Halaman Bulk Create
- Buka menu **Jadwal Delivery**
- Klik tombol **"Buat Schedule (Bulk)"** (tombol hijau)
- Atau akses langsung via URL: `/DeliverySchedules/BulkCreate`

### 2. Pilih Tanggal Jadwal
- Pilih tanggal menggunakan date picker
- Sistem akan menampilkan **nama hari** (Senin, Selasa, dst.)
- Klik **"Filter Customer"** untuk refresh daftar customer

### 3. Filter Otomatis Berdasarkan Cycle
Sistem akan otomatis memfilter customer berdasarkan field **Cycle**:

#### ✅ Customer yang Ditampilkan (Highlight Hijau)
- Customer yang **Cycle**-nya mengandung nama hari yang dipilih
- Contoh: Jika pilih **Senin**, maka customer dengan Cycle:
  - "Senin" ✅
  - "Senin, Rabu, Jumat" ✅
  - "Senin-Kamis" ✅
  
#### ⚪ Customer yang Tetap Ditampilkan (Background Abu-abu)
- Customer dengan Cycle yang tidak mengandung nama hari tertentu
- Customer tanpa Cycle (kosong/null)
- Contoh: "Daily", "Cycle 1", "Harian", dll.

### 4. Checklist Customer
Gunakan tombol-tombol berikut untuk memudahkan seleksi:
- **"Pilih Semua Sesuai Hari"** - Checklist hanya customer yang highlight hijau
- **"Pilih Semua"** - Checklist semua customer tanpa kecuali
- **"Hapus Semua"** - Uncheck semua checkbox

### 5. Buat Schedule
- Klik tombol **"Buat Schedule (X Customer)"**
- Sistem akan membuat schedule untuk semua customer yang dichecklist
- Data Route, Cycle, Pickup, ETD, SKID, Area akan otomatis terisi dari master Customer

---

## 🔧 Implementasi Teknis

### File yang Dibuat/Diubah

#### 1. **ViewModel** - `Models/ViewModels/BulkScheduleViewModel.cs`
```csharp
public class BulkScheduleViewModel
{
    public DateTime ScheduledDate { get; set; }
    public List<int> SelectedCustomerIds { get; set; }
    public List<CustomerScheduleItem> AvailableCustomers { get; set; }
}

public class CustomerScheduleItem
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; }
    public string CustomerName { get; set; }
    // ... properties lainnya
    public bool IsMatchingDay { get; set; } // Flag untuk filter hari
}
```

#### 2. **Controller** - `Controllers/DeliverySchedulesController.cs`

##### Action GET BulkCreate
```csharp
public async Task<IActionResult> BulkCreate(DateTime? selectedDate)
{
    var scheduledDate = selectedDate ?? DateTime.Today;
    var dayName = GetIndonesianDayName(scheduledDate); // "Senin", "Selasa", dll.
    
    // Load customer dan check cycle-nya
    var customerItems = customers.Select(c => new CustomerScheduleItem
    {
        // ... mapping properties
        IsMatchingDay = string.IsNullOrWhiteSpace(c.Cycle) || 
                       c.Cycle.Contains(dayName, StringComparison.OrdinalIgnoreCase) ||
                       !IsValidDayCycle(c.Cycle)
    }).ToList();
    
    return View(viewModel);
}
```

##### Action POST BulkCreate
```csharp
[HttpPost]
public async Task<IActionResult> BulkCreate(BulkScheduleViewModel model)
{
    // Generate schedule number untuk bulk
    // Loop semua selected customer
    // Create schedule dengan data dari customer
    // Save ke database
}
```

##### Helper Methods
- `GetIndonesianDayName(DateTime date)` - Konversi DayOfWeek ke nama hari Indonesia
- `IsValidDayCycle(string cycle)` - Check apakah cycle berisi nama hari
- `ParseTimeToDateTime(string time, DateTime baseDate)` - Parse HH:mm ke DateTime
- `ParseSKID(string skidString)` - Extract angka dari string SKID

#### 3. **View** - `Views/DeliverySchedules/BulkCreate.cshtml`

##### Fitur UI:
- Date picker dengan auto-submit on change
- Tabel customer dengan highlighting:
  - ✅ Hijau = Sesuai hari
  - ⚪ Abu-abu = Beda hari
- Checkbox "Select All" dengan 3 mode:
  - Pilih semua sesuai hari
  - Pilih semua customer
  - Hapus semua
- Counter real-time untuk jumlah customer terpilih
- Badge status untuk setiap customer
- Konfirmasi sebelum submit

##### JavaScript Features:
```javascript
// Update counter selected customers
// Check/uncheck all functionality
// Auto-submit when date changed
// Form validation before submit
```

---

## 📊 Contoh Penggunaan

### Skenario 1: Jadwal Delivery Senin
1. Pilih tanggal: **Senin, 25 Nov 2025**
2. Customer yang ter-highlight hijau:
   - CUST001 - PT ABC (Cycle: "Senin, Rabu")
   - CUST003 - PT XYZ (Cycle: "Senin")
   - CUST005 - PT DEF (Cycle: "Senin-Jumat")
3. Customer abu-abu (tetap bisa dipilih):
   - CUST002 - PT QWE (Cycle: "Selasa, Kamis")
   - CUST004 - PT RTY (Cycle: "Daily")
4. Checklist yang diinginkan → Klik "Buat Schedule"

### Skenario 2: Bulk Create untuk Semua Customer
1. Pilih tanggal apapun
2. Klik **"Pilih Semua"**
3. Klik **"Buat Schedule"**
4. Semua customer akan dijadwalkan tanpa filter hari

---

## 🎨 Kelebihan Sistem Baru

### ✅ Keunggulan:
1. **Efisien** - Tidak perlu create schedule satu per satu
2. **Visual** - Highlighting membuat mudah identifikasi customer yang sesuai
3. **Fleksibel** - Tetap bisa pilih customer lain yang tidak sesuai hari
4. **Otomatis** - Data customer (Route, Pickup, ETD, dll.) terisi otomatis
5. **User-Friendly** - Interface intuitif dengan tombol-tombol pembantu
6. **Smart Filter** - Cycle yang bukan hari (Daily, Cycle 1, dll.) tetap ditampilkan

### 📈 Perbandingan dengan Sistem Lama:

| Aspek | Sistem Lama (Form) | Sistem Baru (Bulk) |
|-------|-------------------|-------------------|
| Kecepatan | 1 schedule per submit | Multiple schedule per submit |
| Filter Hari | Manual | Otomatis dengan highlighting |
| Input Data | Manual per field | Auto-fill dari customer |
| User Experience | Form tradisional | Tabel checklist interaktif |
| Bulk Action | Tidak ada | Ada (select all, filter, dll.) |

---

## 🔍 Tips & Best Practices

### 1. Setting Cycle Customer
Untuk hasil optimal, set **Cycle** customer dengan format:
- ✅ **Baik**: "Senin", "Selasa", "Senin, Rabu, Jumat"
- ✅ **Baik**: "Senin-Jumat", "Senin-Kamis"
- ⚠️ **OK**: "Daily", "Harian", "Cycle 1" (akan selalu muncul)

### 2. Workflow Harian
Gunakan fitur ini untuk jadwal rutin:
1. Setiap pagi, buka Bulk Create
2. Pilih tanggal hari ini
3. Klik "Pilih Semua Sesuai Hari"
4. Review customer yang terpilih
5. Klik "Buat Schedule"
6. ✅ Jadwal harian selesai dalam hitungan detik!

### 3. Jadwal Mingguan
Untuk planning mingguan:
1. Buat schedule untuk Senin → pilih tanggal Senin
2. Buat schedule untuk Selasa → pilih tanggal Selasa
3. Dan seterusnya...
4. Setiap hari akan otomatis filter customer yang sesuai

---

## 🛠️ Troubleshooting

### ❓ Customer tidak muncul di daftar
**Penyebab**: Customer tidak aktif (IsActive = false)
**Solusi**: Aktifkan customer di menu Master Customer

### ❓ Customer tidak ter-highlight hijau padahal cycle-nya sesuai
**Penyebab**: Format cycle tidak sesuai (typo atau bahasa Inggris)
**Solusi**: Edit customer, pastikan cycle menggunakan nama hari dalam bahasa Indonesia

### ❓ Tombol "Buat Schedule" disabled
**Penyebab**: Tidak ada customer yang dichecklist
**Solusi**: Checklist minimal 1 customer

### ❓ Schedule number duplicate
**Penyebab**: Multiple submit bersamaan
**Solusi**: Sistem sudah handle dengan sequence number otomatis

---

## 📝 Catatan Penting

1. **Schedule Number** di-generate otomatis dengan format: `SCH-YYYYMMDD-XXX`
   - Contoh: `SCH-20251122-001`, `SCH-20251122-002`

2. **Data Auto-Fill** dari customer:
   - Route, Cycle, Pickup, ETD, Range, SKID, Area
   - Pickup & ETD dikonversi ke DateTime dengan tanggal schedule

3. **Status Default**: Semua schedule yang dibuat akan berstatus "Scheduled"

4. **Audit Trail**: CreatedDate dan CreatedBy otomatis tercatat

---

## 🚀 Pengembangan Selanjutnya (Future Ideas)

1. ✨ Export hasil filter ke Excel
2. ✨ Template schedule mingguan
3. ✨ Notifikasi schedule yang akan datang
4. ✨ Copy schedule dari minggu sebelumnya
5. ✨ Filter tambahan: Route, Area, SKID

---

**Dibuat**: 22 November 2025
**Versi**: 1.0
**Developer**: AI Assistant

---

