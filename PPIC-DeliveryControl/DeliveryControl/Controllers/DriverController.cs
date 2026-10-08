using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Hubs;
using DeliveryControl.Services;
using System.Globalization;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller khusus untuk Driver - konfirmasi kedatangan dan keberangkatan
    /// </summary>
    [Authorize]
    [AuthorizeRoles("Admin", "Super Admin", "Preparation", "User", "Pulling", "Driver", "Leader")]
    public class DriverController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly ActivityLogService _logService;

        public DriverController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, ActivityLogService logService)
        {
            _context = context;
            _hubContext = hubContext;
            _logService = logService;
        }

        private async Task<HashSet<string>> GetAllowedDockCodesAsync()
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            if (sessionRole == "Admin" || sessionRole == "Super Admin") return new HashSet<string>();

            if (int.TryParse(HttpContext.Session.GetString("UserId"), out int userId))
            {
                var dockData = await _context.UserDockAccesses
                    .Where(uda => uda.UserId == userId)
                    .Select(uda => new { uda.Dock.DockCode, uda.Dock.DockName })
                    .ToListAsync();

                return dockData
                    .SelectMany(d => new[] { d.DockCode ?? "", d.DockName ?? "" })
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .ToHashSet();
            }

            return new HashSet<string>();
        }

        // GET/POST: Driver/CloseSchedules23To26 - Auto close carry-over schedules from 23 to 26 Sep 2026
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet, HttpPost]
        public async Task<IActionResult> CloseSchedules23To26()
        {
            var maxDate = new DateTime(2026, 09, 26, 23, 59, 59);

            var schedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date <= maxDate.Date && s.Status != "Completed")
                .ToListAsync();

            // Ambil juga semua schedule 23-26 tanpa memandang status saat ini
            var targetSchedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date >= new DateTime(2026, 09, 23).Date && s.ScheduledDate.Date <= maxDate.Date)
                .ToListAsync();

            var allToProcess = schedules.Union(targetSchedules).ToList();

            int updatedCount = 0;
            foreach (var s in allToProcess)
            {
                var dockTime = s.ActualEnterDockTime ?? s.EnterDockTime ?? s.ScheduledDate.Date.AddHours(4);
                s.ActualEnterDockTime = dockTime;

                var pickupTarget = s.PickupTime ?? dockTime.AddMinutes(30);
                if (pickupTarget < dockTime)
                {
                    pickupTarget = dockTime.AddMinutes(30);
                }

                s.ActualStartTime = dockTime.AddMinutes(10);
                s.ActualPickupTime = pickupTarget;
                s.ActualEndTime = pickupTarget;

                s.Status = "Completed";
                s.DriverStatus = "Completed";
                s.UpdatedDate = DateTime.Now;
                s.UpdatedBy = "System (Auto Close 23-26)";

                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        di.ActualQuantity = di.Quantity;
                        di.UpdatedDate = DateTime.Now;
                    }
                }
                updatedCount++;
            }

            await _context.SaveChangesAsync();

            // Broadcast SignalR update
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                action = "update",
                message = $"Schedules 23-26 Sept closed successfully ({updatedCount} schedules).",
                timestamp = DateTime.Now
            });

            return Json(new { success = true, count = updatedCount, message = $"Berhasil mengubah {updatedCount} jadwal delivery (23-26 Sep 2026) menjadi Closed Delivery." });
        }

        // GET: Driver - Daftar schedule untuk driver (berbasis PICKUP DATE seperti Dashboard)
        public async Task<IActionResult> Index(DateTime? selectedDate, int? customerId, string? status, string? route, string? cycle)
        {
            // TEMPORARY: Cleanup for presentation (Trigger with ?status=Cleanup)
            if (status == "Cleanup")
            {
                var completedToCleanup = await _context.DeliverySchedules
                    .Where(s => s.Status == "Completed" || s.ActualEndTime.HasValue)
                    .ToListAsync();
                
                foreach (var sch in completedToCleanup)
                {
                    var items = await _context.DeliveryItems.Where(i => i.ScheduleId == sch.ScheduleId).ToListAsync();
                    _context.DeliveryItems.RemoveRange(items);
                    
                    var preps = await _context.PreparationRecords.Where(p => p.ScheduleId == sch.ScheduleId).ToListAsync();
                    _context.PreparationRecords.RemoveRange(preps);
                }
                
                _context.DeliverySchedules.RemoveRange(completedToCleanup);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            var scheduleDate = selectedDate ?? DateTime.Today;
            var windowStart = scheduleDate.AddDays(-7); // Mundur 7 hari untuk cover delay/cross-day
            var windowEnd = scheduleDate.AddDays(3);    // Maju 3 hari untuk planning ke depan
            var now = DateTime.Now;
            
            // Normalize filter - trim dan pastikan tidak null
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
            var normalizedRoute = string.IsNullOrWhiteSpace(route) ? string.Empty : route!.Trim();
            var normalizedCycle = string.IsNullOrWhiteSpace(cycle) ? string.Empty : cycle!.Trim();
            
            ViewData["SelectedDate"] = scheduleDate.ToString("yyyy-MM-dd");
            ViewData["SelectedCustomerId"] = customerId;
            ViewData["SelectedStatus"] = normalizedStatus;
            ViewData["SelectedRoute"] = normalizedRoute;
            ViewData["SelectedCycle"] = normalizedCycle;
            ViewData["DayName"] = scheduleDate.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));

            // Ambil kandidat schedule dari window waktu yang lebih luas, plus carry-over yang belum selesai
            var rawSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .Where(s => (s.ScheduledDate.Date >= windowStart.Date && s.ScheduledDate.Date <= windowEnd.Date)
                         || (s.ScheduledDate.Date < windowStart.Date && s.Status != "Completed"))
                .ToListAsync();

            // Terapkan filter dock â€” hanya untuk Driver yang punya dock assignment
            var allowedDockCodes = await GetAllowedDockCodesAsync();
            if (allowedDockCodes.Any())
            {
                rawSchedules = rawSchedules
                    .Where(s =>
                        allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                        (s.Customer != null && (
                            allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                    .ToList();
            }

            // Filter berdasarkan PICKUP DATE (atau ScheduledDate jika tidak ada PickupTime)
            // Untuk default (tanpa filter), tampilkan hari ini DAN besok agar jadwal cross-day terlihat
            List<DeliverySchedule> schedulesForSelectedDate;
            
            if (selectedDate == null)
            {
                // Portal Driver: tampilkan jadwal HANYA setelah Portal Preparation konfirmasi ENTER DOCK
                // ATAU jika item sudah selesai discan (Kanban Prepared 100%)
                schedulesForSelectedDate = rawSchedules
                    .Where(s => s.Status != "Cancelled")
                    .Where(s => s.ActualEnterDockTime.HasValue
                             || s.Status == "Completed"
                             || (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                    .ToList();
                
                ViewData["SelectedDate"] = null;
            }
            else
            {
                // Filter eksplisit: Tampilkan jadwal dengan PickupTime pada tanggal yang dipilih
                // SERTA tampilkan jadwal carry-over dari masa lalu yang belum selesai
                schedulesForSelectedDate = rawSchedules
                    .Where(s => {
                        var pickupDate = s.PickupTime.HasValue ? s.PickupTime.Value.Date : s.ScheduledDate.Date;
                        return pickupDate == scheduleDate.Date || (pickupDate < scheduleDate.Date && s.Status != "Completed");
                    })
                    .Where(s => s.Status != "Cancelled")
                    .Where(s => s.ActualEnterDockTime.HasValue
                             || s.Status == "Completed"
                             || (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                    .ToList();
                
                ViewData["SelectedDate"] = scheduleDate.ToString("yyyy-MM-dd");
            }

            // Filter by customer jika ada
            if (customerId.HasValue && customerId.Value > 0)
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => s.CustomerId == customerId.Value)
                    .ToList();
            }

            // Filter by status jika ada
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => (s.DriverStatus ?? s.Status) == normalizedStatus)
                    .ToList();
            }

            // Filter by route jika ada
            if (!string.IsNullOrWhiteSpace(normalizedRoute))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => string.Equals(s.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Filter by cycle jika ada
            if (!string.IsNullOrWhiteSpace(normalizedCycle))
            {
                schedulesForSelectedDate = schedulesForSelectedDate
                    .Where(s => string.Equals(s.Cycle, normalizedCycle, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Data customer untuk dropdown: SEMUA CUSTOMER AKTIF
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();
            
            ViewBag.Customers = customers;

            // Data route & cycle yang tersedia untuk tanggal terpilih (berdasarkan schedules yang sudah ter-filter by date)
            var availableRoutes = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Select(s => s.Route)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .OrderBy(r => r)
                .ToList();

            var availableCycles = rawSchedules
                .Where(s => s.PickupTime.HasValue && s.PickupTime.Value.Date == scheduleDate.Date)
                .Select(s => s.Cycle)
                .Where(cy => !string.IsNullOrWhiteSpace(cy))
                .Distinct()
                .OrderBy(cy => cy)
                .ToList();

            ViewBag.Routes = availableRoutes;
            ViewBag.Cycles = availableCycles;

            // Grouping: Dock (Area) + Cycle + Route + ScheduledDate â€” manifest berbeda tapi trip sama = 1 card
            // ScheduledDate HARUS ikut agar trip hari ini tidak digabung dengan trip carry-over hari lama
            // IsCompleted digunakan agar trip aktif tidak digabung dengan trip lama yang sudah selesai
            var groupedSchedules = schedulesForSelectedDate
                .GroupBy(s => new {
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Area  = (s.Area  ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date,
                    IsCompleted = s.ActualEndTime.HasValue
                })
                .Select(g => {
                    var first = g.First();
                    var sortedGroup = g.OrderBy(x => x.ScheduleNumber).ToList();
                    
                    // Combine Customer Names
                    var uniqueCustomers = g.Select(x => x.Customer?.CustomerName ?? "-").Distinct().ToList();
                    var customerDisplay = uniqueCustomers.Count > 1 
                        ? string.Join(", ", uniqueCustomers) 
                        : (uniqueCustomers.FirstOrDefault() ?? "-");

                    return new DeliveryControl.Models.ViewModels.DriverTripViewModel
                    {
                        RepresentativeScheduleId = first.ScheduleId,
                        CustomerName = customerDisplay,
                        Cycle = first.Cycle ?? "",
                        Route = first.Route ?? "",
                        Area = first.Area ?? "",
                        PickupTime = first.PickupTime,
                        ETD = first.ETD,
                        ActualStartTime = first.ActualStartTime, // Assumes synchronized updates
                        ActualEndTime = first.ActualEndTime,     // Assumes synchronized updates
                        DriverStatus = (first.DriverStatus ?? first.Status) ?? "Scheduled",
                        OverallStatus = first.Status ?? "Scheduled",
                        ScheduledDate = first.ScheduledDate,
                        Schedules = sortedGroup
                    };
                })
                .OrderBy(vm => vm.PickupTime ?? vm.ETD ?? DateTime.MaxValue)
                .ToList();

            // GUARD: Portal driver menampilkan grup jika:
            // 1. Sudah departed (untuk riwayat), ATAU
            // 2. Sudah konfirmasi masuk dock (ActualEnterDockTime), ATAU
            // 3. Semua kanbannya sudah terpenuhi (100% prepared).
            groupedSchedules = groupedSchedules
                .Where(vm =>
                    vm.HasDeparted ||
                    vm.Schedules.Any(s => s.ActualEnterDockTime.HasValue) ||
                    vm.Schedules.All(s =>
                        !s.DeliveryItems.Any() ||
                        s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                .ToList();

            // Split into "Need Action" and "Completed"
            var needAction = groupedSchedules
                .Where(vm => !vm.HasDeparted)
                .OrderByDescending(vm => vm.Schedules.Any(s => s.ActualEnterDockTime.HasValue)) // Priority 1: Enter Dock
                .ThenBy(vm => {
                    if (!vm.HasArrived) return 0; // Scheduled
                    return 1; // In Progress (Arrived but not Departed)
                })
                .ThenBy(vm => vm.PickupTime ?? DateTime.MaxValue)
                .ToList();

            var completed = groupedSchedules
                .Where(vm => vm.HasDeparted)
                .OrderByDescending(vm => vm.ActualEndTime)
                .ToList();

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;

            // Return the View with the ViewModel list (Grouped) instead of raw schedules
            return View(groupedSchedules);
        }

        // GET: Driver/Arrival/5 - Halaman konfirmasi kedatangan
        public async Task<IActionResult> Arrival(int? id, string? returnDate)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnDate"] = returnDate;

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Cek apakah sudah ada arrival time
            if (schedule.ActualStartTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi kedatangan pada {schedule.ActualStartTime.Value:dd/MM/yyyy HH:mm}";
            }

            return View(schedule);
        }

        // Helper to find group
        private async Task<List<DeliverySchedule>> GetSchedulesInGroup(DeliverySchedule baseSchedule)
        {
            var date = baseSchedule.PickupTime?.Date ?? baseSchedule.ScheduledDate.Date;
            var scheduleDate = baseSchedule.ScheduledDate.Date;
            
            // Normalize for comparison
            string route = (baseSchedule.Route ?? "").Trim().ToUpper();
            string cycle = (baseSchedule.Cycle ?? "").Trim().ToUpper();
            string area = (baseSchedule.Area ?? "").Trim().ToUpper();
            
            // Note: EF Core translation for case-insensitive/trim might differ. 
            // Since we are using SQLite/SQLServer, usually case-insensitive by default or simple comparison.
            // But to be safe and match the GroupBy logic in Index:
            
            // Hanya ambil schedule yang sudah "enter dock" atau Completed atau sudah FULLY PREPARED
            // (misal masih "In Progress" tanpa ActualEnterDockTime tapi barang sudah siap)
            var candidates = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date == scheduleDate && s.Status != "Cancelled")
                .ToListAsync();
                
            var group = candidates
                .Where(s => 
                    (s.Route ?? "").Trim().ToUpper() == route &&
                    (s.Cycle ?? "").Trim().ToUpper() == cycle &&
                    (s.Area ?? "").Trim().ToUpper() == area
                )
                .ToList();

            // Jaminan: baseSchedule selalu ada di dalam list group
            if (!group.Any(s => s.ScheduleId == baseSchedule.ScheduleId))
            {
                group.Add(baseSchedule);
            }

            return group;
        }

        // POST: Driver/Arrival/5 - Konfirmasi kedatangan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Arrival(int id, DateTime arrivalTime, string? notes, string? returnDate)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            try
            {
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);
                if (!groupSchedules.Any(s => s.ScheduleId == baseSchedule.ScheduleId))
                {
                    groupSchedules.Add(baseSchedule);
                }
                
                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualStartTime = arrivalTime;
                    schedule.ActualPickupTime = arrivalTime; // Record pickup time at arrival as requested
                    schedule.DriverStatus = "In Progress";
                    schedule.UpdatedDate = DateTime.Now;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                    
                    // Tambahkan notes jika ada
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                            ? $"[Arrival] {notes}" 
                            : schedule.Notes + $"\n[Arrival] {notes}";
                    }
                }

                await _context.SaveChangesAsync();

                // Log activity (Group log?)
                await _logService.LogConfirm(
                    "Driver",
                    $"Group-{baseSchedule.Cycle}", // Log Cycle/Group ID?
                    baseSchedule.ScheduleId,
                    $"Konfirmasi kedatangan GROUP ({groupSchedules.Count} Manifest) untuk {baseSchedule.Customer?.CustomerName} pada {arrivalTime:HH:mm}",
                    User.Identity?.Name ?? "Driver"
                );

                // Broadcast update via SignalR
                foreach(var s in groupSchedules) {
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = s.ScheduleNumber,
                        action = "arrival",
                        message = $"Driver tiba di {s.Customer?.CustomerName}",
                        timestamp = DateTime.Now
                    });
                }

                TempData["SuccessMessage"] = $"✅ Kedatangan berhasil dikonfirmasi untuk {groupSchedules.Count} Manifest pada {arrivalTime:HH:mm}!";
                
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, message = $"✅ Kedatangan berhasil dikonfirmasi ({arrivalTime:HH:mm})" });
                }

                var referer = Request.Headers["Referer"].ToString();
                if (!string.IsNullOrEmpty(referer) && !referer.Contains("/Driver/Arrival", StringComparison.OrdinalIgnoreCase) && !referer.Contains("/Driver/Departure", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(referer);
                }
                if (!string.IsNullOrEmpty(returnDate))
                {
                    return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                }
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = ex.Message });
                }
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(Arrival), new { id, returnDate });
            }
        }

        // GET: Driver/Departure/5 - Halaman konfirmasi keberangkatan
        public async Task<IActionResult> Departure(int? id, string? returnDate)
        {
            if (id == null)
            {
                return NotFound();
            }

            ViewData["ReturnDate"] = returnDate;
        
            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);
        
            if (schedule == null)
            {
                return NotFound();
            }
        
            // Validasi: Harus sudah ada arrival time
            if (!schedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "⚠️ Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Arrival), new { id });
            }
        
            // Cek apakah sudah ada departure time
            if (schedule.ActualEndTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi keberangkatan pada {schedule.ActualEndTime.Value:dd/MM/yyyy HH:mm}";
            }
        
            return View(schedule);
        }
        
        // POST: Driver/Departure/5 - Konfirmasi keberangkatan
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Departure(int id, DateTime departureTime, string? notes, string? returnDate)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            // Validasi: Harus sudah ada arrival time (Cek baseSchedule saja cukup karena harusnya sinkron)
            if (!baseSchedule.ActualStartTime.HasValue)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "⚠️ Konfirmasi kedatangan terlebih dahulu!" });
                }
                TempData["ErrorMessage"] = "⚠️ Konfirmasi kedatangan terlebih dahulu!";
                return RedirectToAction(nameof(Arrival), new { id, returnDate });
            }

            if (departureTime < baseSchedule.ActualStartTime.Value)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "⚠️ Waktu keberangkatan tidak boleh lebih awal dari waktu kedatangan!" });
                }
                TempData["ErrorMessage"] = "⚠️ Waktu keberangkatan tidak boleh lebih awal dari waktu kedatangan!";
                return RedirectToAction(nameof(Departure), new { id, returnDate });
            }

            try
            {
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);
                if (!groupSchedules.Any(s => s.ScheduleId == baseSchedule.ScheduleId))
                {
                    groupSchedules.Add(baseSchedule);
                }

                // Periksa kelengkapan kanban untuk menentukan status: Completed vs Shortage Delivery
                var groupIds = groupSchedules.Select(s => s.ScheduleId).ToList();
                var allDeliveryItems = await _context.DeliveryItems
                    .Where(di => groupIds.Contains(di.ScheduleId))
                    .ToListAsync();
                var incompleteItems = allDeliveryItems
                    .Where(di => (di.ActualQuantity ?? 0) < di.Quantity)
                    .ToList();

                bool isKanbanComplete = !incompleteItems.Any();
                string targetStatus = isKanbanComplete ? "Completed" : "Shortage Delivery";

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualEndTime = departureTime;
                    if (!schedule.ActualPickupTime.HasValue)
                    {
                        schedule.ActualPickupTime = departureTime;
                    }
                    schedule.DriverStatus = targetStatus;
                    schedule.Status = targetStatus;
                    schedule.UpdatedDate = DateTime.Now;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                    
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                            ? $"[Departure] {notes}" 
                            : schedule.Notes + $"\n[Departure] {notes}";
                    }
                }

                await _context.SaveChangesAsync();

                var duration = departureTime - baseSchedule.ActualStartTime.Value;
                var durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";

                var statusLog = isKanbanComplete ? "Closed Delivery (Completed)" : "Shortage Delivery (Kanban Parsial)";
                await _logService.LogConfirm(
                    "Driver",
                    $"Group-{baseSchedule.Cycle}",
                    baseSchedule.ScheduleId,
                    $"Konfirmasi keberangkatan GROUP ({groupSchedules.Count} Manifest) dari {baseSchedule.Customer?.CustomerName}. Status: {statusLog}. Durasi: {durationText}",
                    User.Identity?.Name ?? "Driver"
                );

                foreach(var s in groupSchedules) {
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = s.ScheduleNumber,
                        action = "departure",
                        message = $"Delivery ke {s.Customer?.CustomerName} selesai ({targetStatus}).",
                        timestamp = DateTime.Now
                    });
                }

                var successMsg = isKanbanComplete
                    ? $"✅ Keberangkatan berhasil dikonfirmasi untuk {groupSchedules.Count} Manifest! Durasi: {durationText}"
                    : $"⚠️ Keberangkatan tercatat sebagai Shortage Delivery ({incompleteItems.Count} item belum 100%). Durasi: {durationText}";

                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = true, message = successMsg });
                }

                TempData["SuccessMessage"] = successMsg;
                
                var referer = Request.Headers["Referer"].ToString();
                if (!string.IsNullOrEmpty(referer) && !referer.Contains("/Driver/Arrival", StringComparison.OrdinalIgnoreCase) && !referer.Contains("/Driver/Departure", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(referer);
                }
                if (!string.IsNullOrEmpty(returnDate))
                {
                    return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                }
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = ex.Message });
                }
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(Departure), new { id, returnDate });
            }
        }

        // Quick Actions also need to be updated
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickArrival(int id, string? returnDate)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            if (baseSchedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi kedatangan!";
                if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
                if (!string.IsNullOrEmpty(returnDate))
                {
                    return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                }
                return RedirectToAction(nameof(Index));
            }

            try 
            {
                var arrivalTime = DateTime.Now;
                var groupSchedules = await GetSchedulesInGroup(baseSchedule);
                if (!groupSchedules.Any(s => s.ScheduleId == baseSchedule.ScheduleId))
                {
                    groupSchedules.Add(baseSchedule);
                }

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualStartTime = arrivalTime;
                    schedule.ActualPickupTime = arrivalTime; // Record pickup time at arrival as requested
                    schedule.DriverStatus = "In Progress";
                    schedule.UpdatedDate = arrivalTime;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                }

                await _context.SaveChangesAsync();
                
                // SignalR & Log (Simplified for brevity, same logic as above)
                await _logService.LogConfirm("Driver", $"Group-{baseSchedule.Cycle}", baseSchedule.ScheduleId, $"Quick Arrival Group ({groupSchedules.Count})", User.Identity?.Name ?? "Driver");
                
                foreach(var s in groupSchedules) {
                     await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { scheduleNumber = s.ScheduleNumber, action = "arrival", message = "Driver Tiba", timestamp = arrivalTime });
                }

                TempData["SuccessMessage"] = $"✅ Kedatangan dikonfirmasi untuk {groupSchedules.Count} Manifest!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick arrival: " + ex.Message;
            }

            if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
            if (!string.IsNullOrEmpty(returnDate))
            {
                return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickDeparture(int id, string? returnDate)
        {
            var baseSchedule = await _context.DeliverySchedules.Include(s => s.Customer).FirstOrDefaultAsync(s => s.ScheduleId == id);
            if (baseSchedule == null) return NotFound();

            if (!baseSchedule.ActualStartTime.HasValue)
            {
                TempData["ErrorMessage"] = "Konfirmasi kedatangan terlebih dahulu!";
                if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
                if (!string.IsNullOrEmpty(returnDate))
                {
                    return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                }
                return RedirectToAction(nameof(Index));
            }

            if (baseSchedule.ActualEndTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi keberangkatan!";
                if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
                if (!string.IsNullOrEmpty(returnDate))
                {
                    return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                }
                return RedirectToAction(nameof(Index));
            }

            try 
            {
                var departureTime = DateTime.Now;
                
                if (departureTime < baseSchedule.ActualStartTime.Value)
                {
                    TempData["ErrorMessage"] = "âš ï¸ Tidak bisa Quick Departure: Waktu sekarang lebih awal dari waktu kedatangan yang tercatat!";
                    if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
                    if (!string.IsNullOrEmpty(returnDate))
                    {
                        return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
                    }
                    return RedirectToAction(nameof(Index));
                }

                var groupSchedules = await GetSchedulesInGroup(baseSchedule);
                if (!groupSchedules.Any(s => s.ScheduleId == baseSchedule.ScheduleId))
                {
                    groupSchedules.Add(baseSchedule);
                }

                // Periksa kelengkapan kanban: Completed vs Shortage Delivery
                var groupIds = groupSchedules.Select(s => s.ScheduleId).ToList();
                var allDeliveryItems = await _context.DeliveryItems
                    .Where(di => groupIds.Contains(di.ScheduleId))
                    .ToListAsync();
                var incompleteItems = allDeliveryItems
                    .Where(di => (di.ActualQuantity ?? 0) < di.Quantity)
                    .ToList();

                bool isKanbanComplete = !incompleteItems.Any();
                string targetStatus = isKanbanComplete ? "Completed" : "Shortage Delivery";

                foreach (var schedule in groupSchedules)
                {
                    schedule.ActualEndTime = departureTime;
                    if (!schedule.ActualPickupTime.HasValue)
                    {
                        schedule.ActualPickupTime = departureTime;
                    }
                    schedule.DriverStatus = targetStatus;
                    schedule.Status = targetStatus;
                    schedule.UpdatedDate = departureTime;
                    schedule.UpdatedBy = User.Identity?.Name ?? "Driver";
                }

                await _context.SaveChangesAsync();

                var statusLog = isKanbanComplete ? "Closed Delivery (Completed)" : "Shortage Delivery (Kanban Parsial)";
                await _logService.LogConfirm("Driver", $"Group-{baseSchedule.Cycle}", baseSchedule.ScheduleId, $"Quick Departure Group ({groupSchedules.Count}) - {statusLog}", User.Identity?.Name ?? "Driver");
                foreach(var s in groupSchedules) {
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { scheduleNumber = s.ScheduleNumber, action = "departure", message = $"Driver Berangkat ({targetStatus})", timestamp = departureTime });
                }

                TempData["SuccessMessage"] = isKanbanComplete
                    ? $"✅ Keberangkatan dikonfirmasi untuk {groupSchedules.Count} Manifest!"
                    : $"âš ï¸ Keberangkatan tercatat sebagai Shortage Delivery ({groupSchedules.Count} Manifest)!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal memproses quick departure: " + ex.Message;
            }
            
            if (!string.IsNullOrEmpty(Request.Headers["Referer"].ToString())) { return Redirect(Request.Headers["Referer"].ToString()); }
            if (!string.IsNullOrEmpty(returnDate))
            {
                return RedirectToAction(nameof(Index), new { selectedDate = returnDate });
            }
            return RedirectToAction(nameof(Index));
        }
    }
}