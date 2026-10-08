# Prompt untuk Frontend Developer: Implementasi Nett Operating Time

## Tujuan
Tambahkan tampilan **Nett Operating Time** di Time Metrics section pada halaman OEE Detail. Nett Operating Time adalah waktu ideal untuk memproduksi total unit yang dihasilkan, dihitung dari **Cycle Time × Total Produced**.

---

## Data dari Backend

### 1. Initial Load (Server-Side)
```csharp
Model.NettOperatingTime // TimeSpan - Nett Operating Time
```

### 2. AJAX `GetTimeMetrics` (setiap 5 detik)
```json
{
  "NettOperatingTimeSeconds": 3600,  // Durasi dalam detik (number)
  "NettOperatingTime": "01:00:00"     // Format string "hh:mm:ss" (display)
}
```

---

## Implementasi Frontend

### 1. Tambahkan HTML Element di Time Metrics Section

**Lokasi:** `Views/Machine/OeeDetail.cshtml` - Setelah "Operating Time" (sekitar baris 245)

Tambahkan kode berikut setelah section Operating Time:

```html
<div class="mb-2">
    <div class="d-flex justify-content-between mb-1">
        <span class="text-secondary">Nett Operating Time</span>
        <span class="fw-semibold text-info" id="nett-operating-time">@Model.NettOperatingTime.ToString(@"hh\:mm\:ss")</span>
    </div>
    <div class="progress" style="height: 6px; background-color: #e9ecef;">
        @{
            var nettOperatingPercent = Model.OperatingTime.TotalSeconds > 0 
                ? (Model.NettOperatingTime.TotalSeconds / Model.OperatingTime.TotalSeconds * 100) 
                : 0;
            
            // Logika warna: Biru untuk nett operating time
            string nettOperatingColor = "#17a2b8"; // Info color (Bootstrap info color)
        }
        <div class="progress-bar" id="nett-operating-progress-bar" style="width: @Math.Min(100, nettOperatingPercent).ToString("F1")%; background-color: @nettOperatingColor !important; min-width: 2px;"></div>
    </div>
    <div class="small text-muted mt-1">
        <i class="fa-solid fa-info-circle me-1"></i>
        Ideal time = Cycle Time × Total Produced
    </div>
</div>
```

### 2. Update JavaScript untuk Real-time Update

#### A. Update fungsi `updateTimeDisplay()` (sekitar baris 1280)

Tambahkan kode berikut setelah update Operating Time:

```javascript
// Update Nett Operating Time
const nettOperatingEl = document.getElementById('nett-operating-time');
const nettOperatingProgressBar = document.getElementById('nett-operating-progress-bar');
if (nettOperatingEl && data?.NettOperatingTimeSeconds !== undefined) {
    const nettOperatingSeconds = data.NettOperatingTimeSeconds || 0;
    nettOperatingEl.textContent = formatTime(nettOperatingSeconds);
    
    // Update progress bar
    if (nettOperatingProgressBar && currentOperatingSeconds > 0) {
        const nettOperatingPercent = (nettOperatingSeconds / currentOperatingSeconds * 100);
        nettOperatingProgressBar.style.width = Math.min(100, nettOperatingPercent) + '%';
    }
}
```

**Catatan:** Pastikan `currentOperatingSeconds` sudah dihitung sebelumnya di fungsi `updateTimeDisplay()`.

#### B. Update fungsi `fetchTimeMetrics()` (sekitar baris 1400)

Tambahkan kode berikut setelah update NoLoadingTime:

```javascript
// Update Nett Operating Time
if (data?.NettOperatingTimeSeconds !== undefined) {
    const nettOperatingEl = document.getElementById('nett-operating-time');
    const nettOperatingProgressBar = document.getElementById('nett-operating-progress-bar');
    if (nettOperatingEl) {
        nettOperatingEl.textContent = data.NettOperatingTime || '00:00:00';
        
        // Update progress bar
        if (nettOperatingProgressBar && data.OperatingTimeSeconds > 0) {
            const nettOperatingPercent = (data.NettOperatingTimeSeconds / data.OperatingTimeSeconds * 100);
            nettOperatingProgressBar.style.width = Math.min(100, nettOperatingPercent) + '%';
        }
    }
}
```

