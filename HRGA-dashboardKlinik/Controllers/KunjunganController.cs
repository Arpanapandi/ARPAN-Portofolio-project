using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Hubs;
using dashboardKlinik.Models;
using dashboardKlinik.Services;

namespace dashboardKlinik.Controllers
{
    [Authorize]
    public class KunjunganController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;
        private readonly IHubContext<KlinikHub> _hubContext;

        public KunjunganController(
            ApplicationDbContext context, 
            ActivityLogService activityLog,
            IHubContext<KlinikHub> hubContext)
        {
            _context = context;
            _activityLog = activityLog;
            _hubContext = hubContext;
        }

        // GET: Kunjungan
        public async Task<IActionResult> Index(string searchString, string status, string plant, DateTime? startDate, DateTime? endDate, int page = 1)
        {
            if (User.IsInRole("Guest")) return RedirectToAction("Index", "Home");

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentStatus"] = status;
            ViewData["CurrentPlant"] = plant;
            ViewData["CurrentStartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["CurrentEndDate"] = endDate?.ToString("yyyy-MM-dd");

            var query = _context.KunjunganKlinik.AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(k =>
                    k.NamaPasien.Contains(searchString) ||
                    k.NPK.Contains(searchString) ||
                    k.Keluhan.Contains(searchString) ||
                    k.Diagnosa.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(status))
                query = query.Where(k => k.Status == status);

            if (!string.IsNullOrEmpty(plant))
                query = query.Where(k => k.Plant == plant);

            if (startDate.HasValue)
                query = query.Where(k => k.TanggalKunjungan >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(k => k.TanggalKunjungan <= endDate.Value);

            int pageSize = 10;
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            ViewData["TotalPages"] = totalPages;
            ViewData["CurrentPage"] = page;
            ViewData["TotalItems"] = totalItems;

            ViewData["Plants"] = await _context.KunjunganKlinik.Select(k => k.Plant).Distinct().OrderBy(p => p).ToListAsync();

            var kunjungan = await query
                .OrderByDescending(k => k.TanggalKunjungan)
                .ThenByDescending(k => k.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(kunjungan);
        }

        // GET: Kunjungan/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var kunjungan = await _context.KunjunganKlinik
                .Include(k => k.Pasien)
                .FirstOrDefaultAsync(k => k.Id == id);

            if (kunjungan == null) return NotFound();

            return View(kunjungan);
        }

        // GET: Kunjungan/Create?antrianId=5
        public async Task<IActionResult> Create(int? antrianId)
        {
            var model = new KunjunganKlinik
            {
                TanggalKunjungan = DateTime.Today,
                Status = "Rawat Jalan"
            };

            if (antrianId.HasValue)
            {
                var antrian = await _context.AntrianKlinik.FindAsync(antrianId.Value);
                if (antrian != null)
                {
                    model.NamaPasien = antrian.NamaPasien;
                    model.NPK = antrian.NPK;
                    model.Plant = antrian.Plant;
                    model.Departemen = antrian.Departemen;
                    model.JenisKelamin = antrian.JenisKelamin;
                    model.Keluhan = antrian.Keluhan;
                    model.TanggalKunjungan = antrian.Tanggal;
                    ViewData["AntrianId"] = antrian.Id;
                    ViewData["NomorAntrian"] = antrian.NomorAntrian;
                }
            }

            if (!string.IsNullOrEmpty(model.NPK))
            {
                var riwayatQuery = _context.KunjunganKlinik.AsQueryable();
                if (model.NPK != "-")
                {
                    riwayatQuery = riwayatQuery.Where(k => k.NPK == model.NPK);
                }
                else if (!string.IsNullOrEmpty(model.NamaPasien))
                {
                    riwayatQuery = riwayatQuery.Where(k => k.NamaPasien == model.NamaPasien);
                }

                ViewData["RiwayatKunjungan"] = await riwayatQuery
                    .OrderByDescending(k => k.TanggalKunjungan)
                    .ThenByDescending(k => k.Timestamp)
                    .ToListAsync();
            }

            return View(model);
        }

        // GET: Kunjungan/GetRiwayatJson?npk=1234&namaPasien=ASA
        [HttpGet]
        public async Task<IActionResult> GetRiwayatJson(string npk, string namaPasien)
        {
            if (string.IsNullOrEmpty(npk) && string.IsNullOrEmpty(namaPasien))
                return Json(new List<object>());

            var query = _context.KunjunganKlinik.AsQueryable();
            if (!string.IsNullOrEmpty(npk) && npk != "-")
            {
                query = query.Where(k => k.NPK == npk);
            }
            else if (!string.IsNullOrEmpty(namaPasien))
            {
                query = query.Where(k => k.NamaPasien == namaPasien);
            }
            else
            {
                return Json(new List<object>());
            }

            var list = await query
                .OrderByDescending(k => k.TanggalKunjungan)
                .ThenByDescending(k => k.Timestamp)
                .Take(20)
                .Select(k => new
                {
                    k.Id,
                    Tanggal = k.TanggalKunjungan.ToString("dd/MM/yyyy"),
                    k.NamaPasien,
                    k.NPK,
                    k.Plant,
                    k.Departemen,
                    k.Keluhan,
                    k.Diagnosa,
                    k.Dokter,
                    k.Status
                })
                .ToListAsync();

            return Json(list);
        }

        // POST: Kunjungan/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TanggalKunjungan,NamaPasien,NPK,Plant,Departemen,JenisKelamin,Keluhan,Diagnosa,Dokter,Status")] KunjunganKlinik kunjungan, int? antrianId)
        {
            if (ModelState.IsValid)
            {
                kunjungan.Timestamp = DateTime.Now;

                // Find or create patient
                var pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.NPK == kunjungan.NPK);
                if (pasien == null)
                {
                    pasien = new Pasien
                    {
                        NamaPasien = kunjungan.NamaPasien,
                        NPK = kunjungan.NPK,
                        Plant = kunjungan.Plant,
                        Departemen = kunjungan.Departemen,
                        JenisKelamin = kunjungan.JenisKelamin,
                        TanggalTerdaftar = DateTime.Now
                    };
                    _context.Pasien.Add(pasien);
                    await _context.SaveChangesAsync();
                }

                kunjungan.PasienId = pasien.Id;

                _context.Add(kunjungan);
                await _context.SaveChangesAsync();

                // Update status antrian jika berasal dari antrian
                if (antrianId.HasValue)
                {
                    var antrian = await _context.AntrianKlinik.FindAsync(antrianId.Value);
                    if (antrian != null)
                    {
                        antrian.StatusAntrian = "Selesai";
                        antrian.WaktuSelesai = DateTime.Now;
                        antrian.KunjunganId = kunjungan.Id;
                        await _context.SaveChangesAsync();

                        // Notifikasi SignalR ke Display TV & Device Pasien
                        try
                        {
                            await _hubContext.Clients.All.SendAsync("AntrianUpdated", new
                            {
                                timestamp = DateTime.Now,
                                antrianId = antrian.Id,
                                status = "Selesai"
                            });
                        }
                        catch { /* Ignored */ }
                    }
                }

                await _activityLog.LogAsync("CREATE", $"Kunjungan baru: {kunjungan.NamaPasien} - {kunjungan.Keluhan}", "KunjunganKlinik", kunjungan.Id);
                TempData["SuccessMessage"] = "Data kunjungan / rekam medis berhasil ditambahkan!";
                return RedirectToAction("Index", "Antrian");
            }
            return View(kunjungan);
        }

        // GET: Kunjungan/Riwayat?npk=1234&namaPasien=Arfan
        public async Task<IActionResult> Riwayat(string npk, string namaPasien = null)
        {
            if (string.IsNullOrEmpty(npk)) return RedirectToAction(nameof(Index));

            Pasien pasien = null;

            // Jika NPK adalah "-" (tidak ada NPK unik), kita cari berdasarkan Nama Pasien juga
            if (npk == "-" && !string.IsNullOrEmpty(namaPasien))
            {
                var lastVisit = await _context.KunjunganKlinik
                    .Where(k => k.NPK == npk && k.NamaPasien == namaPasien)
                    .OrderByDescending(k => k.TanggalKunjungan)
                    .FirstOrDefaultAsync();

                if (lastVisit == null) return NotFound();

                // Buat profil pasien virtual untuk tampilan riwayat
                pasien = new Pasien
                {
                    NamaPasien = lastVisit.NamaPasien,
                    NPK = lastVisit.NPK,
                    Plant = lastVisit.Plant,
                    Departemen = lastVisit.Departemen,
                    JenisKelamin = lastVisit.JenisKelamin
                };
            }
            else
            {
                pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.NPK == npk);
                if (pasien == null) return NotFound();
            }

            var kunjunganQuery = _context.KunjunganKlinik.Where(k => k.NPK == npk);

            // Filter riwayat spesifik untuk nama tersebut jika NPK "-"
            if (npk == "-" && !string.IsNullOrEmpty(namaPasien))
            {
                kunjunganQuery = kunjunganQuery.Where(k => k.NamaPasien == namaPasien);
            }

            var kunjungan = await kunjunganQuery
                .OrderByDescending(k => k.TanggalKunjungan)
                .ToListAsync();

            ViewData["Pasien"] = pasien;
            return View(kunjungan);
        }

        // POST: Kunjungan/DeleteMultiple
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMultiple(int[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["ErrorMessage"] = "Silakan pilih data yang akan dihapus.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var kunjunganToRemove = await _context.KunjunganKlinik
                    .Where(k => selectedIds.Contains(k.Id))
                    .ToListAsync();

                if (kunjunganToRemove.Any())
                {
                    _context.KunjunganKlinik.RemoveRange(kunjunganToRemove);
                    
                    await _activityLog.LogAsync("DELETE_MULTIPLE", $"Menghapus {kunjunganToRemove.Count} data kunjungan secara massal", "KunjunganKlinik", null);

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"{kunjunganToRemove.Count} data kunjungan berhasil dihapus.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Gagal menghapus data: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
