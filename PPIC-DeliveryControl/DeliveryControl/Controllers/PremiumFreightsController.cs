using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace DeliveryControl.Controllers
{
    public class PremiumFreightsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PremiumFreightsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: PremiumFreights/Dashboard
        public async Task<IActionResult> Dashboard(DateTime? startDate, DateTime? endDate, string? kategori, string? armada)
        {
            // Default filter tanggal: 30 hari terakhir s/d Hari Ini (berdasarkan CreatedDate / Tanggal Input)
            var end = endDate.HasValue ? endDate.Value.Date.AddDays(1).AddSeconds(-1) : DateTime.Today.Date.AddDays(1).AddSeconds(-1);
            var start = startDate.HasValue ? startDate.Value.Date : DateTime.Today.Date.AddDays(-30);

            // Convert legacy Draft entries to Submitted
            var legacyDrafts = await _context.PremiumFreights.Where(p => p.Status == "Draft" || p.Status == null).ToListAsync();
            if (legacyDrafts.Any())
            {
                foreach (var d in legacyDrafts)
                {
                    d.Status = "Submitted";
                }
                await _context.SaveChangesAsync();
            }

            var query = _context.PremiumFreights
                .Include(p => p.DeliverySchedule)
                    .ThenInclude(s => s!.Customer)
                .AsQueryable();

            // Filter strictly berdasarkan CreatedDate (Tanggal Input Premium Freight)
            query = query.Where(p => p.CreatedDate >= start && p.CreatedDate <= end);

            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(p => p.KategoriPengiriman == kategori);
            }

            if (!string.IsNullOrEmpty(armada))
            {
                query = query.Where(p => p.JenisArmada == armada);
            }

            var list = await query.OrderByDescending(p => p.CreatedDate).ToListAsync();

            // KPI Totals
            ViewBag.TotalCount = list.Count;
            ViewBag.ShortageCount = list.Count(p => string.Equals(p.KategoriPengiriman, "Shortage Delivery", StringComparison.OrdinalIgnoreCase));
            ViewBag.PickupMaterialCount = list.Count(p => string.Equals(p.KategoriPengiriman, "Pickup Material", StringComparison.OrdinalIgnoreCase));
            ViewBag.PickupSubconCount = list.Count(p => string.Equals(p.KategoriPengiriman, "Pickup Subcon", StringComparison.OrdinalIgnoreCase));
            ViewBag.RegulerTruckCount = list.Count(p => string.Equals(p.JenisArmada, "Reguler truck", StringComparison.OrdinalIgnoreCase));
            ViewBag.RitaseTruckCount = list.Count(p => string.Equals(p.JenisArmada, "Ritase truck", StringComparison.OrdinalIgnoreCase));

            // Build Daily Trend Series (per tanggal berdasarkan CreatedDate / Tanggal Input)
            var datesList = new List<string>();
            var shortageTrend = new List<int>();
            var pickupMaterialTrend = new List<int>();
            var pickupSubconTrend = new List<int>();

            var regTotalTrend = new List<int>();
            var ritTotalTrend = new List<int>();

            for (var dt = start.Date; dt <= end.Date; dt = dt.AddDays(1))
            {
                datesList.Add(dt.ToString("dd/MM"));
                var dayItems = list.Where(p => p.CreatedDate.Date == dt).ToList();

                shortageTrend.Add(dayItems.Count(p => string.Equals(p.KategoriPengiriman, "Shortage Delivery", StringComparison.OrdinalIgnoreCase)));
                pickupMaterialTrend.Add(dayItems.Count(p => string.Equals(p.KategoriPengiriman, "Pickup Material", StringComparison.OrdinalIgnoreCase)));
                pickupSubconTrend.Add(dayItems.Count(p => string.Equals(p.KategoriPengiriman, "Pickup Subcon", StringComparison.OrdinalIgnoreCase)));

                regTotalTrend.Add(dayItems.Count(p => string.Equals(p.JenisArmada, "Reguler truck", StringComparison.OrdinalIgnoreCase)));
                ritTotalTrend.Add(dayItems.Count(p => string.Equals(p.JenisArmada, "Ritase truck", StringComparison.OrdinalIgnoreCase)));
            }

            ViewBag.TrendDatesJson = System.Text.Json.JsonSerializer.Serialize(datesList);
            ViewBag.ShortageTrendJson = System.Text.Json.JsonSerializer.Serialize(shortageTrend);
            ViewBag.PickupMaterialTrendJson = System.Text.Json.JsonSerializer.Serialize(pickupMaterialTrend);
            ViewBag.PickupSubconTrendJson = System.Text.Json.JsonSerializer.Serialize(pickupSubconTrend);
            ViewBag.RegulerTotalTrendJson = System.Text.Json.JsonSerializer.Serialize(regTotalTrend);
            ViewBag.RitaseTotalTrendJson = System.Text.Json.JsonSerializer.Serialize(ritTotalTrend);

            // Filter states
            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");
            ViewBag.Kategori = kategori ?? "";
            ViewBag.Armada = armada ?? "";

            return View(list);
        }

        // GET: PremiumFreights
        public async Task<IActionResult> Index(string? search, DateTime? startDate, DateTime? endDate, string? kategori, string? armada)
        {
            var query = _context.PremiumFreights
                .Include(f => f.DeliverySchedule)
                    .ThenInclude(s => s!.Customer)
                .AsQueryable();

            // Filter Tanggal Input (CreatedDate)
            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                query = query.Where(f => f.CreatedDate >= start);
            }
            if (endDate.HasValue)
            {
                var end = endDate.Value.Date.AddDays(1).AddSeconds(-1);
                query = query.Where(f => f.CreatedDate <= end);
            }

            // Filter Kategori
            if (!string.IsNullOrEmpty(kategori))
            {
                query = query.Where(f => f.KategoriPengiriman == kategori);
            }

            // Filter Jenis Armada
            if (!string.IsNullOrEmpty(armada))
            {
                query = query.Where(f => f.JenisArmada == armada);
            }

            // Search Keyword: nama driver, shipping schedule, no truck, jenis armada, no referensi
            if (!string.IsNullOrEmpty(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(f =>
                    (f.FreightNumber != null && f.FreightNumber.ToLower().Contains(term)) ||
                    (f.DriverName != null && f.DriverName.ToLower().Contains(term)) ||
                    (f.VehicleNumber != null && f.VehicleNumber.ToLower().Contains(term)) ||
                    (f.CustomerName != null && f.CustomerName.ToLower().Contains(term)) ||
                    (f.Route != null && f.Route.ToLower().Contains(term)) ||
                    (f.JenisArmada != null && f.JenisArmada.ToLower().Contains(term)) ||
                    (f.KategoriPengiriman != null && f.KategoriPengiriman.ToLower().Contains(term)) ||
                    (f.DeliverySchedule != null && (
                        (f.DeliverySchedule.ScheduleNumber != null && f.DeliverySchedule.ScheduleNumber.ToLower().Contains(term)) ||
                        (f.DeliverySchedule.Route != null && f.DeliverySchedule.Route.ToLower().Contains(term)) ||
                        (f.DeliverySchedule.VehicleNumber != null && f.DeliverySchedule.VehicleNumber.ToLower().Contains(term)) ||
                        (f.DeliverySchedule.DriverName != null && f.DeliverySchedule.DriverName.ToLower().Contains(term)) ||
                        (f.DeliverySchedule.Customer != null && f.DeliverySchedule.Customer.CustomerName.ToLower().Contains(term))
                    ))
                );
            }

            var list = await query.OrderByDescending(f => f.CreatedDate).ToListAsync();

            ViewBag.Search = search ?? "";
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.Kategori = kategori ?? "";
            ViewBag.Armada = armada ?? "";

            return View(list);
        }

        // GET: PremiumFreights/Create
        public async Task<IActionResult> Create(int? scheduleId)
        {
            // Fetch 100 recent shipping schedules for dropdown
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .OrderByDescending(s => s.ScheduledDate)
                .ThenByDescending(s => s.ScheduleId)
                .Take(100)
                .Select(s => new {
                    s.ScheduleId,
                    s.ScheduleNumber,
                    CustomerName = s.Customer != null ? s.Customer.CustomerName : "-",
                    s.Route,
                    s.Cycle,
                    s.VehicleNumber,
                    s.DriverName,
                    ScheduledDateStr = s.ScheduledDate.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            ViewBag.Schedules = schedules;

            var model = new PremiumFreight();

            if (scheduleId.HasValue && scheduleId.Value > 0)
            {
                var sched = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == scheduleId.Value);
                if (sched != null)
                {
                    model.ScheduleId = sched.ScheduleId;
                    model.CustomerName = sched.Customer?.CustomerName;
                    model.Route = sched.Route;
                    model.Cycle = sched.Cycle;
                    model.VehicleNumber = sched.VehicleNumber;
                    model.DriverName = sched.DriverName;
                    model.ScheduledDate = sched.ScheduledDate;
                }
            }

            return View(model);
        }

        // GET: PremiumFreights/GetScheduleDetails/5
        [HttpGet]
        public async Task<IActionResult> GetScheduleDetails(int id)
        {
            var sched = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (sched == null)
            {
                return Json(new { success = false, message = "Schedule tidak ditemukan" });
            }

            return Json(new {
                success = true,
                scheduleId = sched.ScheduleId,
                scheduleNumber = sched.ScheduleNumber,
                customerName = sched.Customer?.CustomerName ?? "-",
                route = sched.Route ?? "-",
                cycle = sched.Cycle ?? "-",
                vehicleNumber = sched.VehicleNumber ?? "-",
                driverName = sched.DriverName ?? "-",
                scheduledDate = sched.ScheduledDate.ToString("yyyy-MM-dd")
            });
        }

        // POST: PremiumFreights/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PremiumFreight premiumFreight, string? inputMode)
        {
            if (inputMode == "manual" || (!premiumFreight.ScheduleId.HasValue || premiumFreight.ScheduleId <= 0))
            {
                premiumFreight.ScheduleId = null;
                if (string.IsNullOrWhiteSpace(premiumFreight.CustomerName))
                {
                    ModelState.AddModelError("CustomerName", "Nama Customer / Dock wajib diisi untuk opsi input manual.");
                }
            }

            if (ModelState.IsValid)
            {
                // Auto pull from schedule if selected in dropdown mode
                if (premiumFreight.ScheduleId.HasValue && premiumFreight.ScheduleId > 0)
                {
                    var sched = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == premiumFreight.ScheduleId.Value);
                    if (sched != null)
                    {
                        premiumFreight.CustomerName = string.IsNullOrEmpty(premiumFreight.CustomerName) ? sched.Customer?.CustomerName : premiumFreight.CustomerName;
                        premiumFreight.Route = string.IsNullOrEmpty(premiumFreight.Route) ? sched.Route : premiumFreight.Route;
                        premiumFreight.Cycle = string.IsNullOrEmpty(premiumFreight.Cycle) ? sched.Cycle : premiumFreight.Cycle;
                        premiumFreight.VehicleNumber = string.IsNullOrEmpty(premiumFreight.VehicleNumber) ? sched.VehicleNumber : premiumFreight.VehicleNumber;
                        premiumFreight.DriverName = string.IsNullOrEmpty(premiumFreight.DriverName) ? sched.DriverName : premiumFreight.DriverName;
                        premiumFreight.ScheduledDate = premiumFreight.ScheduledDate ?? sched.ScheduledDate;
                    }
                }

                premiumFreight.CreatedDate = DateTime.Now;
                premiumFreight.CreatedBy = HttpContext.Session.GetString("Username") ?? "User";
                premiumFreight.FreightNumber = "PF-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                premiumFreight.Status = "Submitted";

                _context.Add(premiumFreight);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Data Premium Freight berhasil disimpan dan diintegrasikan dengan Dashboard Shipping!";
                return RedirectToAction(nameof(Dashboard));
            }

            // Reload dropdown if invalid
            ViewBag.Schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .OrderByDescending(s => s.ScheduledDate)
                .Take(100)
                .Select(s => new {
                    s.ScheduleId,
                    s.ScheduleNumber,
                    CustomerName = s.Customer != null ? s.Customer.CustomerName : "-",
                    s.Route,
                    s.Cycle,
                    s.VehicleNumber,
                    s.DriverName,
                    ScheduledDateStr = s.ScheduledDate.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            return View(premiumFreight);
        }

        // POST: PremiumFreights/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.PremiumFreights.FindAsync(id);
            if (item != null)
            {
                _context.PremiumFreights.Remove(item);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Data Premium Freight berhasil dihapus.";
            }
            return RedirectToAction(nameof(Dashboard));
        }
    }
}
