# Dokumentasi Dashboard - Filter Berdasarkan Tanggal Pickup

## 📅 Perubahan Tanggal: 28 November 2025

### 🎯 Tujuan Perubahan
Dashboard sekarang menampilkan schedule berdasarkan **TANGGAL PICKUP**, bukan tanggal operasional. Ini penting karena:

1. **Customer yang Enter Dock H-1**: Ada customer yang harus masuk dock di hari sebelumnya (contoh: masuk dock jam 19:30 tanggal 27), tapi pickup-nya baru di hari berikutnya (jam 05:30 tanggal 28)
2. **AutoScheduler jam 18:00**: Setiap jam 18:00, sistem otomatis membuat schedule untuk besok, karena ada customer yang harus enter dock hari ini untuk pickup besok

### 📊 Logika Tampilan Dashboard

#### **Hari ini tanggal 28 November**
Dashboard menampilkan:
1. **Grup 1 - PICKUP HARI INI (28 Nov)**
   - Semua schedule dengan `PickupTime.Date = 28 November`
   - Termasuk schedule yang dibuat tanggal 27 (enter dock H-1)
   - Contoh: IAMI, AHM STR, AHM KRW (enter dock 19:30 tgl 27, pickup 05:30-06:00 tgl 28)

2. **Grup 2 - PICKUP BESOK (29 Nov)** - dengan separator visual
   - Semua schedule dengan `PickupTime.Date = 29 November`
   - Schedule ini dibuat oleh AutoScheduler jam 18:00 tanggal 28
   - Customer yang harus enter dock hari ini (28) untuk pickup besok (29)

### 🔧 Perubahan Teknis

#### **1. Controller (HomeController.cs)**

**Before:**
```csharp
// Filter berdasarkan tanggal operasional
var dashboardSchedules = allSchedulesRaw
    .Where(s => GetOperationalDateLocal(s).Date == today)
    .ToList();
```

**After:**
```csharp
// Filter berdasarkan TANGGAL PICKUP
var todayPickupSchedules = allSchedulesRaw
    .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == today)
    .ToList();

var tomorrowPickupSchedules = allSchedulesRaw
    .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == tomorrow)
    .ToList();

// Gabungkan dengan prioritas: pickup hari ini dulu, lalu pickup besok
var dashboardSchedules = new List<DeliverySchedule>();
dashboardSchedules.AddRange(SortSchedulesByPriority(todayPickupSchedules, now));
dashboardSchedules.AddRange(SortSchedulesByPriority(tomorrowPickupSchedules, now));
```

#### **2. Partial View (_ScheduleTablePartial.cshtml)**

**Perbaikan Logika Delay Pickup:**
```csharp
// BEFORE - Logika kompleks dan salah
if (item.EnterDockTime.HasValue && pickup.TimeOfDay <= item.EnterDockTime.Value.TimeOfDay)
{
    logicalPickup = pickup.AddDays(1); // ❌ Salah!
}

// AFTER - Sederhana dan benar
if (now > pickup)
{
    isDelayPickup = true;
    delayPickupMinutes = (int)(now - pickup).TotalMinutes;
}
```

Logika lama salah karena mencoba merekonstruksi tanggal, padahal `PickupTime` dari database sudah berisi **tanggal yang benar**.

**Perbaikan Logika Delay Prepare:**
```csharp
// AFTER - Langsung bandingkan dengan EnterDockTime
if (nowDock > enterDock)
{
    isDelayPrepare = true;
    delayPrepareMinutes = (int)(nowDock - enterDock).TotalMinutes;
}
```

#### **3. View (Index.cshtml)**

**Penambahan informasi di header tabel:**
```cshtml
<h5 class="mb-0 fw-bold">
    <i class="bi bi-calendar-day me-2"></i>
    Jadwal Pickup Delivery Hari Ini
</h5>
<small class="text-muted">
    Termasuk X schedule pickup besok (dari AutoScheduler jam 18:00)
</small>
```

**Separator visual antar grup:**
```cshtml
<!-- Muncul di antara grup pickup hari ini dan pickup besok -->
<tr class="schedule-separator-row">
    <td colspan="9">
        JADWAL PICKUP BESOK - KAMIS, 29 NOVEMBER 2025
        Schedule baru dari AutoScheduler
    </td>
</tr>
```

### 📈 Statistik Dashboard
Statistik (card di atas) **HANYA menghitung schedule pickup hari ini**, tidak termasuk pickup besok:
- ✅ Total Schedule Hari Ini
- ✅ Sudah Delivery (Completed)
- ✅ Sedang Pickup (In Progress)
- ⚠️ **Delay Pickup** - Sekarang sudah benar mendeteksi delay!
- ⏳ Belum Datang

### 🚨 Contoh Kasus Delay Pickup

**Tanggal: 28 November 2025, Jam 15:00**

| Customer | Enter Dock | Pickup | Status Sebelumnya | Status Sekarang |
|----------|------------|--------|-------------------|-----------------|
| DOCK 43 | 27/11 21:00 | **28/11 03:04** | ❌ Delay Prepare | ✅ **Delay Pickup +719 menit** |
| DOCK 53 | 27/11 21:35 | **28/11 04:43** | ❌ Delay Prepare | ✅ **Delay Pickup +617 menit** |
| IAMI | 27/11 19:30 | **28/11 05:30** | ❌ Delay Prepare | ✅ **Delay Pickup +570 menit** |

Sekarang sudah benar menunjukkan **Delay Pickup** karena waktu pickup sudah terlewati!

### 🔄 Flow AutoScheduler

```
Jam 17:59 (28 Nov)
├─ Dashboard menampilkan: Pickup hari ini (28 Nov)
└─ Belum ada schedule pickup besok

Jam 18:00 (28 Nov) - AutoScheduler Triggered
├─ Sistem membuat schedule untuk tanggal 29 Nov
├─ Customer yang perlu enter dock 28 Nov untuk pickup 29 Nov
└─ Schedule dibuat dengan CreatedBy = "AutoScheduler"

Jam 18:01 (28 Nov)
├─ Dashboard menampilkan:
│   ├─ GRUP 1: Pickup hari ini (28 Nov)
│   ├─ [SEPARATOR]
│   └─ GRUP 2: Pickup besok (29 Nov) ← Schedule baru
```

### ✅ Keuntungan Perubahan Ini

1. **Lebih Intuitif**: Dashboard menampilkan schedule berdasarkan "kapan pickup/delivery dilakukan"
2. **Delay Detection Akurat**: Sistem sekarang bisa mendeteksi delay pickup dengan benar
3. **Visibilitas Lebih Baik**: User bisa melihat schedule hari ini DAN schedule besok yang sudah dibuat AutoScheduler
4. **Persiapan Lebih Awal**: User bisa melihat schedule besok dan mempersiapkan customer yang harus enter dock hari ini

### 🔍 Debug Log
Lihat di console/log untuk debug:
```
=== DASHBOARD INDEX START ===
Today: 2025-11-28
Auto Schedule Date: 2025-11-29
Schedules with PICKUP date = TODAY (28/11): 7
Schedules with PICKUP date = TOMORROW (29/11): 5
Total schedules to display: 12 (Pickup Today: 7, Pickup Tomorrow: 5)
```

---

**Catatan Penting:**
- Database `PickupTime` sudah menyimpan **tanggal lengkap yang benar**, tidak perlu rekonstruksi
- Delay Pickup sekarang dihitung dengan membandingkan `DateTime.Now` vs `PickupTime` langsung
- AutoScheduler berjalan setiap hari jam 18:00 (configurable via SystemSettings)

