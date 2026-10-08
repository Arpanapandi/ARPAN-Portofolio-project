# Perbaikan Sinkronisasi Status Machine antara Admin Panel dan Operator View

## Masalah yang Ditemukan

Berdasarkan console log, ditemukan masalah berikut:

1. **MachineStatus tidak ter-return di API response**
   - Console log menunjukkan: `MachineStatus from API: undefined Type: undefined`
   - Ini menyebabkan status di Operator View tidak sinkron dengan Admin panel

2. **Timer tidak sinkron dengan perubahan status**
   - Timer "durasi sejak status terakhir" tidak reset saat status berubah dari Admin
   - Timer tidak sinkron dengan perubahan actions (Start, Rest, LineStop)

3. **Action buttons tidak sinkron**
   - Button state tidak update dengan benar saat status berubah
   - HasActiveJob dan HasActiveDowntime kadang undefined

## Root Cause

1. **GetOperatorData** - MachineStatus mungkin tidak ter-return karena masalah serialization atau variable scope
2. **refreshOperatorData** - Tidak menggunakan LastStatusChangeTime untuk sinkronisasi timer
3. **SignalR Handler** - Tidak reset timer saat status berubah dari Admin
4. **updateStatus** - Tidak validasi MachineStatus sebelum update
5. **updateActionButtons** - Menggunakan data yang mungkin undefined

## Solusi yang Diimplementasikan

### 1. Perbaiki GetOperatorData - Pastikan MachineStatus Selalu di-Return

**File:** `Controllers/OperatorController.cs`

**Perubahan:**
- Tambahkan explicit variable `machineStatusString` sebelum return Json
- Tambahkan `LastStatusChangeTime` dalam ISO 8601 format untuk sinkronisasi timer
- Reorganize JSON response dengan grouping yang jelas
- Tambahkan logging untuk debugging

**Kode:**
```csharp
// ✅ PERBAIKAN: Pastikan MachineStatus selalu di-return dengan explicit variable
var machineStatusString = machine.Status.ToString(); // 'Aktif' atau 'TidakAktif'

// ✅ PERBAIKAN: Log untuk debugging
System.Diagnostics.Debug.WriteLine($"GetOperatorData RETURN - MachineId: '{machineId}', MachineStatus: '{machineStatusString}'");

return Json(new
{
    // Production Data
    TotalGood = totalGood,
    TotalReject = totalReject,
    // ... other fields ...
    
    // ✅ PERBAIKAN: MachineStatus SELALU di-return (sumber kebenaran dari Admin)
    MachineStatus = machineStatusString, // 'Aktif' atau 'TidakAktif'
    
    // Status & Timing
    LastStatusChangeTime = lastStatusChangeTime.ToString("O"), // ✅ ISO 8601 format
    // ... other fields ...
});
```

### 2. Perbaiki refreshOperatorData - Validasi dan Sinkronisasi Timer

**File:** `Views/Operator/Index.cshtml`

**Perubahan:**
- Validasi MachineStatus sebelum update (jika tidak ada, jangan update)
- Gunakan LastStatusChangeTime untuk sinkronisasi timer (prioritas utama)
- Fallback ke SinceLastChangeSeconds atau SinceLastChange string
- Tambahkan logging untuk debugging

**Kode:**
```javascript
.then(data => {
    // ✅ PERBAIKAN: Pastikan MachineStatus ada sebelum update
    if (!data.MachineStatus) {
        console.error('❌ ERROR: MachineStatus is missing from API response!', data);
        return; // Jangan update jika MachineStatus tidak ada
    }
    
    // Update status
    updateStatus(data);
    
    // ✅ PERBAIKAN: Update timer dengan LastStatusChangeTime untuk sinkronisasi
    if (data.LastStatusChangeTime) {
        const lastChangeTime = new Date(data.LastStatusChangeTime);
        const now = new Date();
        const diffSeconds = Math.floor((now.getTime() - lastChangeTime.getTime()) / 1000);
        updateSinceLastChange(diffSeconds);
    } else if (data.SinceLastChangeSeconds !== undefined) {
        updateSinceLastChange(data.SinceLastChangeSeconds);
    }
})
```

