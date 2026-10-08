using Microsoft.AspNetCore.Mvc;
using dashboardKlinik.Data;
using dashboardKlinik.Models;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace dashboardKlinik.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class KunjunganApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<KlinikHub> _hubContext;
        private readonly ILogger<KunjunganApiController> _logger;

        public KunjunganApiController(
            ApplicationDbContext context, 
            IHubContext<KlinikHub> hubContext,
            ILogger<KunjunganApiController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitKunjungan([FromBody] KunjunganInputModel input)
        {
            try
            {
                if (input == null) return BadRequest("Data tidak boleh kosong");

                // Find or create Pasien
                var pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.NPK == input.NPK);
                if (pasien == null)
                {
                    pasien = new Pasien
                    {
                        NamaPasien = input.NamaPasien,
                        NPK = input.NPK,
                        Plant = input.Plant,
                        Departemen = input.Departemen,
                        JenisKelamin = input.JenisKelamin,
                        TanggalTerdaftar = DateTime.Now
                    };
                    _context.Pasien.Add(pasien);
                    // No need to save here, let it save with Kunjungan
                }

                var status = input.Status;
                if (status == "Rujuk Rumah Sakit") status = "Dirujuk";

                var kunjungan = new KunjunganKlinik
                {
                    Timestamp = DateTime.Now,
                    TanggalKunjungan = input.TanggalKunjungan,
                    NamaPasien = input.NamaPasien,
                    NPK = input.NPK,
                    Plant = input.Plant,
                    Departemen = input.Departemen,
                    JenisKelamin = input.JenisKelamin,
                    Keluhan = input.Keluhan,
                    Diagnosa = input.Diagnosa,
                    Dokter = input.Dokter,
                    Status = status,
                    Pasien = pasien
                };

                _context.KunjunganKlinik.Add(kunjungan);
                
                _context.ActivityLog.Add(new ActivityLog
                {
                    Action = "API_SUBMIT",
                    Description = $"Kunjungan baru via Google Form: {input.NamaPasien} ({input.NPK})",
                    EntityType = "KunjunganKlinik",
                    Timestamp = DateTime.Now
                });

                await _context.SaveChangesAsync();

                // Notify SignalR clients with updated stats
                await NotifyClientsAsync();

                return Ok(new { success = true, message = "Data berhasil disimpan ke database", id = kunjungan.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting kunjungan via API");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        private async Task NotifyClientsAsync()
        {
            try
            {
                var today = DateTime.Today;
                var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);
                var kunjunganAll = await _context.KunjunganKlinik.AsNoTracking().ToListAsync();

                var stats = new
                {
                    totalPasien = await _context.Pasien.CountAsync(),
                    totalKunjungan = kunjunganAll.Count,
                    kunjunganHariIni = kunjunganAll.Count(k => k.TanggalKunjungan.Date == today),
                    kunjunganBulanIni = kunjunganAll.Count(k => k.TanggalKunjungan >= firstDayOfMonth),
                    rawatJalan = kunjunganAll.Count(k => k.Status == "Rawat Jalan"),
                    perawatan = kunjunganAll.Count(k => k.Status == "Perawatan"),
                    dirujuk = kunjunganAll.Count(k => k.Status == "Dirujuk")
                };

                await _hubContext.Clients.All.SendAsync("NewDataReceived", new
                {
                    message = "Data baru diterima dari Google Form!",
                    timestamp = DateTime.Now,
                    stats = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting SignalR update");
            }
        }
    }

    public class KunjunganInputModel
    {
        public DateTime TanggalKunjungan { get; set; }
        public string NamaPasien { get; set; } = string.Empty;
        public string NPK { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public string JenisKelamin { get; set; } = string.Empty;
        public string Keluhan { get; set; } = string.Empty;
        public string Diagnosa { get; set; } = string.Empty;
        public string Dokter { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