### 3. Styling (Opsional)

Jika ingin menambahkan styling khusus untuk Nett Operating Time, tambahkan di CSS:

```css
/* Nett Operating Time - Info color (Biru) */
#nett-operating-time {
    color: #17a2b8; /* Bootstrap info color */
}

#nett-operating-progress-bar {
    background-color: #17a2b8 !important;
    transition: width 0.3s ease;
}
```

---

## Penjelasan Nett Operating Time

**Nett Operating Time** adalah waktu ideal yang seharusnya diperlukan untuk memproduksi total unit yang dihasilkan, dihitung dengan rumus:

```
Nett Operating Time = Standar Cycle Time × Total Produced (Good + Reject)
```

**Contoh:**
- Standar Cycle Time: 10 detik/unit
- Total Produced: 360 unit
- Nett Operating Time: 10 × 360 = 3600 detik = 1 jam

**Kegunaan:**
- Digunakan untuk menghitung **Performance** dalam OEE
- Performance = (Nett Operating Time / Operating Time) × 100%
- Menunjukkan efisiensi produksi: semakin kecil selisih antara Operating Time dan Nett Operating Time, semakin baik

---

## Checklist Implementasi

- [ ] Tambahkan HTML element untuk Nett Operating Time di Time Metrics section
- [ ] Tambahkan progress bar dengan warna info (biru)
- [ ] Tambahkan tooltip/helper text: "Ideal time = Cycle Time × Total Produced"
- [ ] Update fungsi `updateTimeDisplay()` untuk real-time update
- [ ] Update fungsi `fetchTimeMetrics()` untuk AJAX update
- [ ] Test tampilan initial load (dari server-side)
- [ ] Test real-time update via AJAX (setiap 5 detik)
- [ ] Test progress bar update sesuai dengan Operating Time
- [ ] Pastikan format waktu konsisten (HH:mm:ss)

---

## File yang Perlu Diubah

- `Views/Machine/OeeDetail.cshtml` - Tambahkan HTML element dan update JavaScript

---

## Testing

1. **Test Initial Load:**
   - Buka OEE Detail page
   - Pastikan Nett Operating Time ter-display dengan benar
   - Pastikan progress bar ter-render dengan benar

2. **Test Real-time Update:**
   - Tunggu 5 detik (AJAX update)
   - Pastikan Nett Operating Time update otomatis
   - Pastikan progress bar update sesuai dengan Operating Time

3. **Test Progress Bar:**
   - Pastikan progress bar menunjukkan persentase: (Nett Operating Time / Operating Time) × 100%
   - Pastikan warna progress bar adalah biru (#17a2b8)

---

## Catatan Penting

1. **Nett Operating Time** hanya akan memiliki nilai jika:
   - `standarCycleTime > 0` (ada cycle time dari product)
   - `totalCount > 0` (ada produksi)

2. **Progress Bar** menunjukkan persentase Nett Operating Time terhadap Operating Time:
   - Jika Nett Operating Time = Operating Time → Performance = 100%
   - Jika Nett Operating Time < Operating Time → Performance < 100% (ada loss)
   - Jika Nett Operating Time > Operating Time → Tidak mungkin (error calculation)

3. **Warna Progress Bar:** Gunakan warna info (biru) untuk membedakan dari Operating Time (hijau) dan Downtime (merah).

---

## Contoh Tampilan

```
Time Metrics
├── Planned Production: 12:00:00
├── Operating Time: 10:00:00 (hijau)
├── Nett Operating Time: 08:00:00 (biru) ← BARU
│   └── Progress: 80% (ideal time / operating time)
├── Downtime: 02:00:00 (merah)
└── NO LOADING: 00:00:00 (abu-abu)
```

---

Prompt ini siap digunakan oleh frontend developer untuk implementasi Nett Operating Time di halaman OEE Detail.

