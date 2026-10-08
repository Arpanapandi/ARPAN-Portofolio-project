using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Hubs;
using dashboardKlinik.Models;
using dashboardKlinik.Services;
using QRCoder;

namespace dashboardKlinik.Controllers
{
    public class AntrianController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<KlinikHub> _hubContext;
        private readonly ActivityLogService _activityLog;
        private readonly ILogger<AntrianController> _logger;

        public AntrianController(
            ApplicationDbContext context,
            IHubContext<KlinikHub> hubContext,
            ActivityLogService activityLog,
            ILogger<AntrianController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _activityLog = activityLog;
            _logger = logger;
        }

        // ==========================================
        // 1. FITUR PASIEN (PUBLIC / SCAN QR CODE)
        // ==========================================

        // GET: /Antrian/Daftar
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Daftar()
        {
            return View();
        }

        // GET: /Antrian/CariPasien?npk=1001
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> CariPasien(string npk)
        {
            if (string.IsNullOrWhiteSpace(npk))
            {
                return Json(new { found = false, message = "NPK tidak boleh kosong" });
            }

            var cleanNpk = npk.Trim();

            // 1. Cari di tabel Pasien
            var pasien = await _context.Pasien
                .FirstOrDefaultAsync(p => p.NPK == cleanNpk);

            if (pasien != null)
            {
                return Json(new
                {
                    found = true,
                    namaPasien = pasien.NamaPasien,
                    npk = pasien.NPK,
                    plant = pasien.Plant,
                    departemen = pasien.Departemen,
                    jenisKelamin = pasien.JenisKelamin,
                    source = "master"
                });
            }

            // 2. Fallback cari di riwayat KunjunganKlinik terakhir
            var lastKunjungan = await _context.KunjunganKlinik
                .Where(k => k.NPK == cleanNpk)
                .OrderByDescending(k => k.TanggalKunjungan)
                .FirstOrDefaultAsync();

            if (lastKunjungan != null)
            {
                return Json(new
                {
                    found = true,
                    namaPasien = lastKunjungan.NamaPasien,
                    npk = lastKunjungan.NPK,
                    plant = lastKunjungan.Plant,
                    departemen = lastKunjungan.Departemen,
                    jenisKelamin = lastKunjungan.JenisKelamin,
                    source = "history"
                });
            }

            return Json(new { found = false, message = "Data pasien dengan NPK ini belum terdaftar. Silakan gunakan tab Pasien Baru." });
        }

        // POST: /Antrian/AmbilAntrian
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AmbilAntrian([FromForm] AmbilAntrianInputModel input)
        {
            if (input == null)
            {
                TempData["ErrorMessage"] = "Data pendaftaran tidak valid.";
                return RedirectToAction(nameof(Daftar));
            }

            var cleanNpk = input.NPK?.Trim() ?? "";
            var cleanNama = input.NamaPasien?.Trim() ?? "";

            // Validasi NPK 4 digit angka
            if (!Regex.IsMatch(cleanNpk, @"^\d{4}$"))
            {
                TempData["ErrorMessage"] = "NPK harus berupa 4 digit angka (contoh: 1001).";
                return RedirectToAction(nameof(Daftar));
            }

            if (string.IsNullOrWhiteSpace(cleanNama))
            {
                TempData["ErrorMessage"] = "Nama pasien wajib diisi.";
                return RedirectToAction(nameof(Daftar));
            }

            if (string.IsNullOrWhiteSpace(input.Keluhan))
            {
                TempData["ErrorMessage"] = "Keluhan wajib diisi.";
                return RedirectToAction(nameof(Daftar));
            }

            // Cari atau buat Pasien baru
            var pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.NPK == cleanNpk);
            if (pasien == null)
            {
                pasien = new Pasien
                {
                    NamaPasien = cleanNama,
                    NPK = cleanNpk,
                    Plant = input.Plant ?? "MOLDED",
                    Departemen = input.Departemen ?? "PRODUKSI",
                    JenisKelamin = input.JenisKelamin ?? "L",
                    TanggalTerdaftar = DateTime.Now
                };
                _context.Pasien.Add(pasien);
                await _context.SaveChangesAsync();
            }

            // Hitung nomor urut hari ini
            var today = DateTime.Today;
            var todayQueueCount = await _context.AntrianKlinik
                .CountAsync(a => a.Tanggal == today);

            var nomorUrut = todayQueueCount + 1;
            var nomorAntrian = $"A-{nomorUrut:D3}";

