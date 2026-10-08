using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Filters;
using System.Globalization;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller untuk Activity Logs
    /// </summary>
    [AuthorizeAdmin]
    public class ActivityLogsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ActivityLogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: ActivityLogs - List semua activity logs dengan filter (tanpa wajib filter tanggal)
        public async Task<IActionResult> Index(
            DateTime? startDate, 
            DateTime? endDate, 
            string? module, 
            string? action,
            string? performedBy,
            int pageNumber = 1,
            int pageSize = 20)
        {
            // Tanggal hanya untuk tampilan & export (tidak membatasi query utama)
            var start = startDate ?? DateTime.Today;
            var end = endDate ?? DateTime.Today.AddDays(1).AddSeconds(-1);

            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd") ?? "";
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd") ?? "";
            ViewData["SelectedModule"] = module;
            ViewData["SelectedAction"] = action;
            ViewData["SelectedPerformedBy"] = performedBy;

            // Query logs (TANPA filter tanggal, modul, aksi, atau user)
            // Selalu tampilkan semua activity logs, urut dari terbaru
            var query = _context.ActivityLogs.AsQueryable();

            // Get distinct values for filters
            var modules = await _context.ActivityLogs
                .Select(l => l.Module)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync();

            var actions = await _context.ActivityLogs
                .Select(l => l.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToListAsync();

            var users = await _context.ActivityLogs
                .Select(l => l.PerformedBy)
                .Distinct()
                .OrderBy(u => u)
                .ToListAsync();

            ViewBag.Modules = modules;
            ViewBag.Actions = actions;
            ViewBag.Users = users;

            // Count total records
            var totalRecords = await query.CountAsync();
            ViewBag.TotalRecords = totalRecords;

            // Pagination
            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .ThenByDescending(l => l.LogId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Pagination info
            ViewBag.CurrentPage = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            ViewBag.TotalItems = totalRecords;
            ViewBag.RouteData = new Dictionary<string, string> { 
                { "startDate", startDate?.ToString("yyyy-MM-dd") },
                { "endDate", endDate?.ToString("yyyy-MM-dd") },
                { "module", module },
                { "action", action },
                { "performedBy", performedBy }
            };

            return View(logs);
        }

        // GET: ActivityLogs/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var log = await _context.ActivityLogs
                .FirstOrDefaultAsync(m => m.LogId == id);

            if (log == null)
            {
                return NotFound();
            }

            return View(log);
        }

        // POST: ActivityLogs/ClearOldLogs - Hapus log lama (lebih dari X hari)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearOldLogs(int daysToKeep = 90)
        {
            try
            {
                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var logsToDelete = await _context.ActivityLogs
                    .Where(l => l.Timestamp < cutoffDate)
                    .ToListAsync();

                if (logsToDelete.Any())
                {
                    _context.ActivityLogs.RemoveRange(logsToDelete);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = $"✅ Berhasil menghapus {logsToDelete.Count} log yang lebih lama dari {daysToKeep} hari.";
                }
                else
                {
                    TempData["InfoMessage"] = $"ℹ️ Tidak ada log yang lebih lama dari {daysToKeep} hari.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: ActivityLogs/Export - Export logs to CSV
        public async Task<IActionResult> Export(DateTime? startDate, DateTime? endDate, string? module, string? action, string? performedBy)
        {
            var start = startDate ?? DateTime.Today;
            var end = endDate ?? DateTime.Today.AddDays(1).AddSeconds(-1);

            var query = _context.ActivityLogs
                .Where(l => l.Timestamp >= start && l.Timestamp <= end);

            if (!string.IsNullOrWhiteSpace(module))
                query = query.Where(l => l.Module == module);

            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(l => l.Action == action);

            if (!string.IsNullOrWhiteSpace(performedBy))
                query = query.Where(l => l.PerformedBy.Contains(performedBy));

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            // Generate CSV
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Timestamp,Module,Action,Entity,Description,Performed By,IP Address");

            foreach (var log in logs)
            {
                csv.AppendLine($"\"{log.Timestamp:yyyy-MM-dd HH:mm}\",\"{log.Module}\",\"{log.Action}\",\"{log.EntityName}\",\"{log.Description}\",\"{log.PerformedBy}\",\"{log.IpAddress}\"");
            }

            var fileName = $"ActivityLogs_{start:yyyyMMdd}_{end:yyyyMMdd}.csv";
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }

        /// <summary>
        /// API endpoint untuk mendapatkan data activity logs terbaru (untuk real-time update)
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetActivityLogsData(int pageNumber = 1, int pageSize = 50)
        {
            // Always get fresh data from database (no caching)
            var query = _context.ActivityLogs.AsNoTracking().AsQueryable();

            // Count total records
            var totalRecords = await query.CountAsync();

            // Pagination
            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .ThenByDescending(l => l.LogId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Pagination info
            ViewBag.CurrentPage = pageNumber;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            ViewBag.TotalItems = totalRecords;

            return PartialView("_ActivityLogsTablePartial", logs);
        }

        /// <summary>
        /// API endpoint untuk mendapatkan total records (untuk update statistik)
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetTotalRecords()
        {
            var totalRecords = await _context.ActivityLogs.AsNoTracking().CountAsync();
            return Json(new { totalRecords });
        }
    }
}

