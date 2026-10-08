# Prompt Frontend: Implementasi Rest Break Time di Time Metrics

## 📋 Deskripsi
Backend sudah mengirim data **Rest Break Time** secara terpisah untuk ditampilkan di Time Metrics. Rest Break Time tetap termasuk dalam Planned Downtime untuk perhitungan Planned Production Time, tetapi perlu ditampilkan terpisah di frontend.

## ✅ Data yang Sudah Tersedia dari Backend

### 1. API Endpoint: `/Machine/GetTimeMetrics`
Response JSON sekarang sudah termasuk:
```json
{
  "RestBreakTimeSeconds": 300,        // Durasi Rest Break dalam detik
  "RestBreakTime": "00:05:00",        // Format waktu HH:mm:ss
  "HasActiveRestBreak": false,        // Status apakah Rest Break sedang aktif
  // ... data lainnya
}
```

### 2. ViewModel: `MachineOeeViewModel`
Property baru yang tersedia:
```csharp
public TimeSpan RestBreakTime { get; set; }  // Rest Break Time
public bool HasActiveRestBreak { get; set; } // Status Rest Break aktif
```

## 🎯 Tugas Frontend

### 1. Update Time Metrics Display
**File:** `Views/Machine/OeeDetail.cshtml`

**Lokasi:** Di bagian Time Metrics card (sekitar baris 270-290)

**Perubahan yang diperlukan:**

#### A. Tampilkan Rest Break Time
Tambahkan display untuk Rest Break Time di Time Metrics:

```html
<!-- Rest Break Time -->
<div class="d-flex justify-content-between align-items-center mb-2">
    <span class="text-secondary">Rest Break</span>
    <span class="fw-semibold text-warning" id="rest-break-time">00:00:00</span>
</div>
```

#### B. Update JavaScript untuk Rest Break Time
**Lokasi:** Di fungsi `updateTimeMetrics()` (sekitar baris 1240-1370)

**Perubahan:**

1. **Ambil data Rest Break dari backend:**
```javascript
// Di fungsi fetchTimeMetrics() atau updateTimeMetrics()
let baseRestBreakSeconds = 0;
let hasActiveRestBreak = false;

// Update dari data backend
if (data.RestBreakTimeSeconds !== undefined) {
    baseRestBreakSeconds = Number(data.RestBreakTimeSeconds) || 0;
}
if (data.HasActiveRestBreak !== undefined) {
    hasActiveRestBreak = data.HasActiveRestBreak;
}
```

2. **Hitung Rest Break Time dengan real-time increment:**
```javascript
// Di dalam fungsi updateTimeMetrics()
// Rest Break Time bertambah jika ada Rest Break aktif DAN bukan NoLoading
let currentRestBreakSeconds = baseRestBreakSeconds;
if (hasActiveRestBreak && !isNoLoading) {
    // Rest Break time bertambah setiap detik jika ada rest break aktif dan bukan NoLoading
    currentRestBreakSeconds = baseRestBreakSeconds + realTimeCounter;
}

// Update Rest Break Time display
const restBreakEl = document.getElementById('rest-break-time');
if (restBreakEl) {
    restBreakEl.textContent = formatTime(currentRestBreakSeconds);
}
```

3. **Update Rest Break Progress Bar (jika ada):**
```javascript
// Hitung persentase Rest Break terhadap Planned Production Time
const restBreakProgressBar = document.getElementById('rest-break-progress-bar');
if (restBreakProgressBar) {
    if (currentPlannedSeconds > 0) {
        const restBreakPercent = (currentRestBreakSeconds / currentPlannedSeconds * 100);
        restBreakProgressBar.style.width = restBreakPercent.toFixed(1) + '%';
    } else {
        restBreakProgressBar.style.width = '0%';
    }
}
```

4. **Update status Rest Break saat action button diklik:**
```javascript
// Di event handler untuk REST BREAK button
document.querySelector('form[action*="Rest"]')?.addEventListener('submit', function() {
    hasActiveRestBreak = true; // Set Rest Break status aktif
    // ... kode lainnya
});

// Di event handler untuk RUNNING button
document.querySelector('form[action*="Start"]')?.addEventListener('submit', function() {
    hasActiveRestBreak = false; // Reset Rest Break status saat RUNNING
    // ... kode lainnya
});

// Di event handler untuk LINE STOP button
document.querySelector('form[action*="LineStop"]')?.addEventListener('submit', function() {
    hasActiveRestBreak = false; // Reset Rest Break status saat LINE STOP
    // ... kode lainnya
});

// Di event handler untuk NO LOADING button
document.querySelector('form[action*="NoLoading"]')?.addEventListener('submit', function() {
    hasActiveRestBreak = false; // Reset Rest Break status saat NO LOADING
    // ... kode lainnya
});
```

