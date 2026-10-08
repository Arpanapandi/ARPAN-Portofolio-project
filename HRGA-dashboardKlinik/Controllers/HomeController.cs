using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.ViewModels;
using dashboardKlinik.Services;

namespace dashboardKlinik.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ApplicationDbContext context, ActivityLogService activityLog, ILogger<HomeController> logger)
        {
            _context = context;
            _activityLog = activityLog;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);

            var dbConn = _context.Database.GetDbConnection().ConnectionString;
            var maskedConn = dbConn.Contains("Password") ? "SQL Server (Production)" : "SQLite (Local)";
            
            var kunjunganAll = await _context.KunjunganKlinik.AsNoTracking().ToListAsync();
            _logger.LogInformation("📊 DASHBOARD: Memuat {Count} kunjungan. Database: {Conn}", kunjunganAll.Count, maskedConn);

            // Check if patient has an active ticket cookie
            if (Request.Cookies.TryGetValue("ActiveTicketId", out var activeTicketStr) && int.TryParse(activeTicketStr, out int activeTicketId))
            {
                var activeTicket = await _context.AntrianKlinik.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activeTicketId);
                if (activeTicket != null)
                {
                    ViewData["ActiveTicketId"] = activeTicket.Id;
                    ViewData["ActiveTicketNumber"] = activeTicket.NomorAntrian;
                    ViewData["ActiveTicketStatus"] = activeTicket.StatusAntrian;
                }
            }

            var viewModel = new DashboardViewModel
            {
                TotalPasien = await _context.Pasien.CountAsync(),
                TotalKunjungan = kunjunganAll.Count,
                KunjunganHariIni = kunjunganAll.Count(k => k.TanggalKunjungan.Date == today),
                KunjunganBulanIni = kunjunganAll.Count(k => k.TanggalKunjungan >= firstDayOfMonth),

                // Status
                RawatJalan = kunjunganAll.Count(k => k.Status == "Rawat Jalan"),
                Perawatan = kunjunganAll.Count(k => k.Status == "Perawatan"),
                Dirujuk = kunjunganAll.Count(k => k.Status == "Dirujuk"),

                // Gender
                PasienLakiLaki = await _context.Pasien.CountAsync(p => p.JenisKelamin == "L"),
                PasienPerempuan = await _context.Pasien.CountAsync(p => p.JenisKelamin == "P"),

                // Kunjungan per bulan (6 bulan terakhir)
                KunjunganPerBulan = kunjunganAll
                    .Where(k => k.TanggalKunjungan >= today.AddMonths(-5))
                    .GroupBy(k => new { k.TanggalKunjungan.Year, k.TanggalKunjungan.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new ChartDataItem
                    {
                        Label = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                        Value = g.Count()
                    }).ToList(),

                // Kunjungan per departemen
                KunjunganPerDepartemen = kunjunganAll
                    .GroupBy(k => k.Departemen)
                    .Select(g => new ChartDataItem
                    {
                        Label = g.Key,
                        Value = g.Count()
                    }).OrderByDescending(x => x.Value).ToList(),

                // Kunjungan per plant
                KunjunganPerPlant = kunjunganAll
                    .GroupBy(k => k.Plant)
                    .Select(g => new ChartDataItem
                    {
                        Label = g.Key,
                        Value = g.Count()
                    }).ToList(),

                // Top 10 diagnosa bulan ini
                DiagnosaPopuler = kunjunganAll
                    .Where(k => k.TanggalKunjungan >= firstDayOfMonth)
                    .GroupBy(k => k.Diagnosa)
                    .Select(g => new ChartDataItem
                    {
                        Label = g.Key,
                        Value = g.Count()
                    }).OrderByDescending(x => x.Value).Take(10).ToList(),

                // Kunjungan per hari (7 hari terakhir)
                KunjunganPerHari = Enumerable.Range(0, 7)
                    .Select(i => today.AddDays(-6 + i))
                    .Select(d => new ChartDataItem
                    {
                        Label = d.ToString("dd MMM"),
                        Value = kunjunganAll.Count(k => k.TanggalKunjungan.Date == d)
                    }).ToList(),

                // Status distribusi
                StatusDistribusi = kunjunganAll
                    .GroupBy(k => k.Status)
                    .Select(g => new ChartDataItem
                    {
                        Label = g.Key,
                        Value = g.Count()
                    }).ToList(),

                // Kunjungan terbaru
                KunjunganTerbaru = kunjunganAll
                    .OrderByDescending(k => k.TanggalKunjungan)
                    .ThenByDescending(k => k.Timestamp)
                    .Take(5)
                    .Select(k => new KunjunganRecentItem
                    {
                        Id = k.Id,
                        NamaPasien = k.NamaPasien,
                        Plant = k.Plant,
                        Departemen = k.Departemen,
                        Keluhan = k.Keluhan,
                        Status = k.Status,
                        Dokter = k.Dokter,
                        TanggalKunjungan = k.TanggalKunjungan,
                        Timestamp = k.Timestamp
                    }).ToList(),

                // Aktivitas terbaru
                AktivitasTerbaru = await _context.ActivityLog
                    .OrderByDescending(a => a.Timestamp)
                    .Take(5)
                    .Select(a => new ActivityRecentItem
                    {
                        Action = a.Action,
                        Description = a.Description,
                        Timestamp = a.Timestamp
                    }).ToListAsync()
            };

            return View(viewModel);
        }

        // API: Get drill-down data for each indicator card
        [HttpGet]
        public async Task<IActionResult> GetCardDetail(string filter)
        {
            var today = DateTime.Today;
            var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
            string title;

            switch (filter)
            {
                // ----------------------------------------------------------------
                case "total-pasien":
                    var pasienList = await _context.Pasien
                        .OrderByDescending(p => p.TanggalTerdaftar)
                        .ToListAsync();

                    var pasienRows = pasienList.Select(p => new
                    {
                        id             = p.Id,
                        namaPasien     = p.NamaPasien,
                        npk            = p.NPK,
                        plant          = p.Plant,
                        departemen     = p.Departemen,
                        jenisKelamin   = p.JenisKelamin == "L" ? "Laki-laki" : "Perempuan",
                        tanggalDaftar  = p.TanggalTerdaftar.ToString("dd MMM yyyy")
                    }).ToList();

                    return Json(new
                    {
                        title  = "Total Pasien Terdaftar",
                        type   = "pasien",
                        rows   = pasienRows,
                        charts = new
                        {
                            plantChart  = pasienList.GroupBy(p => p.Plant)
                                            .Select(g => new { label = g.Key, value = g.Count() }).ToList(),
                            deptChartP  = pasienList.GroupBy(p => p.Departemen)
                                            .Select(g => new { label = g.Key, value = g.Count() })
                                            .OrderByDescending(x => x.value).ToList(),
                            genderChart = new[]
                            {
                                new { label = "Laki-laki", value = pasienList.Count(p => p.JenisKelamin == "L") },
                                new { label = "Perempuan",  value = pasienList.Count(p => p.JenisKelamin == "P") }
                            }
                        }
                    });

                // ----------------------------------------------------------------
                case "total-kunjungan":
                    title = "Semua Data Kunjungan";
                    break;
                case "hari-ini":
                    title = "Kunjungan Hari Ini — " + today.ToString("dd MMMM yyyy");
                    break;
                case "bulan-ini":
                    title = "Kunjungan Bulan Ini — " + today.ToString("MMMM yyyy");
                    break;
                default:
                    return BadRequest();
            }

            // Fetch raw data dulu, baru filter & format di memori
            var allKunjungan = await _context.KunjunganKlinik
                .OrderByDescending(k => k.TanggalKunjungan)
                .ThenByDescending(k => k.Timestamp)
                .ToListAsync();

            var filtered = filter switch
            {
                "hari-ini"  => allKunjungan.Where(k => k.TanggalKunjungan.Date == today).ToList(),
                "bulan-ini" => allKunjungan.Where(k => k.TanggalKunjungan >= firstDayOfMonth).ToList(),
                _           => allKunjungan   // total-kunjungan
            };

            var rows = filtered.Select(k => new
            {
                id               = k.Id,
                namaPasien       = k.NamaPasien,
                npk              = k.NPK,
                plant            = k.Plant,
                departemen       = k.Departemen,
                jenisKelamin     = k.JenisKelamin,
                keluhan          = k.Keluhan,
                diagnosa         = k.Diagnosa,
                dokter           = k.Dokter,
                status           = k.Status,
                tanggalKunjungan = k.TanggalKunjungan.ToString("dd MMM yyyy")
            }).ToList();

            return Json(new
            {
                title,
                type   = "kunjungan",
                rows,
                charts = new
                {
                    statusChart   = rows.GroupBy(k => k.status)
                                    .Select(g => new { label = g.Key, value = g.Count() }).ToList(),
                    deptChart     = rows.GroupBy(k => k.departemen)
                                    .Select(g => new { label = g.Key, value = g.Count() })
                                    .OrderByDescending(x => x.value).ToList(),
                    plantChartK   = rows.GroupBy(k => k.plant)
                                    .Select(g => new { label = g.Key, value = g.Count() }).ToList(),
                    diagnosaChart = rows.GroupBy(k => k.diagnosa)
                                    .Select(g => new { label = g.Key, value = g.Count() })
                                    .OrderByDescending(x => x.value).Take(5).ToList()
                }
            });
        }
    }
}