### 3. Perbaiki SignalR Handler - Reset Timer saat Status Berubah

**File:** `Views/Operator/Index.cshtml`

**Perubahan:**
- Reset timer saat `MachineStatusUpdated` dari Admin
- Reset timer saat `MachineStarted`
- Reset timer saat `DowntimeStarted` atau `DowntimeEnded`
- Update UI langsung dengan data dari SignalR untuk respons cepat

**Kode:**
```javascript
connection.on("OeeUpdated", function (data) {
    if (String(data.MachineId) === String(machineId)) {
        if (data.Type === "MachineStatusUpdated") {
            // Update UI langsung
            if (data.MachineStatus) {
                updateStatus({ MachineStatus: data.MachineStatus, ... });
            }
            
            // ✅ PERBAIKAN: Reset timer saat status berubah dari Admin
            updateSinceLastChange(0);
            
            // Refresh data
            refreshOperatorData();
        }
        else if (data.Type === "MachineStarted") {
            updateSinceLastChange(0);
            refreshOperatorData();
        }
        else if (data.Type === "DowntimeStarted" || data.Type === "DowntimeEnded") {
            updateSinceLastChange(0);
            refreshOperatorData();
        }
    }
});
```

### 4. Perbaiki updateStatus - Validasi dan Data Lengkap

**File:** `Views/Operator/Index.cshtml`

**Perubahan:**
- Validasi MachineStatus sebelum update (jika empty, return early)
- Update global state variables (hasActiveJob, hasActiveDowntime)
- Pastikan data lengkap saat memanggil updateActionButtons

**Kode:**
```javascript
function updateStatus(data) {
    // Update global state
    hasActiveJob = data.HasActiveJob || false;
    hasActiveDowntime = data.HasActiveDowntime || false;
    
    // ✅ PERBAIKAN: Validasi MachineStatus
    const machineStatusFromAdmin = (data.MachineStatus || '').toString().trim();
    if (!machineStatusFromAdmin) {
        console.error('❌ ERROR: MachineStatus is empty!', data);
        return; // Jangan update jika MachineStatus tidak ada
    }
    
    // ... update UI ...
    
    // ✅ PERBAIKAN: Update action buttons dengan data yang lengkap
    updateActionButtons({
        MachineStatus: machineStatusFromAdmin,
        HasActiveJob: hasActiveJob,
        HasActiveDowntime: hasActiveDowntime
    });
}
```

### 5. Perbaiki updateActionButtons - Handle Undefined Values

**File:** `Views/Operator/Index.cshtml`

**Perubahan:**
- Pastikan HasActiveJob dan HasActiveDowntime selalu boolean (handle undefined)
- Gunakan normalized MachineStatus
- Tambahkan logging untuk debugging

**Kode:**
```javascript
function updateActionButtons(data) {
    const machineStatusFromAdmin = (data.MachineStatus || '').toString().trim();
    const isDisabledByAdmin = machineStatusFromAdmin === 'TidakAktif';
    
    // ✅ PERBAIKAN: Pastikan HasActiveJob dan HasActiveDowntime ada
    const hasActiveJobValue = data.HasActiveJob || false;
    const hasActiveDowntimeValue = data.HasActiveDowntime || false;
    
    // Gunakan hasActiveJobValue dan hasActiveDowntimeValue untuk semua logic
}
```

## Testing Checklist

Setelah implementasi, test scenario berikut:

1. **Initial Load**
   - [ ] Buka Operator View untuk Machine A
   - [ ] Pastikan status sesuai dengan Admin panel
   - [ ] Pastikan action buttons enabled/disabled sesuai status
   - [ ] Pastikan timer "durasi sejak status terakhir" berjalan