5. **Update dari polling data (jika menggunakan polling):**
```javascript
// Di fungsi yang memanggil GetTimeMetrics (polling)
fetch('/Machine/GetTimeMetrics?machineId=' + machineId + '&shiftKey=' + shiftKey)
    .then(response => response.json())
    .then(data => {
        // Update Rest Break Time dari backend
        if (data.RestBreakTimeSeconds !== undefined) {
            baseRestBreakSeconds = Number(data.RestBreakTimeSeconds) || 0;
        }
        if (data.HasActiveRestBreak !== undefined) {
            hasActiveRestBreak = data.HasActiveRestBreak;
        }
        // ... update lainnya
    });
```

## 📐 Formula dan Logika

### Formula Time Metrics (Sudah Benar di Backend):
```
Total Shift Time = 12:00:00
Rest Break = 00:05:00 (termasuk Planned Downtime)
Setup = 00:02:31 (termasuk Planned Downtime)
Unplanned Downtime = 00:00:00

Planned Downtime = Rest Break + Setup = 00:07:31 ✅
Planned Production Time = 12:00:00 - 00:07:31 = 11:52:29 ✅
Operating Time = 11:52:29 - 00:00:00 = 11:52:29 ✅
Downtime Total = 00:07:31 + 00:00:00 = 00:07:31 ✅
Rest Break Time = 00:05:00 (terpisah untuk display) ✅
```

### Logika Real-time Update:
- **Rest Break Time** bertambah setiap detik jika:
  - `hasActiveRestBreak === true` (Rest Break sedang aktif)
  - `isNoLoading === false` (bukan No Loading)
- **Rest Break Time** tidak bertambah jika:
  - Rest Break sudah selesai (`hasActiveRestBreak === false`)
  - Sedang No Loading (`isNoLoading === true`)

## 🎨 Styling (Opsional)

Jika ingin menambahkan progress bar untuk Rest Break:

```html
<!-- Rest Break dengan Progress Bar -->
<div class="d-flex justify-content-between align-items-center mb-2">
    <span class="text-secondary">Rest Break</span>
    <span class="fw-semibold text-warning" id="rest-break-time">00:00:00</span>
</div>
<div class="progress" style="height: 4px; margin-top: 2px;">
    <div class="progress-bar" id="rest-break-progress-bar" 
         style="width: 0%; background-color: #ffc107 !important; min-width: 2px;"></div>
</div>
```

## ✅ Checklist Implementasi

- [ ] Tambahkan HTML element untuk Rest Break Time display
- [ ] Update JavaScript untuk mengambil `RestBreakTimeSeconds` dan `HasActiveRestBreak` dari backend
- [ ] Implementasi real-time increment untuk Rest Break Time
- [ ] Update status `hasActiveRestBreak` saat action button diklik (REST BREAK, RUNNING, LINE STOP, NO LOADING)
- [ ] Update Rest Break Time dari polling data (jika menggunakan polling)
- [ ] Test: Rest Break Time bertambah saat Rest Break aktif
- [ ] Test: Rest Break Time berhenti saat RUNNING/LINE STOP/NO LOADING diklik
- [ ] Test: Rest Break Time sinkron dengan data dari backend

## 📝 Catatan Penting

1. **Rest Break Time tetap termasuk Planned Downtime:**
   - Rest Break mengurangi Planned Production Time (karena termasuk Planned Downtime)
   - Rest Break Time hanya ditampilkan terpisah untuk kemudahan monitoring

2. **Real-time Update:**
   - Rest Break Time harus update setiap detik saat Rest Break aktif
   - Gunakan `realTimeCounter` yang sudah ada untuk increment

3. **Sinkronisasi dengan Backend:**
   - Base value (`baseRestBreakSeconds`) diambil dari backend
   - Real-time increment dihitung di frontend
   - Status `hasActiveRestBreak` diupdate dari backend dan dari action button

## 🔗 Referensi

- File yang perlu diubah: `Views/Machine/OeeDetail.cshtml`
- API Endpoint: `/Machine/GetTimeMetrics`
- ViewModel: `MachineOeeViewModel` (property `RestBreakTime` dan `HasActiveRestBreak`)

---

**Status Backend:** ✅ Sudah selesai dan siap digunakan
**Status Frontend:** ⏳ Menunggu implementasi

