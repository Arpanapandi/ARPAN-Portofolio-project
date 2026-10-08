# 🧪 Quick Test - Real-Time Update

## ⚡ Test Fitur Real-Time Update dalam 5 Menit!

### Prerequisites
1. ✅ Aplikasi sudah running (`dotnet run`)
2. ✅ Database sudah ada data schedule hari ini
3. ✅ 2 browser tabs/windows siap

---

## 🎯 Test Scenario 1: Basic Auto-Update

### Langkah-langkah:

#### 1. **Buka Dashboard** (Tab/Browser 1)
```
URL: http://localhost:5000
Menu: Dashboard
```

**✅ Cek:**
- [ ] Status indicator di pojok kanan atas
- [ ] Seharusnya muncul "🟢 Live" (jika hijau = connected)
- [ ] Lihat console browser (F12), seharusnya ada log:
  ```
  ✅ SignalR Connected! Connection ID: xxxxx
  ```

#### 2. **Buka Portal Driver** (Tab/Browser 2)
```
URL: http://localhost:5000/Driver
Menu: Portal Driver
```

**✅ Cek:**
- [ ] Muncul list schedule hari ini
- [ ] Ada tombol "Konfirmasi Kedatangan" / "Konfirmasi Keberangkatan"

#### 3. **Konfirmasi Arrival di Portal Driver**
- Pilih salah satu schedule yang belum ada actual time
- Klik tombol **"Quick Arrival"** atau **"Konfirmasi Kedatangan"**
- Submit form

#### 4. **Lihat Dashboard (Tab 1)** - JANGAN REFRESH!
**✅ Expected Result:**
- [ ] **Toast notification muncul** (pojok kanan atas)
  - Contoh: "🚚 Driver telah tiba di [Customer] pada [Time]"
- [ ] **Card "Sedang Berjalan" bertambah** (dari X ke X+1)
- [ ] **Card "Belum Datang" berkurang** (dari Y ke Y-1)
- [ ] **Tabel otomatis update** - row yang di-update berubah statusnya
- [ ] **Semua terjadi TANPA refresh browser!**

---

## 🎯 Test Scenario 2: Multiple Users

### Langkah-langkah:

#### 1. **Buka Dashboard di 3 Browser Berbeda**
- Browser A: Chrome - Dashboard
- Browser B: Firefox - Dashboard  
- Browser C: Edge - Portal Driver

**✅ Cek semua dashboard:**
- [ ] Ketiga dashboard menampilkan status "🟢 Live"

#### 2. **Konfirmasi Departure di Browser C**
- Di Portal Driver (Browser C)
- Pilih schedule yang sudah ada arrival time
- Klik **"Quick Departure"**

#### 3. **Lihat Dashboard di Browser A & B** - JANGAN REFRESH!
**✅ Expected Result:**
- [ ] **Kedua dashboard (A & B) update bersamaan**
- [ ] **Toast notification muncul di A & B secara simultan**
- [ ] **Card "Completed" bertambah di A & B**
- [ ] **Card "In Progress" berkurang di A & B**
- [ ] **Tabel update di A & B dengan data yang sama**

---

## 🎯 Test Scenario 3: Reconnection

### Langkah-langkah:

#### 1. **Buka Dashboard**
```
URL: http://localhost:5000
```
- Status indicator: 🟢 Live

#### 2. **Disconnect Internet** (Simulasi)
- Windows: Disable network adapter
- Atau: Matikan WiFi sebentar

**✅ Cek:**
- [ ] Status indicator berubah: 🟡 Reconnecting...
- [ ] Console log: "⚠️ SignalR reconnecting..."

#### 3. **Connect Internet Lagi**
**✅ Expected Result:**
- [ ] Status indicator: 🟢 Live
- [ ] Console log: "✅ SignalR reconnected!"
- [ ] Dashboard auto-refresh data (fetch latest data)

---

## 🎯 Test Scenario 4: Console Monitoring

### Buka Browser Console (F12)

#### Expected Console Logs:

```javascript
// Saat page load
🚀 Initializing SignalR connection...
✅ SignalR connection started successfully!
✅ SignalR Connected! Connection ID: xxxxx-xxxxx-xxxxx

// Saat ada update dari driver
🔔 Delivery Update Received: {
  scheduleNumber: "SCH-20251123-001",
  action: "arrival",
  message: "Driver telah tiba di PT ABC pada 10:30",
  timestamp: "2025-11-23T10:30:00"
}
📊 Statistics updated: {
  todaySchedules: 10,
  completedCount: 3,
  inProgressCount: 2,
  delayPickupCount: 1,
  notArrivedCount: 4
}
📋 Table data updated

// Saat reconnection
⚠️ SignalR reconnecting...
✅ SignalR reconnected! Connection ID: yyyyy-yyyyy-yyyyy
```

---

## 📊 Checklist Hasil Test

### ✅ Functional Tests
- [ ] Auto-update dashboard tanpa refresh
- [ ] Toast notification muncul
- [ ] Statistics cards update
- [ ] Table rows update
- [ ] Multiple users update simultan
- [ ] Reconnection works
- [ ] Status indicator accurate

### ✅ Performance Tests
- [ ] Update latency < 1 detik
- [ ] Tidak ada lag/freeze UI
- [ ] Memory usage normal (check Task Manager)
- [ ] CPU usage minimal

### ✅ UI/UX Tests
- [ ] Notifikasi tidak mengganggu
- [ ] Animasi smooth (fade in/out)
- [ ] Status indicator jelas
- [ ] Tidak perlu refresh manual

---

## 🐛 Jika Ada Masalah

### Problem 1: Status stuck di "Connecting..."
**Solusi:**
1. Check apakah server running: `dotnet run`
2. Check port: Pastikan port tidak bentrok
3. Refresh browser (Ctrl+F5)
4. Check console untuk error

### Problem 2: Dashboard tidak update
**Solusi:**
1. Check status indicator - harus 🟢 Live
2. Check console - cari error message
3. Check network tab (F12) - cari WebSocket connection
4. Pastikan tidak ada firewall block WebSocket

### Problem 3: Notification tidak muncul
**Solusi:**
1. Check jQuery loaded: `typeof jQuery` di console
2. Check Bootstrap JS loaded
3. Clear browser cache
4. Refresh browser (Ctrl+F5)

### Problem 4: Multiple users tidak sync
**Solusi:**
1. Pastikan semua browser connected (🟢 Live)
2. Check server console - pastikan broadcast terkirim
3. Check setiap client console untuk event received

---

## 📈 Expected Performance

### Timing Benchmarks:
- **Connection Time**: < 200ms
- **Update Latency**: < 100ms (local)
- **Update Latency**: < 500ms (remote)
- **Reconnect Time**: < 2 seconds

### Resource Usage:
- **Memory per Tab**: ~50-70MB (normal browser tab)
- **CPU**: < 1% idle, < 5% during update
- **Network**: ~5KB per update

---

## ✅ Test Complete!

Jika semua checklist di atas passed (✅), berarti implementasi SignalR real-time update **BERHASIL**! 🎉

### Next Steps:
1. Test dengan user real di production
2. Monitor server logs untuk performance
3. Collect user feedback

### Dokumentasi:
- Technical: `REALTIME_UPDATE_GUIDE.md`
- User Guide: `CARA_PAKAI_REALTIME_UPDATE.md`
- Changelog: `CHANGELOG_REALTIME.md`

---

**Happy Testing! 🚀**