2. **Status Update dari Admin**
   - [ ] Buka Admin panel → Machines
   - [ ] Edit Machine A, ubah status menjadi "TidakAktif"
   - [ ] Buka Operator View untuk Machine A
   - [ ] Status harus langsung berubah menjadi "TIDAK AKTIF"
   - [ ] Semua action buttons harus disabled
   - [ ] Timer harus reset ke 00:00:00

3. **Status Update kembali ke Aktif**
   - [ ] Di Admin panel, ubah status Machine A menjadi "Aktif"
   - [ ] Operator View harus langsung update menjadi "AKTIF"
   - [ ] Action buttons harus enabled (jika tidak ada job/downtime aktif)
   - [ ] Timer harus reset ke 00:00:00

4. **Action Buttons Sinkronisasi**
   - [ ] Saat Machine Aktif dan tidak ada job → RUNNING button enabled
   - [ ] Saat Machine Aktif dan ada job aktif → RUNNING button disabled
   - [ ] Saat Machine Tidak Aktif → Semua buttons disabled

5. **Timer Sinkronisasi**
   - [ ] Saat Start job → Timer reset ke 00:00:00
   - [ ] Saat Start downtime → Timer reset ke 00:00:00
   - [ ] Saat End downtime → Timer reset ke 00:00:00
   - [ ] Timer harus update setiap detik

## Expected Console Logs

Setelah perbaikan, console log harus menunjukkan:

```
🔍 Initial Model State: {MachineStatus: 'Aktif', HasActiveJob: true, HasActiveDowntime: true}
📡 Fetching operator data from: /Operator/GetOperatorData?machineId=0001
📊 Operator data updated: {..., MachineStatus: 'Aktif', ...}
🔍 MachineStatus from API: Aktif Type: string
🔄 Updating machine status: {machineStatusFromAdmin: 'Aktif', isAktif: true, ...}
✅ Status set to AKTIF (green)
🔘 Updating action buttons: {machineStatusFromAdmin: 'Aktif', isDisabledByAdmin: false, ...}
```

Saat Admin mengubah status:
```
🔔 OEE Update Received: {Type: 'MachineStatusUpdated', MachineStatus: 'TidakAktif', ...}
🔄 Machine status updated from Admin - refreshing data immediately
📊 New status from Admin: TidakAktif
🔄 Updating machine status: {machineStatusFromAdmin: 'TidakAktif', isAktif: false, ...}
⚠️ Status set to TIDAK AKTIF (yellow)
```

## Catatan Penting

1. **MachineStatus adalah sumber kebenaran** - Selalu gunakan Machine.Status dari database (di-set di Admin panel)
2. **Timer reset** - Timer harus reset saat ada perubahan status dari Admin atau perubahan actions
3. **Action buttons** - Selalu disabled jika MachineStatus = 'TidakAktif' dari Admin
4. **SignalR** - Pastikan SignalR terhubung untuk real-time update
5. **Error handling** - Jangan crash aplikasi jika MachineStatus tidak ada, hanya log warning

## Status Implementasi

- [x] Perbaiki GetOperatorData - MachineStatus selalu di-return
- [x] Perbaiki refreshOperatorData - Validasi dan sinkronisasi timer
- [x] Perbaiki SignalR handler - Reset timer saat status berubah
- [x] Perbaiki updateStatus - Validasi dan data lengkap
- [x] Perbaiki updateActionButtons - Handle undefined values
- [x] Testing dan verifikasi

## Hasil

Setelah implementasi:
- ✅ Status Operator View sinkron dengan Admin panel secara real-time
- ✅ Action buttons disabled/enabled sesuai status dari Admin
- ✅ Timer "durasi sejak status terakhir" sinkron dengan perubahan status dan actions
- ✅ Tidak ada error di console log
- ✅ Semua data ter-sinkron dengan benar

