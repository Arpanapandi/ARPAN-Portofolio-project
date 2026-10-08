using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.ViewModels;
using dashboardKlinik.Services;
using ClosedXML.Excel;
using System.Globalization;

namespace dashboardKlinik.Controllers
{
    [Authorize]
    public class LaporanController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;

        public LaporanController(ApplicationDbContext context, ActivityLogService activityLog)
        {
            _context = context;
            _activityLog = activityLog;
        }

        // GET: Laporan
        public async Task<IActionResult> Index(int? bulan, int? tahun)
        {
            if (User.IsInRole("Guest")) return RedirectToAction("Index", "Home");

            var today = DateTime.Today;
            int selectedBulan = bulan ?? today.Month;
            int selectedTahun = tahun ?? today.Year;

            var startDate = new DateTime(selectedTahun, selectedBulan, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var kunjungan = await _context.KunjunganKlinik
                .Where(k => k.TanggalKunjungan >= startDate && k.TanggalKunjungan <= endDate)
                .OrderByDescending(k => k.TanggalKunjungan)
                .ToListAsync();

            var viewModel = new LaporanViewModel
            {
                Bulan = selectedBulan,
                Tahun = selectedTahun,
                NamaBulan = new DateTime(selectedTahun, selectedBulan, 1).ToString("MMMM yyyy", new CultureInfo("id-ID")),
                TotalKunjungan = kunjungan.Count,
                TotalPasienUnik = kunjungan.Select(k => k.NPK).Distinct().Count(),
                RawatJalan = kunjungan.Count(k => k.Status == "Rawat Jalan"),
                Perawatan = kunjungan.Count(k => k.Status == "Perawatan"),
                Dirujuk = kunjungan.Count(k => k.Status == "Dirujuk"),

                KunjunganPerDepartemen = kunjungan
                    .GroupBy(k => k.Departemen)
                    .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                    .OrderByDescending(x => x.Value).ToList(),

                DiagnosaPopuler = kunjungan
                    .GroupBy(k => k.Diagnosa)
                    .Select(g => new ChartDataItem { Label = g.Key, Value = g.Count() })
                    .OrderByDescending(x => x.Value).Take(10).ToList(),

                KunjunganPerHari = Enumerable.Range(1, DateTime.DaysInMonth(selectedTahun, selectedBulan))
                    .Select(d => new ChartDataItem
                    {
                        Label = d.ToString(),
                        Value = kunjungan.Count(k => k.TanggalKunjungan.Day == d)
                    }).ToList(),

                DataKunjungan = kunjungan
            };

            return View(viewModel);
        }

        // GET: Laporan/ExportExcel
        public async Task<IActionResult> ExportExcel(int? bulan, int? tahun)
        {
            var today = DateTime.Today;
            int selectedBulan = bulan ?? today.Month;
            int selectedTahun = tahun ?? today.Year;

            var startDate = new DateTime(selectedTahun, selectedBulan, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);
            var namaBulan = startDate.ToString("MMMM yyyy", new CultureInfo("id-ID"));

            var kunjungan = await _context.KunjunganKlinik
                .Where(k => k.TanggalKunjungan >= startDate && k.TanggalKunjungan <= endDate)
                .OrderBy(k => k.TanggalKunjungan)
                .ToListAsync();

            using var workbook = new XLWorkbook();

            // Sheet 1: Data Kunjungan
            var ws1 = workbook.Worksheets.Add("Data Kunjungan");

            // Title
            ws1.Cell(1, 1).Value = $"Laporan Kunjungan Klinik - {namaBulan}";
            ws1.Range(1, 1, 1, 11).Merge();
            ws1.Cell(1, 1).Style.Font.Bold = true;
            ws1.Cell(1, 1).Style.Font.FontSize = 14;
            ws1.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws1.Cell(2, 1).Value = $"Dicetak pada: {DateTime.Now:dd/MM/yyyy HH:mm}";
            ws1.Range(2, 1, 2, 11).Merge();
            ws1.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Headers
            var headers = new[] { "No", "Tanggal", "Nama Pasien", "NPK", "Plant", "Departemen", "Jenis Kelamin", "Keluhan", "Diagnosa", "Dokter", "Status" };
            for (int i = 0; i < headers.Length; i++)
            {
                ws1.Cell(4, i + 1).Value = headers[i];
                ws1.Cell(4, i + 1).Style.Font.Bold = true;
                ws1.Cell(4, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");
                ws1.Cell(4, i + 1).Style.Font.FontColor = XLColor.White;
                ws1.Cell(4, i + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws1.Cell(4, i + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            // Data
            for (int i = 0; i < kunjungan.Count; i++)
            {
                var k = kunjungan[i];
                int row = i + 5;
                ws1.Cell(row, 1).Value = i + 1;
                ws1.Cell(row, 2).Value = k.TanggalKunjungan.ToString("dd/MM/yyyy");
                ws1.Cell(row, 3).Value = k.NamaPasien;
                ws1.Cell(row, 4).Value = k.NPK;
                ws1.Cell(row, 5).Value = k.Plant;
                ws1.Cell(row, 6).Value = k.Departemen;
                ws1.Cell(row, 7).Value = k.JenisKelamin == "L" ? "Laki-laki" : "Perempuan";
                ws1.Cell(row, 8).Value = k.Keluhan;
                ws1.Cell(row, 9).Value = k.Diagnosa;
                ws1.Cell(row, 10).Value = k.Dokter;
                ws1.Cell(row, 11).Value = k.Status;

                for (int j = 1; j <= 11; j++)
                {
                    ws1.Cell(row, j).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                if (i % 2 == 1)
                {
                    ws1.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f9fa");
                }
            }

            ws1.Columns().AdjustToContents();

            // Sheet 2: Ringkasan
            var ws2 = workbook.Worksheets.Add("Ringkasan");
            ws2.Cell(1, 1).Value = $"Ringkasan Laporan - {namaBulan}";
            ws2.Range(1, 1, 1, 3).Merge();
            ws2.Cell(1, 1).Style.Font.Bold = true;
            ws2.Cell(1, 1).Style.Font.FontSize = 14;

            ws2.Cell(3, 1).Value = "Metrik";
            ws2.Cell(3, 2).Value = "Jumlah";
            ws2.Cell(3, 1).Style.Font.Bold = true;
            ws2.Cell(3, 2).Style.Font.Bold = true;
            ws2.Cell(3, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");
            ws2.Cell(3, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#1a73e8");
            ws2.Cell(3, 1).Style.Font.FontColor = XLColor.White;
            ws2.Cell(3, 2).Style.Font.FontColor = XLColor.White;

            ws2.Cell(4, 1).Value = "Total Kunjungan";
            ws2.Cell(4, 2).Value = kunjungan.Count;
            ws2.Cell(5, 1).Value = "Pasien Unik";
            ws2.Cell(5, 2).Value = kunjungan.Select(k => k.NPK).Distinct().Count();
            ws2.Cell(6, 1).Value = "Rawat Jalan";
            ws2.Cell(6, 2).Value = kunjungan.Count(k => k.Status == "Rawat Jalan");
            ws2.Cell(7, 1).Value = "Perawatan";
            ws2.Cell(7, 2).Value = kunjungan.Count(k => k.Status == "Perawatan");
            ws2.Cell(8, 1).Value = "Dirujuk";
            ws2.Cell(8, 2).Value = kunjungan.Count(k => k.Status == "Dirujuk");

            // Kunjungan per departemen
            ws2.Cell(10, 1).Value = "Kunjungan Per Departemen";
            ws2.Cell(10, 1).Style.Font.Bold = true;
            ws2.Cell(10, 1).Style.Font.FontSize = 12;

            ws2.Cell(11, 1).Value = "Departemen";
            ws2.Cell(11, 2).Value = "Jumlah";
            ws2.Cell(11, 1).Style.Font.Bold = true;
            ws2.Cell(11, 2).Style.Font.Bold = true;

            var deptGroups = kunjungan.GroupBy(k => k.Departemen).OrderByDescending(g => g.Count()).ToList();
            for (int i = 0; i < deptGroups.Count; i++)
            {
                ws2.Cell(12 + i, 1).Value = deptGroups[i].Key;
                ws2.Cell(12 + i, 2).Value = deptGroups[i].Count();
            }

            ws2.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            await _activityLog.LogAsync("EXPORT", $"Excel report exported for {namaBulan}", "Laporan");

            var fileName = $"Laporan_Klinik_{namaBulan.Replace(" ", "_")}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }
}
