using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Hubs;
using dashboardKlinik.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace dashboardKlinik.Services
{
    /// <summary>
    /// Background service yang polling Google Spreadsheet setiap N detik.
    /// Jika ada baris baru, data langsung disimpan ke database dan
    /// dashboard diperbarui via SignalR.
    ///
    /// Format kolom spreadsheet (dari Google Form):
    /// [0] Timestamp (otomatis)
    /// [1] Tanggal Kunjungan
    /// [2] Nama Pasien
    /// [3] NPK
    /// [4] Plant
    /// [5] Departemen
    /// [6] Jenis Kelamin
    /// [7] Keluhan
    /// [8] Diagnosa
    /// [9] Dokter
    /// [10] Status
    /// </summary>
    public class GoogleSheetPollingService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<KlinikHub> _hubContext;
        private readonly ILogger<GoogleSheetPollingService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        // Track jumlah baris yang sudah diproses (exclude header)
        private int _lastProcessedRowCount = 0;
        private string _lastDataHash = string.Empty;
        private readonly int _pollingIntervalSeconds;

        // Dynamic Column Indices
        private int _idxTimestamp = 0;
        private int _idxTanggal = 1;
        private int _idxNama = 2;
        private int _idxNpk = 3;
        private int _idxPlant = 4;
        private int _idxDept = 5;
        private int _idxGender = 6;
        private int _idxKeluhan = 7;
        private int _idxDiagnosa = 8;
        private int _idxDokter = 9;
        private int _idxStatus = 10;
        private bool _columnsMapped = false;

        public GoogleSheetPollingService(
            IServiceProvider serviceProvider,
            IHubContext<KlinikHub> hubContext,
            ILogger<GoogleSheetPollingService> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
            // Default 5 detik untuk near-real-time
            _pollingIntervalSeconds = configuration.GetValue<int>("GoogleSheet:PollingIntervalSeconds", 5);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ Google Sheet Polling Service dimulai. Interval: {Interval} detik", _pollingIntervalSeconds);

            // Kirim status aktif ke dashboard
            await SafeSendAsync("ServiceStatus", new
            {
                status = "active",
                message = $"Real-time sync aktif (polling {_pollingIntervalSeconds}s)",
                interval = _pollingIntervalSeconds
            }, stoppingToken);

            // Set baseline awal - catat jumlah baris yang sudah ada
            await InitializeBaselineAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollForNewRowsAsync(stoppingToken);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saat polling Google Sheet");
                }

                await Task.Delay(TimeSpan.FromSeconds(_pollingIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("Google Sheet Polling Service dihentikan.");
        }

        // --------------------------------------------------------
        // Set baseline: catat berapa baris yang sudah ada di sheet
        // --------------------------------------------------------
        private async Task InitializeBaselineAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // JIka database kosong (sama sekali belum ada kunjungan), 
                // jangan set baseline supaya semua data ditarik di awal
                bool hasData = await context.KunjunganKlinik.AnyAsync(stoppingToken);
                if (!hasData)
                {
                    _lastProcessedRowCount = 0;
                    _lastDataHash = "";
                    _logger.LogInformation("Database kosong, sinkronisasi penuh akan dilakukan pada poll pertama.");
                    return;
                }

                // Set baseline ke jumlah data di database, bukan di sheet
                // Supaya jika ada selisih, data yang kurang akan ditarik pada poll pertama
                _lastProcessedRowCount = await context.KunjunganKlinik.CountAsync(stoppingToken);
                
                var fullCsv = await FetchFullCsvAsync(stoppingToken);
                if (string.IsNullOrEmpty(fullCsv)) return;

                var lines = fullCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0) MapColumns(lines[0]);

                var rows = lines.Length > 1 ? lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList() : new List<string>();
                
                // CRUCIAL: Jangan simpan hash jika database kosong agar poll pertama tetap melakukan sync
                _lastDataHash = _lastProcessedRowCount > 0 ? ComputeHash(string.Join("|", rows)) : "";
                
                _logger.LogInformation("Baseline: {Count} data di DB. Hash: {Hash}", _lastProcessedRowCount, _lastDataHash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gagal baseline: {Message}", ex.Message);
            }
        }

        private async Task PollForNewRowsAsync(CancellationToken stoppingToken)
        {
            var spreadsheetId = _configuration["GoogleSheet:SpreadsheetId"] ?? "";
            if (string.IsNullOrEmpty(spreadsheetId)) return;

            var fullCsv = await FetchFullCsvAsync(stoppingToken);
            if (string.IsNullOrEmpty(fullCsv)) return;

            var lines = fullCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return;

            if (!_columnsMapped) MapColumns(lines[0]);

            var rows = lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var currentHash = ComputeHash(string.Join("|", rows));
            var currentCount = rows.Count;

            // Jika jumlah baris berubah, kita WAJIB proses (abaikan hash)
            if (currentCount != _lastProcessedRowCount)
            {
                _logger.LogInformation("📊 Sinkronisasi: {Old} -> {New} baris.", _lastProcessedRowCount, currentCount);
            }
            else if (currentHash == _lastDataHash)
            {
                // Jika jumlah sama DAN konten sama, baru kita skip
                return;
            }

            // Ada baris baru atau DB kosong!
            var newRows = _lastProcessedRowCount == 0 ? rows : rows.Skip(_lastProcessedRowCount).ToList();
            _logger.LogInformation("🆕 {Count} baris baru terdeteksi di Google Sheet!", newRows.Count);

            await SafeSendAsync("SyncStarted", new
            {
                message = $"Data baru terdeteksi ({newRows.Count} baris)! Menyimpan...",
                timestamp = DateTime.Now
            }, stoppingToken);

            // Proses hanya baris baru
            var (added, errors) = await SaveNewRowsAsync(newRows, stoppingToken);

            _lastDataHash = currentHash;
            _lastProcessedRowCount = currentCount;

            // Ambil stats terbaru
            var stats = await GetDashboardStatsAsync(stoppingToken);

            // Broadcast ke semua client dashboard
            await SafeSendAsync("NewDataReceived", new
            {
                message = $"✅ {added} data baru berhasil disimpan!",
                added,
                errors,
                timestamp = DateTime.Now,
                stats
            }, stoppingToken);

            _logger.LogInformation("✅ Sync selesai: {Added} disimpan, {Errors} error", added, errors);
        }

        // --------------------------------------------------------
        // Ambil CSV utuh dari spreadsheet
        // --------------------------------------------------------
        private async Task<string?> FetchFullCsvAsync(CancellationToken stoppingToken)
        {
            var spreadsheetId = _configuration["GoogleSheet:SpreadsheetId"] ?? "";
            if (string.IsNullOrEmpty(spreadsheetId)) return null;

            var csvUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv";

            try
            {
                return await _httpClient.GetStringAsync(csvUrl, stoppingToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("Tidak bisa mengakses Google Sheet: {Message}", ex.Message);
                return null;
            }
        }

        private void MapColumns(string headerLine)
        {
            var cols = ParseCsvLine(headerLine);
            for (int i = 0; i < cols.Length; i++)
            {
                var h = cols[i].Trim().ToLower();
                if (h.Contains("timestamp")) _idxTimestamp = i;
                else if (h.Contains("tanggal") && h.Contains("kunjungan")) _idxTanggal = i;
                else if (h.Contains("nama") && h.Contains("pasien")) _idxNama = i;
                else if (h.Contains("npk")) _idxNpk = i;
                else if (h.Contains("plant")) _idxPlant = i;
                else if (h.Contains("departemen") || h == "dept") _idxDept = i;
                else if (h.Contains("kelamin") || h == "gender") _idxGender = i;
                else if (h.Contains("keluhan")) _idxKeluhan = i;
                else if (h.Contains("diagnosa")) _idxDiagnosa = i;
                else if (h.Contains("dokter")) _idxDokter = i;
                else if (h.Contains("status")) _idxStatus = i;
            }
            _columnsMapped = true;
            _logger.LogInformation("Columns Mapped: Nama={N}, NPK={P}, Tanggal={T}, Status={S}", _idxNama, _idxNpk, _idxTanggal, _idxStatus);
        }

        private string GetCol(string[] cols, int idx) => idx < cols.Length ? cols[idx].Trim() : "";

        // --------------------------------------------------------
        // Simpan baris-baris baru ke database
        // --------------------------------------------------------
        private async Task<(int added, int errors)> SaveNewRowsAsync(List<string> newRows, CancellationToken stoppingToken)
        {
            int added = 0, errors = 0;

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            foreach (var line in newRows)
            {
                try
                {
                    var cols = ParseCsvLine(line);
                    if (cols.Length <= Math.Max(_idxNama, _idxNpk)) continue;

                    var now = DateTime.Now;

                    // Parse Timestamp
                    DateTime sheetTimestamp = now;
                    if (_idxTimestamp < cols.Length) {
                        DateTime.TryParseExact(cols[_idxTimestamp].Trim(),
                            new[] { "M/d/yyyy H:mm:ss", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy HH:mm:ss" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out sheetTimestamp);
                    }
                    if (sheetTimestamp == default) sheetTimestamp = now;

                    // Parse Tanggal Kunjungan
                    DateTime tanggalKunjungan = sheetTimestamp.Date;
                    if (_idxTanggal < cols.Length) {
                        DateTime.TryParseExact(cols[_idxTanggal].Trim(),
                            new[] { "dd/MM/yyyy", "M/d/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out tanggalKunjungan);
                    }
                    if (tanggalKunjungan == default) tanggalKunjungan = sheetTimestamp.Date;

                    // Data Utama dengan index dinamis
                    var namaPasien   = GetCol(cols, _idxNama);
                    var npk          = GetCol(cols, _idxNpk);
                    var plant        = GetCol(cols, _idxPlant);
                    var departemen   = GetCol(cols, _idxDept);
                    var genderRaw    = GetCol(cols, _idxGender);
                    var jenisKelamin = genderRaw.Length > 0 ? genderRaw.Substring(0, 1).ToUpper() : "L";
                    var keluhan      = GetCol(cols, _idxKeluhan);
                    var diagnosa     = GetCol(cols, _idxDiagnosa);
                    var dokter       = GetCol(cols, _idxDokter);
                    var statusRaw    = GetCol(cols, _idxStatus);
                    
                    var status       = NormalizeStatus(statusRaw);

                    if (string.IsNullOrEmpty(namaPasien) || namaPasien == "Nama Pasien" || namaPasien == "ID") continue;
                    if (string.IsNullOrEmpty(npk)) continue;
                    
                    // FIX: Jika pasien tidak memiliki NPK atau diisi "-", buatkan NPK unik berdasarkan namanya
                    // Tujuannya agar riwayat mereka tidak tercampur ke orang lain yang juga mengisi "-"
                    if (npk.Trim() == "-" || npk.Trim().ToLower() == "strip")
                    {
                        string safeName = new string(namaPasien.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpper();
                        npk = $"GUEST-{safeName}";
                        if (npk.Length > 20) npk = npk.Substring(0, 20); // Batasi max 20 karakter
                    }

                    // Cari atau buat Pasien
                    var pasien = await context.Pasien.FirstOrDefaultAsync(p => p.NPK == npk, stoppingToken);
                    if (pasien == null)
                    {
                        _logger.LogInformation("Membuat pasien baru: {NPK}", npk);
                        pasien = new Pasien
                        {
                            NamaPasien = namaPasien,
                            NPK = npk,
                            Plant = plant,
                            Departemen = departemen,
                            JenisKelamin = jenisKelamin,
                            TanggalTerdaftar = tanggalKunjungan
                        };
                        context.Pasien.Add(pasien);
                        await context.SaveChangesAsync(stoppingToken);
                    }

                    // Hindari duplikat: cek pasien + tanggal + timestamp yang sama
                    var exists = await context.KunjunganKlinik.AnyAsync(
                        k => k.NPK == npk && k.TanggalKunjungan.Date == tanggalKunjungan.Date
                             && k.Timestamp == sheetTimestamp, stoppingToken);

                    if (!exists)
                    {
                        _logger.LogInformation("Menambahkan kunjungan baru untuk {NPK}", npk);
                        context.KunjunganKlinik.Add(new KunjunganKlinik
                        {
                            Timestamp = sheetTimestamp,
                            TanggalKunjungan = tanggalKunjungan,
                            NamaPasien = namaPasien,
                            NPK = npk,
                            Plant = plant,
                            Departemen = departemen,
                            JenisKelamin = jenisKelamin,
                            Keluhan = keluhan,
                            Diagnosa = diagnosa,
                            Dokter = dokter,
                            Status = status,
                            PasienId = pasien.Id
                        });
                        added++;
                    }
                    else {
                        _logger.LogInformation("Kunjungan sudah ada (duplikat): {NPK}", npk);
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogError(ex, "Error proses baris: {Message}", ex.Message);
                }
            }

            if (added > 0)
            {
                _logger.LogInformation("Menyimpan {Count} data baru ke database...", added);
                await context.SaveChangesAsync(stoppingToken);

                // Log aktivitas
                context.ActivityLog.Add(new ActivityLog
                {
                    Action = "AUTO_SYNC_SHEET",
                    Description = $"Google Sheet sync: {added} kunjungan baru ditambahkan",
                    EntityType = "KunjunganKlinik",
                    Timestamp = DateTime.Now
                });
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Berhasil simpan ke database.");
            }

            return (added, errors);
        }

        // --------------------------------------------------------
        // Ambil statistik terbaru untuk di-push ke dashboard
        // --------------------------------------------------------
        private async Task<object> GetDashboardStatsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var today = DateTime.Today;
            var firstOfMonth = new DateTime(today.Year, today.Month, 1);
            var kunjunganAll = await context.KunjunganKlinik.ToListAsync(stoppingToken);

            return new
            {
                totalPasien       = await context.Pasien.CountAsync(stoppingToken),
                totalKunjungan    = kunjunganAll.Count,
                kunjunganHariIni  = kunjunganAll.Count(k => k.TanggalKunjungan.Date == today),
                kunjunganBulanIni = kunjunganAll.Count(k => k.TanggalKunjungan >= firstOfMonth),
                rawatJalan        = kunjunganAll.Count(k => k.Status == "Rawat Jalan"),
                perawatan         = kunjunganAll.Count(k => k.Status == "Perawatan"),
                dirujuk           = kunjunganAll.Count(k => k.Status == "Dirujuk")
            };
        }

        // --------------------------------------------------------
        // Normalisasi nilai status dari form ke nilai database
        // --------------------------------------------------------
        private static string NormalizeStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "Rawat Jalan";
            var s = status.ToLower();
            if (s.Contains("rujuk"))     return "Dirujuk";
            if (s.Contains("perawatan")) return "Perawatan";
            return "Rawat Jalan";
        }

        // --------------------------------------------------------
        // Kirim SignalR dengan aman (tidak throw jika gagal)
        // --------------------------------------------------------
        private async Task SafeSendAsync(string method, object data, CancellationToken stoppingToken)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync(method, data, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SignalR send failed for {Method}: {Message}", method, ex.Message);
            }
        }

        // --------------------------------------------------------
        // Hitung hash SHA256 dari konten
        // --------------------------------------------------------
        private static string ComputeHash(string content)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
            return Convert.ToBase64String(bytes);
        }

        // --------------------------------------------------------
        // Parse baris CSV dengan dukungan tanda kutip
        // --------------------------------------------------------
        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