            var antrian = new AntrianKlinik
            {
                NomorAntrian = nomorAntrian,
                NomorUrut = nomorUrut,
                Tanggal = today,
                WaktuDaftar = DateTime.Now,
                NPK = cleanNpk,
                NamaPasien = cleanNama,
                Plant = input.Plant ?? pasien.Plant,
                Departemen = input.Departemen ?? pasien.Departemen,
                JenisKelamin = input.JenisKelamin ?? pasien.JenisKelamin,
                Keluhan = input.Keluhan.Trim(),
                StatusAntrian = "Menunggu",
                PasienId = pasien.Id
            };

            _context.AntrianKlinik.Add(antrian);
            await _context.SaveChangesAsync();

            await _activityLog.LogAsync("AMBIL_ANTRIAN", $"Nomor {nomorAntrian}: {antrian.NamaPasien} ({antrian.NPK}) mendaftar antrian", "AntrianKlinik", antrian.Id);

            // Broadcast SignalR update ke Dashboard Staf & Layar TV Display
            await BroadcastAntrianUpdateAsync();

            return RedirectToAction(nameof(Tiket), new { id = antrian.Id });
        }

        // GET: /Antrian/Tiket/5
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Tiket(int id)
        {
            var antrian = await _context.AntrianKlinik
                .Include(a => a.Pasien)
                .Include(a => a.Kunjungan)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (antrian == null)
            {
                return NotFound("Tiket antrian tidak ditemukan.");
            }

            // Set cookie for active ticket so patient can easily return from Dashboard
            Response.Cookies.Append("ActiveTicketId", id.ToString(), new CookieOptions
            {
                Expires = DateTimeOffset.Now.AddDays(1),
                HttpOnly = false
            });

            var today = DateTime.Today;
            var antrianAhead = await _context.AntrianKlinik
                .CountAsync(a => a.Tanggal == today && a.NomorUrut < antrian.NomorUrut && (a.StatusAntrian == "Menunggu" || a.StatusAntrian == "Dipanggil"));

            ViewData["AntrianAhead"] = antrianAhead;

            // Pastikan data kunjungan terisi jika status sudah selesai atau terhubung
            var kunjungan = antrian.Kunjungan;
            if (kunjungan == null && antrian.KunjunganId.HasValue)
            {
                kunjungan = await _context.KunjunganKlinik.FindAsync(antrian.KunjunganId.Value);
            }
            if (kunjungan == null && antrian.StatusAntrian == "Selesai")
            {
                kunjungan = await _context.KunjunganKlinik
                    .Where(k => k.NPK == antrian.NPK && k.TanggalKunjungan == antrian.Tanggal)
                    .OrderByDescending(k => k.Timestamp)
                    .FirstOrDefaultAsync();
            }
            ViewData["Kunjungan"] = kunjungan;

            // Jika sudah selesai atau ada rekam medis kunjungan, ambil riwayat kunjungan pasien
            var riwayatList = await _context.KunjunganKlinik
                .Where(k => k.NPK == antrian.NPK)
                .OrderByDescending(k => k.TanggalKunjungan)
                .ThenByDescending(k => k.Timestamp)
                .Take(10)
                .ToListAsync();

            ViewData["RiwayatList"] = riwayatList;

            return View(antrian);
        }

        // GET: /Antrian/GetTiketStatus/5
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetTiketStatus(int id)
        {
            var antrian = await _context.AntrianKlinik
                .Include(a => a.Kunjungan)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (antrian == null)
            {
                return NotFound(new { success = false });
            }

            var today = DateTime.Today;
            var antrianAhead = await _context.AntrianKlinik
                .CountAsync(a => a.Tanggal == today && a.NomorUrut < antrian.NomorUrut && (a.StatusAntrian == "Menunggu" || a.StatusAntrian == "Dipanggil"));

            var hasKunjungan = antrian.KunjunganId.HasValue || antrian.StatusAntrian == "Selesai";

            return Json(new
            {
                success = true,
                id = antrian.Id,
                nomorAntrian = antrian.NomorAntrian,
                status = antrian.StatusAntrian,
                antrianAhead = antrianAhead,
                waktuDipanggil = antrian.WaktuDipanggil?.ToString("HH:mm"),
                hasKunjungan = hasKunjungan,
                kunjunganId = antrian.KunjunganId
            });
        }

        // ==========================================
        // 2. TAMPILAN DISPLAY TV / MONITOR KLINIK
        // ==========================================

        // GET: /Antrian/Display
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Display()
        {
            var today = DateTime.Today;
            var antrianHariIni = await _context.AntrianKlinik
                .Where(a => a.Tanggal == today)
                .OrderBy(a => a.NomorUrut)
                .ToListAsync();

            var sedangDipanggil = antrianHariIni
                .Where(a => a.StatusAntrian == "Dipanggil" || a.StatusAntrian == "Sedang Diperiksa")
                .OrderByDescending(a => a.WaktuDipanggil ?? a.WaktuDaftar)
                .FirstOrDefault();

            var antrianMenunggu = antrianHariIni
                .Where(a => a.StatusAntrian == "Menunggu")
                .Take(5)
                .ToList();

            ViewData["SedangDipanggil"] = sedangDipanggil;
            ViewData["AntrianMenunggu"] = antrianMenunggu;
            ViewData["TotalHariIni"] = antrianHariIni.Count;
            ViewData["TotalMenunggu"] = antrianHariIni.Count(a => a.StatusAntrian == "Menunggu");
            ViewData["TotalSelesai"] = antrianHariIni.Count(a => a.StatusAntrian == "Selesai");

            return View(antrianHariIni);
        }

        // GET: /Antrian/GetDisplayData (JSON for live TV poll/update)
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetDisplayData()
        {
            var today = DateTime.Today;
            var antrianHariIni = await _context.AntrianKlinik
                .Where(a => a.Tanggal == today)
                .OrderBy(a => a.NomorUrut)
                .ToListAsync();

            var sedangDipanggil = antrianHariIni
                .Where(a => a.StatusAntrian == "Dipanggil" || a.StatusAntrian == "Sedang Diperiksa")
                .OrderByDescending(a => a.WaktuDipanggil ?? a.WaktuDaftar)
                .FirstOrDefault();

            var antrianMenunggu = antrianHariIni
                .Where(a => a.StatusAntrian == "Menunggu")
                .Take(6)
                .Select(a => new
                {
                    id = a.Id,
                    nomorAntrian = a.NomorAntrian,
                    namaPasien = a.NamaPasien,
                    plant = a.Plant,
                    departemen = a.Departemen,
                    waktuDaftar = a.WaktuDaftar.ToString("HH:mm")
                })
                .ToList();

            return Json(new
            {
                success = true,
                totalHariIni = antrianHariIni.Count,
                totalMenunggu = antrianHariIni.Count(a => a.StatusAntrian == "Menunggu"),
                totalSelesai = antrianHariIni.Count(a => a.StatusAntrian == "Selesai"),
                sedangDipanggil = sedangDipanggil == null ? null : new
                {
                    id = sedangDipanggil.Id,
                    nomorAntrian = sedangDipanggil.NomorAntrian,
                    namaPasien = sedangDipanggil.NamaPasien,
                    plant = sedangDipanggil.Plant,
                    departemen = sedangDipanggil.Departemen,
                    status = sedangDipanggil.StatusAntrian
                },
                antrianMenunggu = antrianMenunggu
            });
        }

        // ==========================================
        // 3. FITUR STAF KLINIK (PROTECTED / [Authorize])
        // ==========================================

        // GET: /Antrian / /Antrian/Index
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Index(string? status)
        {
            if (User.IsInRole("Guest")) return RedirectToAction("Index", "Home");

            var today = DateTime.Today;
            var query = _context.AntrianKlinik.Where(a => a.Tanggal == today);

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.StatusAntrian == status);
            }

            ViewData["CurrentStatus"] = status;

            var list = await query
                .OrderBy(a => a.NomorUrut)
                .ToListAsync();

            // Total counts
            ViewData["TotalHariIni"] = await _context.AntrianKlinik.CountAsync(a => a.Tanggal == today);
            ViewData["TotalMenunggu"] = await _context.AntrianKlinik.CountAsync(a => a.Tanggal == today && a.StatusAntrian == "Menunggu");
            ViewData["TotalDipanggil"] = await _context.AntrianKlinik.CountAsync(a => a.Tanggal == today && a.StatusAntrian == "Dipanggil");
            ViewData["TotalDiperiksa"] = await _context.AntrianKlinik.CountAsync(a => a.Tanggal == today && a.StatusAntrian == "Sedang Diperiksa");
            ViewData["TotalSelesai"] = await _context.AntrianKlinik.CountAsync(a => a.Tanggal == today && a.StatusAntrian == "Selesai");

            return View(list);
        }

        // POST: /Antrian/Panggil/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Panggil(int id)
        {
            var antrian = await _context.AntrianKlinik.FindAsync(id);
            if (antrian == null)
            {
                TempData["ErrorMessage"] = "Antrian tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }

            antrian.StatusAntrian = "Dipanggil";
            antrian.WaktuDipanggil = DateTime.Now;
            await _context.SaveChangesAsync();

            await _activityLog.LogAsync("PANGGIL_ANTRIAN", $"Memanggil antrian {antrian.NomorAntrian}: {antrian.NamaPasien}", "AntrianKlinik", antrian.Id);

            // Broadcast SignalR Voice Announcement ke Layar Display TV & Staf
            await _hubContext.Clients.All.SendAsync("PanggilAntrianVoice", new
            {
                id = antrian.Id,
                nomorAntrian = antrian.NomorAntrian,
                namaPasien = antrian.NamaPasien,
                plant = antrian.Plant,
                departemen = antrian.Departemen,
                pesanSuara = $"Nomor antrian {antrian.NomorAntrian}, atas nama {antrian.NamaPasien}, silakan menuju ruang periksa"
            });

            await BroadcastAntrianUpdateAsync();

            TempData["SuccessMessage"] = $"Nomor antrian {antrian.NomorAntrian} ({antrian.NamaPasien}) sedang dipanggil!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Antrian/MulaiPeriksa/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MulaiPeriksa(int id)
        {
            var antrian = await _context.AntrianKlinik.FindAsync(id);
            if (antrian != null)
            {
                antrian.StatusAntrian = "Sedang Diperiksa";
                await _context.SaveChangesAsync();
                await BroadcastAntrianUpdateAsync();
                TempData["SuccessMessage"] = $"Pasien {antrian.NamaPasien} ({antrian.NomorAntrian}) sedang dalam pemeriksaan.";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Antrian/Batal/5
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Batal(int id)
        {
            var antrian = await _context.AntrianKlinik.FindAsync(id);
            if (antrian != null)
            {
                antrian.StatusAntrian = "Batal";
                await _context.SaveChangesAsync();
                await BroadcastAntrianUpdateAsync();
                TempData["SuccessMessage"] = $"Antrian {antrian.NomorAntrian} berhasil dibatalkan.";
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: /Antrian/CetakQR
        [HttpGet]
        [Authorize]
        public IActionResult CetakQR()
        {
            if (User.IsInRole("Guest")) return RedirectToAction("Index", "Home");

            var hostName = System.Net.Dns.GetHostName();
            var ipList = System.Net.Dns.GetHostAddresses(hostName)
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                .Select(ip => ip.ToString())
                .ToList();

            var port = HttpContext.Request.Host.Port ?? 5000;
            var primaryIp = ipList.FirstOrDefault(ip => ip.StartsWith("10.") || ip.StartsWith("192.168.")) ?? ipList.FirstOrDefault() ?? "10.14.173.70";
            var qrTargetUrl = $"http://{primaryIp}:{port}/Antrian/Daftar";

            ViewData["QrTargetUrl"] = qrTargetUrl;
            ViewData["PrimaryIp"] = primaryIp;
            ViewData["IpList"] = ipList;
            ViewData["CurrentPort"] = port;
            return View();
        }

        // GET: /Antrian/GenerateQrCodeImage?url=...
        [HttpGet]
        [AllowAnonymous]
        public IActionResult GenerateQrCodeImage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "http://10.14.173.70:5000/Antrian/Daftar";
            }

            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new PngByteQRCode(qrCodeData);
                byte[] qrCodeBytes = qrCode.GetGraphic(20);
                return File(qrCodeBytes, "image/png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code image for url: {Url}", url);
                return BadRequest();
            }
        }

        private async Task BroadcastAntrianUpdateAsync()
        {
            try
            {
                var today = DateTime.Today;
                var list = await _context.AntrianKlinik
                    .Where(a => a.Tanggal == today)
                    .OrderBy(a => a.NomorUrut)
                    .ToListAsync();

                var stats = new
                {
                    totalHariIni = list.Count,
                    menunggu = list.Count(a => a.StatusAntrian == "Menunggu"),
                    dipanggil = list.Count(a => a.StatusAntrian == "Dipanggil"),
                    diperiksa = list.Count(a => a.StatusAntrian == "Sedang Diperiksa"),
                    selesai = list.Count(a => a.StatusAntrian == "Selesai")
                };

                await _hubContext.Clients.All.SendAsync("AntrianUpdated", new
                {
                    timestamp = DateTime.Now,
                    stats = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting Antrian SignalR update");
            }
        }
    }

    public class AmbilAntrianInputModel
    {
        public string NPK { get; set; } = string.Empty;
        public string NamaPasien { get; set; } = string.Empty;
        public string? Plant { get; set; }
        public string? Departemen { get; set; }
        public string? JenisKelamin { get; set; }
        public string Keluhan { get; set; } = string.Empty;
    }
}
