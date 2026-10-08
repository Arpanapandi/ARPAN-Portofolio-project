using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Services;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using DeliveryControl.Data;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "User")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _logService;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly IServiceProvider _serviceProvider;

        public HomeController(
            ILogger<HomeController> logger, 
            ApplicationDbContext context, 
            ActivityLogService logService, 
            IHubContext<DeliveryHub> hubContext,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _context = context;
            _logService = logService;
            _hubContext = hubContext;
            _serviceProvider = serviceProvider;
        }

        [HttpGet("/api/migratedata")]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> MigrateData()
        {
            try {
                await DeliveryControl.MigrateDataToSqlite.RunAsync(_serviceProvider);
                return Content("Migration Successful");
            } catch (Exception ex) {
                return Content($"Migration Failed: {ex.ToString()}");
            }
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin")]
        public async Task<IActionResult> ToggleForceOnTime()
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ForceOnTimeMode");
            bool current = false;
            
            if (setting == null)
            {
                setting = new SystemSetting { Key = "ForceOnTimeMode", Value = "true" };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                current = setting.Value == "true";
                setting.Value = current ? "false" : "true";
            }
            
            await _context.SaveChangesAsync();
            
            // Broadcast toggle status globally so other devices can sync the mode and button
            await _hubContext.Clients.All.SendAsync("forceOnTimeToggled", new { isActive = !current });

            return Json(new { success = true, isActive = !current });
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin")]
        public async Task<IActionResult> SetGlobalDateRangeFilter([FromBody] DateRangeFilterRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.StartDate) || string.IsNullOrWhiteSpace(req.EndDate))
                return Json(new { success = false, message = "Tanggal mulai dan selesai wajib diisi." });

            var startSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GlobalFilterStartDate");
            if (startSetting == null)
            {
                startSetting = new SystemSetting { Key = "GlobalFilterStartDate", Value = req.StartDate.Trim() };
                _context.SystemSettings.Add(startSetting);
            }
            else
            {
                startSetting.Value = req.StartDate.Trim();
            }

            var endSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GlobalFilterEndDate");
            if (endSetting == null)
            {
                endSetting = new SystemSetting { Key = "GlobalFilterEndDate", Value = req.EndDate.Trim() };
                _context.SystemSettings.Add(endSetting);
            }
            else
            {
                endSetting.Value = req.EndDate.Trim();
            }

            await _context.SaveChangesAsync();

            // Broadcast filter status globally so other devices can sync dashboard
            await _hubContext.Clients.All.SendAsync("globalDateRangeFilterToggled", new { startDate = req.StartDate, endDate = req.EndDate });

            return Json(new { success = true, startDate = req.StartDate, endDate = req.EndDate });
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin")]
        public async Task<IActionResult> ResetGlobalDateRangeFilter()
        {
            var startSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GlobalFilterStartDate");
            if (startSetting != null) _context.SystemSettings.Remove(startSetting);

            var endSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "GlobalFilterEndDate");
            if (endSetting != null) _context.SystemSettings.Remove(endSetting);

            await _context.SaveChangesAsync();

            // Broadcast filter reset globally
            await _hubContext.Clients.All.SendAsync("globalDateRangeFilterToggled", new { startDate = (string?)null, endDate = (string?)null });

            return Json(new { success = true });
        }

        public class DateRangeFilterRequest
        {
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> Index(DateTime? date = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var forceOnTimeSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "ForceOnTimeMode");
            bool forceOnTimeMode = forceOnTimeSetting?.Value == "true";
            ViewBag.ForceOnTimeMode = forceOnTimeMode;

            var autoDockSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            bool autoEnterDockActive = autoDockSetting != null && autoDockSetting.Value == "1";
            ViewBag.AutoEnterDockActive = autoEnterDockActive;

            var skipLeaderSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            bool skipLeaderVerifActive = skipLeaderSetting != null && skipLeaderSetting.Value == "1";
            ViewBag.SkipLeaderVerificationActive = skipLeaderVerifActive;

            // Fetch dynamic available dates for range filter dropdown
            var rawAvailableDates = await _context.DeliverySchedules
                .AsNoTracking()
                .Where(s => s.Status != "Cancelled")
                .Select(s => s.ScheduledDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();
            
            ViewBag.AvailableDates = rawAvailableDates.Select(d => d.ToString("yyyy-MM-dd")).ToList();

            // Global Filter Date Range (Super Admin)
            var startSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterStartDate");
            var endSetting   = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterEndDate");

            DateTime? activeStartDate = startDate?.Date ?? (DateTime.TryParse(startSetting?.Value, out var sParsed) ? sParsed.Date : null);
            DateTime? activeEndDate   = endDate?.Date   ?? (DateTime.TryParse(endSetting?.Value, out var eParsed) ? eParsed.Date : null);

            ViewBag.ActiveStartDate = activeStartDate?.ToString("yyyy-MM-dd");
            ViewBag.ActiveEndDate   = activeEndDate?.ToString("yyyy-MM-dd");
            ViewBag.IsRangeFilterActive = activeStartDate.HasValue && activeEndDate.HasValue;
            
            var now = DateTime.Now;
            var today = date?.Date ?? DateTime.Today;
            ViewBag.DisplayDate = today;
            var isViewingPast = date.HasValue && date.Value.Date < DateTime.Today;
            var effectiveNow = isViewingPast ? today.AddDays(1).AddSeconds(-1) : now;

            var minCarryOver = today.AddDays(-7);
            var indexQuery = _context.DeliverySchedules
                .AsNoTracking()
                .AsSplitQuery()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            if (activeStartDate.HasValue && activeEndDate.HasValue)
            {
                var endExclusive = activeEndDate.Value.Date.AddDays(1);
                indexQuery = indexQuery.Where(s => s.ScheduledDate >= activeStartDate.Value.Date && s.ScheduledDate < endExclusive);
            }
            else if (isViewingPast)
            {
                var nextDay = today.AddDays(1);
                indexQuery = indexQuery.Where(s => s.ScheduledDate >= today && s.ScheduledDate < nextDay);
            }
            else
            {
                indexQuery = indexQuery.Where(s => s.ScheduledDate >= minCarryOver);
            }

            var allSchedules = await indexQuery.ToListAsync();

            var tomorrowDate = today.AddDays(1);
            bool isRangeFilterActive = activeStartDate.HasValue && activeEndDate.HasValue;
            List<DeliverySchedule> todaySchedules = FilterAndOrderTodaySchedules(allSchedules, today, isRangeFilterActive, isViewingPast, minCarryOver);

            // Auto-reset: Kembalikan orderan yang terlanjur masuk dock in padahal scan kanban masih 0 ke WAITING PROSES (Scheduled)
            var invalidDockSchedules = todaySchedules
                .Where(s => s.ActualEnterDockTime.HasValue && !s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue)
                .Where(s => {
                    if (s.DeliveryItems == null || !s.DeliveryItems.Any()) return false;
                    double scanned = 0;
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot > 0 ? di.Item.QtyLot.Value : 1;
                        scanned += (di.ActualQuantity ?? 0) >= di.Quantity ? Math.Ceiling((double)di.Quantity / qpc) : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                    }
                    return scanned == 0;
                })
                .ToList();

            if (invalidDockSchedules.Any())
            {
                foreach (var inv in invalidDockSchedules)
                {
                    inv.ActualEnterDockTime = null;
                    inv.Status = "Scheduled";
                    inv.PreparationStatus = "Scheduled";
                    inv.DriverStatus = "Scheduled";
                    inv.IsLeaderVerified = false;
                    inv.LeaderVerifiedKanbanCount = 0;
                    inv.UpdatedDate = DateTime.Now;
                    inv.UpdatedBy = "AutoResetInvalidDockIn";
                }
                await _context.SaveChangesAsync();
            }

            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    IsCarryOver = g.All(s => s.ScheduledDate.Date < today),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Closed delivery") ? "Closed delivery" : 
                                   (g.Any(s => GetDisplayStatus(s) == "Shortage delivery") ? "Shortage delivery" : 
                                   (g.Any(s => GetDisplayStatus(s) == "Progres preparation") ? "Progres preparation" : "Waiting preparation")),
                    IsPrepared = g.All(s => s.PreparationStatus == "Prepared")
                })
                .OrderBy(g => g.First.ScheduledDate.Date) // Urutkan berdasarkan tanggal secara kronologis
                .ThenBy(g => g.DisplayStatus == "Closed delivery" ? 1 : 0) // Incomplete di atas, Closed di bawah
                .ThenBy(g => {
                    var sp = g.First.StartPrepareTime > 0 ? g.First.StartPrepareTime : (g.First.Customer?.StartPrepareTime ?? 0);
                    return sp > 0 ? sp : int.MaxValue;
                })
                .ThenBy(g => g.First.PickupTime)
                .ToList();

            int completedCount = 0;
            int inProgressCount = 0;
            
            // Jumlah order yang seharusnya sudah delivered berdasarkan ETD sampai jam sekarang
            var shouldBeDeliveredCount = todaySchedules
                .GroupBy(s => new { 
                    s.CustomerId, 
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(), 
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(), 
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Manifest = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(),
                    Date = s.ScheduledDate.Date 
                })
                .Count(g => (g.First().ETD <= effectiveNow) == true);
            
            // --- ALIGN EXACTLY WITH _ScheduleTablePartial.cshtml ---
            int delayPrepareCount = 0;
            int delayPickupCount = 0;
            int onTimePrepareCount = 0;
            int onTimeDockInCount = 0;
            int onTimeLoadingCount = 0;
            int onTimeDeliveryCount = 0;
            int delayDeliveryCount = 0;
            int delayDockInCount = 0;
            int preparedCount = 0;
            int forceCompletedCount = 0;
            int forceInProgressCount = 0;
            int forceOnTimePrepareCount = 0;
            int forceOnTimeDockInCount = 0;
            int forceOnTimeLoadingCount = 0;
            int forceOnTimeDeliveryCount = 0;
            int forceWaitingProsesCount = 0;
            int shortageCount = 0;
            int sudahProsesAcc = 0;
            int waitingProsesAcc = 0;
            int pastClosedGroupsCount = 0;

            var tableGroups = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                });

            foreach (var g in tableGroups)
            {
                var schedules = g.ToList();
                var item = schedules.First();
                
                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                var daysAhead = (item.ScheduledDate.Date - today.Date).Days;
                var isFutureSchedule = daysAhead > 0;
                var isH1Shift = isFutureSchedule &&
                                (daysAhead == 1 ||
                                 (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                 (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today.Date && item.PickupTime.HasValue && pickupHour < 12;
                
                var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today.Date && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                var isPastCompletedGroup = item.ScheduledDate.Date < today.Date && schedules.All(s => GetDisplayStatus(s) == "Closed delivery");
                // Allow all table groups to be processed by indicator counters for 100% accuracy with table rows

                var dockInTimeTarget = item.EnterDockTime;
                var pickupTimeTarget = item.PickupTime;
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    planEndTime = planEndTime.AddDays(-h1ShiftDays);
                }

                var isCarryOverSched = item.ScheduledDate.Date < today.Date;
                var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                
                if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                    var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                    if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                }
                if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                    pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                }

                if (!isCarryOverSched) {
                    var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                    if (isH1Shift || isTodayH1 || isOvernightPrep) {
                        var h1ShiftDays = isH1Shift ? daysAhead : 1;
                        startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                    }
                    if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                    if (pickupTimeTarget.HasValue && !isTodayH1) {
                        var anchor = dockInTimeTarget ?? planEndTime;
                        if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                    }
                }

                var isCompleted = schedules.All(s => s.ActualEndTime.HasValue || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase));

                double totalKanbanTarget = 0;
                double totalKanbanActual = 0;
                foreach(var s in schedules)
                {
                    if (s.DeliveryItems != null)
                    {
                        foreach(var di in s.DeliveryItems)
                        {
                            var qpc = di.Item?.QtyLot ?? 1;
                            if (qpc > 0)
                            {
                                var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                totalKanbanTarget += diKbnTarget;
                                totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                    ? diKbnTarget
                                    : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                            }
                        }
                    }
                }
                bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                var groupFirstPrepTime = schedules.SelectMany(s => s.DeliveryItems ?? Enumerable.Empty<DeliveryItem>()).Where(di => di.PrepScanTime.HasValue).Select(di => (DateTime?)di.PrepScanTime).Min()
                    ?? schedules.SelectMany(s => s.PreparationRecords ?? Enumerable.Empty<PreparationRecord>()).Select(pr => (DateTime?)pr.CreatedDate).Min();

                string displayStatus = "Scheduled";
                if (!isCompleted)
                {
                    if (schedules.All(s => s.ActualEndTime.HasValue))
                    {
                        displayStatus = "Completed";
                    }
                    else if (item.ActualPickupTime.HasValue || schedules.Any(s => s.ActualStartTime.HasValue))
                    {
                        displayStatus = "In Progress";
                    }
                    else if (pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value)
                    {
                        displayStatus = "Waiting Pickup";
                    }
                    else if (item.ActualEnterDockTime.HasValue || schedules.Any(s => s.ActualEnterDockTime.HasValue))
                    {
                        displayStatus = "In Progress";
                    }
                    else if (dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value)
                    {
                        displayStatus = "Waiting Dock In";
                    }
                    else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                    {
                        displayStatus = "Prepared";
                    }
                    else if (schedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") || groupFirstPrepTime.HasValue || totalKanbanActual > 0)
                    {
                        displayStatus = "Inprogres Prepare";
                    }
                    else
                    {
                        displayStatus = "Waiting Prepare";
                    }
                }

                if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                    && !isCompleted
                    && !schedules.Any(s => s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue)
                    && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    displayStatus = "Delayed";
                }

                var isDelayed = displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase);
                var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);

                var isDelayPickup = false;
                if (!item.ActualPickupTime.HasValue && pickupTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= pickupTimeTarget.Value.AddMinutes(20)) isDelayPickup = true;
                }
                if (isCarryOverSched && !isCompleted && !item.ActualPickupTime.HasValue) {
                    isDelayPickup = true;
                }

                var isDelayDelivery = false;
                if (!item.ActualEndTime.HasValue && !isCompleted) {
                    DateTime? deliveryTargetTime = item.ETD;
                    if (!deliveryTargetTime.HasValue && pickupTimeTarget.HasValue) {
                        deliveryTargetTime = pickupTimeTarget.Value.AddMinutes(20);
                    }
                    if (deliveryTargetTime.HasValue) {
                        var etdTarget = deliveryTargetTime.Value;
                        if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                        if (effectiveNow >= etdTarget) isDelayDelivery = true;
                    }
                    if (!isDelayDelivery && pickupTimeTarget.HasValue) {
                        var fallbackTarget = pickupTimeTarget.Value.AddMinutes(20);
                        if (effectiveNow >= fallbackTarget) isDelayDelivery = true;
                    }
                }
                
                var isDelayDockIn = false;
                if (!item.ActualEnterDockTime.HasValue && dockInTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= dockInTimeTarget.Value) isDelayDockIn = true;
                }
                if (isCarryOverSched && !isCompleted && !item.ActualEnterDockTime.HasValue) {
                    isDelayDockIn = true;
                }

                var groupMaxReadyTime = isKanbanFullyPrepared || isPrepared
                    ? (schedules.Where(s => s.ReadyToDockTime.HasValue).Select(s => (DateTime?)s.ReadyToDockTime).Max()
                       ?? schedules.SelectMany(s => s.DeliveryItems ?? Enumerable.Empty<DeliveryItem>()).Where(di => di.PrepScanTime.HasValue || di.UpdatedDate.HasValue).Select(di => di.PrepScanTime ?? di.UpdatedDate).Max()
                       ?? schedules.SelectMany(s => s.PreparationRecords ?? Enumerable.Empty<PreparationRecord>()).Select(pr => (DateTime?)pr.CreatedDate).Max())
                    : (DateTime?)null;

                var isDelayPrepare = false;
                DateTime effectiveTargetPrep;
                if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                    effectiveTargetPrep = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                else if (dockInTimeTarget.HasValue)
                    effectiveTargetPrep = dockInTimeTarget.Value;
                else
                    effectiveTargetPrep = planEndTime;

                if (isKanbanFullyPrepared || isPrepared)
                {
                    if (groupMaxReadyTime.HasValue)
                    {
                        if (groupMaxReadyTime.Value > effectiveTargetPrep && (stdPMinutes > 0 || dockInTimeTarget.HasValue))
                        {
                            isDelayPrepare = true;
                        }
                    }
                    else
                    {
                        isDelayPrepare = false;
                    }
                }
                else
                {
                    if (effectiveNow > effectiveTargetPrep && (stdPMinutes > 0 || dockInTimeTarget.HasValue))
                    {
                        isDelayPrepare = true;
                    }
                }
                if (isCarryOverSched && !isCompleted)
                {
                    if (!isKanbanFullyPrepared) isDelayPrepare = true;
                    if (!item.ActualEnterDockTime.HasValue) isDelayDockIn = true;
                    if (!item.ActualEndTime.HasValue && !item.ActualPickupTime.HasValue) isDelayPickup = true;
                    if (!item.ActualEndTime.HasValue) isDelayDelivery = true;
                }

                var isPickupLate = false;
                var planBaseDate = item.CreatedDate;
                var actDockTimeH = item.ActualEnterDockTime ?? schedules.Where(s => s.ActualEnterDockTime.HasValue).Select(s => s.ActualEnterDockTime).FirstOrDefault();
                if (pickupTimeTarget.HasValue && actDockTimeH.HasValue) {
                    var pickupEndTarget = pickupTimeTarget.Value.AddMinutes(20);
                    var actArrival = item.ActualPickupTime ?? item.ActualStartTime ?? schedules.Where(s => s.ActualPickupTime.HasValue || s.ActualStartTime.HasValue).Select(s => s.ActualPickupTime ?? s.ActualStartTime).FirstOrDefault();
                    var actDeparture = item.ActualEndTime ?? schedules.Where(s => s.ActualEndTime.HasValue).Select(s => s.ActualEndTime).FirstOrDefault();
                    if (actDeparture.HasValue && actDeparture.Value < actDockTimeH.Value)
                    {
                        actDeparture = null;
                    }
                    if (actArrival.HasValue && actArrival.Value < actDockTimeH.Value.AddMinutes(-30))
                    {
                        actArrival = null;
                    }
                    if (actDeparture.HasValue && !actArrival.HasValue)
                    {
                        actArrival = actDeparture;
                    }

                    if (actDeparture.HasValue) {
                        var actualDep = actDeparture.Value;
                        if (planBaseDate > actualDep && planBaseDate > pickupEndTarget) actualDep = planBaseDate;
                        if ((actualDep - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                    else if (actArrival.HasValue) {
                        var actualArr = actArrival.Value;
                        if (planBaseDate > actualArr && planBaseDate > pickupEndTarget) actualArr = planBaseDate;
                        if ((effectiveNow - pickupEndTarget).TotalMinutes > 0 || (actualArr - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                    else {
                        if ((effectiveNow - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                }

                var isDeliveryLate = false;
                if (item.ActualEndTime.HasValue && item.ETD.HasValue) {
                    var actualE = item.ActualEndTime.Value;
                    if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                    var etdForPunctuality = item.ETD.Value;
                    if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                    if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                }

                var isDockDelay = false;
                if (item.ActualEnterDockTime.HasValue && dockInTimeTarget.HasValue) {
                    var actualD = item.ActualEnterDockTime.Value;
                    if (planBaseDate > actualD && planBaseDate > dockInTimeTarget.Value) actualD = planBaseDate;
                    var dockEndTarget = dockInTimeTarget.Value.AddMinutes(15);
                    if ((actualD - dockEndTarget).TotalMinutes > 0) isDockDelay = true;
                }

                var startPrepDT_Force = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                if (isH1Shift || isTodayH1 || isOvernightPrep)
                {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    startPrepDT_Force = startPrepDT_Force.AddDays(-h1ShiftDays);
                }
                bool isPickupReached_Force = isCarryOverSched || (pickupTimeTarget.HasValue && pickupTimeTarget.Value <= effectiveNow);
                bool isDockReached_Force   = isCarryOverSched || (dockInTimeTarget.HasValue && dockInTimeTarget.Value <= effectiveNow);
                bool isPrepReached_Force   = isCarryOverSched || (planEndTime <= effectiveNow);
                bool isPrepStarted_Force   = isCarryOverSched || (startPMinutes > 0 && effectiveNow >= startPrepDT_Force);

                if (isPickupReached_Force)
                {
                    forceCompletedCount++;
                    forceOnTimeLoadingCount++;
                    forceOnTimeDeliveryCount++;
                }
                else if (isPrepReached_Force || isPrepStarted_Force)
                {
                    forceInProgressCount++;
                }
                else
                {
                    forceWaitingProsesCount++;
                }

                if (isPrepReached_Force) forceOnTimePrepareCount++;
                if (isDockReached_Force) forceOnTimeDockInCount++;

                if (forceOnTimeMode)
                {
                    isDelayPrepare = false;
                    isPickupLate = false;
                    isDelayPickup = false;
                    isDeliveryLate = false;
                    isDelayDelivery = false;
                    isDockDelay = false;
                    isDelayDockIn = false;
                    isDelayed = false;
                }

                // --- H-1 schedules MUST also be counted in today's delay cards! ---
                // Hanya hitung sebagai "Terlambat Prepare" jika saat ini MASIH BELUM selesai prepare
                if (isDelayPrepare && !isPrepared && !isCompleted) delayPrepareCount++;
                
                DateTime? actArrivalH2 = null;
                DateTime? actDepartureH2 = null;
                if (actDockTimeH.HasValue)
                {
                    actArrivalH2 = item.ActualPickupTime ?? item.ActualStartTime ?? schedules.Where(s => s.ActualPickupTime.HasValue || s.ActualStartTime.HasValue).Select(s => s.ActualPickupTime ?? s.ActualStartTime).FirstOrDefault();
                    if (actArrivalH2.HasValue && actArrivalH2.Value < actDockTimeH.Value.AddMinutes(-30))
                    {
                        actArrivalH2 = null;
                    }

                    actDepartureH2 = item.ActualEndTime ?? schedules.Where(s => s.ActualEndTime.HasValue).Select(s => s.ActualEndTime).FirstOrDefault();
                    if (actDepartureH2.HasValue && actDepartureH2.Value < actDockTimeH.Value)
                    {
                        actDepartureH2 = null;
                    }

                    if (actDepartureH2.HasValue && !actArrivalH2.HasValue)
                    {
                        actArrivalH2 = actDepartureH2;
                    }
                }

                bool hasPickupBadge = false;
                if (!isCompleted)
                {
                    if (actArrivalH2.HasValue || actDepartureH2.HasValue)
                    {
                        hasPickupBadge = isPickupLate;
                    }
                    else
                    {
                        hasPickupBadge = isDelayPickup;
                    }
                }
                if (hasPickupBadge) delayPickupCount++;
                
                bool isDeparted = actDockTimeH.HasValue && actDepartureH2.HasValue;
                bool isPastClosedGroup = isCarryOverSched && isDeparted && isKanbanFullyPrepared;

                if (!isPastClosedGroup)
                {
                    if (isDeparted)
                    {
                        if (isKanbanFullyPrepared)
                        {
                            completedCount++;
                        }
                        else
                        {
                            shortageCount++;
                        }
                    }
                    else if (totalKanbanActual > 0 || isKanbanFullyPrepared || schedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared"))
                    {
                        inProgressCount++;
                    }

                    DateTime? actDelivDT = isDeparted ? actDepartureH2 : (DateTime?)null;
                    if (actDelivDT.HasValue)
                    {
                        var endPickupTarget = pickupTimeTarget.HasValue ? pickupTimeTarget.Value.AddMinutes(20) : (DateTime?)null;
                        if (endPickupTarget.HasValue && actDelivDT.Value > endPickupTarget.Value)
                        {
                            delayDeliveryCount++;
                        }
                        else
                        {
                            onTimeDeliveryCount++;
                        }
                    }
                    else if (!isCompleted && (isDelayDelivery || isCarryOverSched))
                    {
                        delayDeliveryCount++;
                    }
                    
                    bool hasDockBadge = false;
                    if (!isCompleted)
                    {
                        if (item.ActualEnterDockTime.HasValue)
                        {
                            hasDockBadge = isDockDelay;
                        }
                        else
                        {
                            hasDockBadge = isDelayDockIn || isDelayed;
                        }
                    }
                    if (hasDockBadge) delayDockInCount++;
                    
                    if (isPrepared && !isCompleted) preparedCount++;

                    bool hasOnTimePrepareBadge = (isKanbanFullyPrepared || isPrepared || (groupMaxReadyTime.HasValue && isCompleted)) && !isDelayPrepare;
                    if (hasOnTimePrepareBadge) onTimePrepareCount++;

                    bool hasOnTimeDockInBadge = item.ActualEnterDockTime.HasValue && !isDockDelay;
                    if (hasOnTimeDockInBadge) onTimeDockInCount++;

                    bool hasOnTimeLoadingBadge = actArrivalH2.HasValue && !isPickupLate;
                    if (hasOnTimeLoadingBadge) onTimeLoadingCount++;

                    if (totalKanbanActual > 0)
                    {
                        sudahProsesAcc++;
                    }
                    else
                    {
                        waitingProsesAcc++;
                    }
                }
                else
                {
                    pastClosedGroupsCount++;
                }
            }
            
            if (forceOnTimeMode)
            {
                delayPickupCount = 0;
                delayDockInCount = 0;
                delayPrepareCount = 0;
                delayDeliveryCount = 0;
                shortageCount = 0;
                completedCount = forceCompletedCount;
                inProgressCount = forceInProgressCount;
                onTimePrepareCount = forceOnTimePrepareCount;
                onTimeDockInCount = forceOnTimeDockInCount;
                onTimeLoadingCount = forceOnTimeLoadingCount;
                onTimeDeliveryCount = forceOnTimeDeliveryCount;
                sudahProsesAcc = forceCompletedCount + forceInProgressCount;
                waitingProsesAcc = forceWaitingProsesCount;
            }

            int totalTodayCount = Math.Max(0, groupedSchedules.Count - pastClosedGroupsCount);
            int sudahProses = sudahProsesAcc;
            int waitingProses = waitingProsesAcc;

            int onTimePrepare = onTimePrepareCount;
            if (onTimePrepare < 0) onTimePrepare = 0;

            int onTimeDockIn = onTimeDockInCount;
            if (onTimeDockIn < 0) onTimeDockIn = 0;

            int onTimeLoading = onTimeLoadingCount;
            if (onTimeLoading < 0) onTimeLoading = 0;

            int onTimeDelivery = onTimeDeliveryCount;
            if (onTimeDelivery < 0) onTimeDelivery = 0;

            // ShortageCount dihitung berdasarkan yang belum closed dan qty kanban belum penuh
            if (shortageCount < 0) shortageCount = 0;

            // Set ViewBag
            ViewBag.TodaySchedules = totalTodayCount;
            ViewBag.SudahProsesCount = sudahProses;
            ViewBag.WaitingProsesCount = waitingProses;

            ViewBag.OnTimePrepareCount = onTimePrepare;
            ViewBag.DelayPrepareCount = delayPrepareCount;

            ViewBag.OnTimeDockInCount = onTimeDockIn;
            ViewBag.NotArrivedCount = delayDockInCount; // Delay Dock In

            ViewBag.OnTimeLoadingCount = onTimeLoading;
            ViewBag.DelayPickupCount = delayPickupCount; // Delay Loading

            ViewBag.OnTimeDeliveryCount = onTimeDelivery;
            ViewBag.DelayDeliveryCount = delayDeliveryCount;

            ViewBag.CompletedCount = completedCount; // Closed Delivery
            ViewBag.ShortageCount = shortageCount; // Shortage Delivery

            ViewBag.InProgressCount = inProgressCount;
            ViewBag.PreparedCount = preparedCount;
            ViewBag.ShouldBeDeliveredCount = shouldBeDeliveredCount;
            ViewBag.SelectedDate = today.ToString("yyyy-MM-dd");
            ViewBag.TodayScheduleCount = todaySchedules.Count(s => s.ScheduledDate.Date == today);
            ViewBag.TomorrowScheduleCount = todaySchedules.Count(s => s.ScheduledDate.Date == tomorrowDate);
            ViewBag.DisplayDate = today;

            return View(todaySchedules);
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> PreparationDashboard(DateTime? date = null)
        {
            var today = date?.Date ?? DateTime.Today;
            var nextDay = today.AddDays(1);
            var allSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .Where(s => (s.ScheduledDate >= today && s.ScheduledDate < nextDay) || (s.ScheduledDate < today && s.Status != "Completed"))
                .ToListAsync();

            // Flatten to Items for granular sorting
            var allItems = allSchedules
                .SelectMany(s => s.DeliveryItems)
                .OrderBy(di => {
                    bool isCompleted = (di.ActualQuantity ?? 0) >= di.Quantity;
                    bool isPreparing = (di.ActualQuantity ?? 0) > 0 && !isCompleted;
                    if (isPreparing) return 0; // Top
                    if (isCompleted) return 2; // Bottom
                    return 1; // Middle (Waiting)
                })
                .ThenByDescending(di => di.UpdatedDate ?? di.CreatedDate) // Recent scans first
                .ThenBy(di => di.DeliverySchedule?.PickupTime ?? di.DeliverySchedule?.ScheduledDate ?? DateTime.MaxValue)
                .ToList();

            ViewBag.SelectedDate = today.ToString("yyyy-MM-dd");
            ViewBag.PendingPrepCount = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null);
            return View(allItems);
        }

        private string GetDisplayStatus(DeliverySchedule s)
        {
            bool isDeparted = s.ActualEnterDockTime.HasValue && s.ActualEndTime.HasValue;
            bool isKanbanDone = s.DeliveryItems == null || !s.DeliveryItems.Any() || s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
            if (isDeparted)
            {
                return isKanbanDone ? "Closed delivery" : "Shortage delivery";
            }
            bool hasScan = (s.DeliveryItems != null && s.DeliveryItems.Any(di => (di.ActualQuantity ?? 0) > 0)) || isKanbanDone;
            if (hasScan || s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared")
            {
                return "Progres preparation";
            }
            return "Waiting preparation";
        }

        private List<DeliverySchedule> FilterAndOrderTodaySchedules(List<DeliverySchedule> allSchedules, DateTime today, bool isRangeFilterActive, bool isViewingPast, DateTime minCarryOver)
        {
            List<DeliverySchedule> filtered;
            if (isRangeFilterActive || isViewingPast)
            {
                filtered = allSchedules;
            }
            else
            {
                var tempGroups = allSchedules
                    .Where(s => s.ScheduledDate.Date >= minCarryOver)
                    .GroupBy(s => new {
                        CustId = s.CustomerId,
                        Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                        Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                        Route = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper(),
                        Date  = s.ScheduledDate.Date
                    });

                filtered = tempGroups
                    .Where(g => g.Key.Date == today.Date || !g.All(s => GetDisplayStatus(s) == "Closed delivery"))
                    .SelectMany(g => g)
                    .ToList();
            }

            return filtered
                .OrderBy(s => s.ScheduledDate.Date)
                .ThenBy(s => GetDisplayStatus(s) == "Closed delivery" ? 1 : 0)
                .ThenBy(s => {
                    var sp = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
                    return sp > 0 ? sp : int.MaxValue;
                })
                .ThenBy(s => s.PickupTime)
                .ToList();
        }

        // Action for SignalR Dashboard Update (Polling replacement)
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetScheduleTableData(DateTime? date = null, bool? forceOnTime = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            if (forceOnTime.HasValue)
            {
                ViewBag.ForceOnTimeMode = forceOnTime.Value;
            }
            else
            {
                var forceOnTimeSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "ForceOnTimeMode");
                ViewBag.ForceOnTimeMode = forceOnTimeSetting?.Value == "true";
            }

            // Baca Global Date Range Filter dari SystemSettings jika tidak dikirim via parameter
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var startSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterStartDate");
                var endSetting   = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterEndDate");
                if (DateTime.TryParse(startSetting?.Value, out var sParsed)) startDate = sParsed.Date;
                if (DateTime.TryParse(endSetting?.Value,   out var eParsed)) endDate   = eParsed.Date;
            }

            bool isRangeFilterActive = startDate.HasValue && endDate.HasValue;

            var now = DateTime.Now;
            var today = date?.Date ?? DateTime.Today;
            var isViewingPast = date.HasValue && date.Value.Date < DateTime.Today;
            var effectiveNow = isViewingPast ? today.AddDays(1).AddSeconds(-1) : now;

            var autoDockSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            bool autoEnterDockActive = autoDockSetting != null && autoDockSetting.Value == "1";
            ViewBag.AutoEnterDockActive = autoEnterDockActive;

            var skipLeaderSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            bool skipLeaderVerifActive = skipLeaderSetting != null && skipLeaderSetting.Value == "1";
            ViewBag.SkipLeaderVerificationActive = skipLeaderVerifActive;

            var minCarryOver = today.AddDays(-7);
            var tableQuery = _context.DeliverySchedules
                .AsNoTracking()
                .AsSplitQuery()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            if (isRangeFilterActive)
            {
                // Mode filter range: HANYA tampilkan jadwal dalam rentang startDate s/d endDate
                var rangeEnd = endDate!.Value.Date.AddDays(1); // exclusive upper bound
                tableQuery = tableQuery.Where(s => s.ScheduledDate >= startDate!.Value.Date && s.ScheduledDate < rangeEnd);
            }
            else if (isViewingPast)
            {
                var nextDay = today.AddDays(1);
                tableQuery = tableQuery.Where(s => s.ScheduledDate >= today && s.ScheduledDate < nextDay);
            }
            else
            {
                tableQuery = tableQuery.Where(s => s.ScheduledDate >= minCarryOver);
            }

            var allSchedules = await tableQuery.ToListAsync();

            List<DeliverySchedule> todaySchedules = FilterAndOrderTodaySchedules(allSchedules, today, isRangeFilterActive, isViewingPast, minCarryOver);

            ViewBag.DisplayDate = today;
            return PartialView("_ScheduleTablePartial", todaySchedules);
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> GetPrepTableData()
        {
            var today = DateTime.Today;
            var nextDay = today.AddDays(1);
            var allSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .Where(s => (s.ScheduledDate >= today && s.ScheduledDate < nextDay) || (s.ScheduledDate < today && s.Status != "Completed"))
                .ToListAsync();

            var allItems = allSchedules
                .SelectMany(s => s.DeliveryItems)
                .OrderBy(di => {
                    bool isCompleted = (di.ActualQuantity ?? 0) >= di.Quantity;
                    bool isPreparing = (di.ActualQuantity ?? 0) > 0 && !isCompleted;
                    if (isPreparing) return 0; // Top
                    if (isCompleted) return 2; // Bottom
                    return 1; // Middle (Waiting)
                })
                .ThenByDescending(di => di.UpdatedDate ?? di.CreatedDate) // Recent scans first
                .ThenBy(di => di.DeliverySchedule?.PickupTime ?? di.DeliverySchedule?.ScheduledDate ?? DateTime.MaxValue)
                .ToList();

            return PartialView("_PrepTableDirectPartial", allItems);
        }

        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> GetDashboardStatistics(DateTime? date = null, bool? forceOnTime = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            bool forceOnTimeMode;
            if (forceOnTime.HasValue)
            {
                forceOnTimeMode = forceOnTime.Value;
            }
            else
            {
                var forceOnTimeSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "ForceOnTimeMode");
                forceOnTimeMode = forceOnTimeSetting?.Value == "true";
            }

            // Baca Global Date Range Filter dari SystemSettings jika tidak dikirim via parameter
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var startSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterStartDate");
                var endSetting   = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "GlobalFilterEndDate");
                if (DateTime.TryParse(startSetting?.Value, out var sParsed)) startDate = sParsed.Date;
                if (DateTime.TryParse(endSetting?.Value,   out var eParsed)) endDate   = eParsed.Date;
            }

            bool isRangeFilterActive = startDate.HasValue && endDate.HasValue;

            var now = DateTime.Now;
            var today = date?.Date ?? DateTime.Today;
            var isViewingPast = date.HasValue && date.Value.Date < DateTime.Today;
            var effectiveNow = isViewingPast ? today.AddDays(1).AddSeconds(-1) : now;

            var minCarryOver = today.AddDays(-7);
            var statsQuery = _context.DeliverySchedules
                .AsNoTracking()
                .AsSplitQuery()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            if (isRangeFilterActive)
            {
                var rangeEnd = endDate!.Value.Date.AddDays(1);
                statsQuery = statsQuery.Where(s => s.ScheduledDate >= startDate!.Value.Date && s.ScheduledDate < rangeEnd);
            }
            else if (isViewingPast)
            {
                var nextDay = today.AddDays(1);
                statsQuery = statsQuery.Where(s => s.ScheduledDate >= today && s.ScheduledDate < nextDay);
            }
            else
            {
                statsQuery = statsQuery.Where(s => s.ScheduledDate >= minCarryOver);
            }

            var allSchedules = await statsQuery.ToListAsync();

            List<DeliverySchedule> todaySchedules = FilterAndOrderTodaySchedules(allSchedules, today, isRangeFilterActive, isViewingPast, minCarryOver);

            // Auto-reset: Kembalikan orderan yang terlanjur masuk dock in padahal scan kanban masih 0 ke WAITING PROSES (Scheduled)
            var invalidDockSchedulesJson = todaySchedules
                .Where(s => s.ActualEnterDockTime.HasValue && !s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue)
                .Where(s => {
                    if (s.DeliveryItems == null || !s.DeliveryItems.Any()) return false;
                    double scanned = 0;
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot > 0 ? di.Item.QtyLot.Value : 1;
                        scanned += (di.ActualQuantity ?? 0) >= di.Quantity ? Math.Ceiling((double)di.Quantity / qpc) : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                    }
                    return scanned == 0;
                })
                .ToList();

            if (invalidDockSchedulesJson.Any())
            {
                foreach (var inv in invalidDockSchedulesJson)
                {
                    inv.ActualEnterDockTime = null;
                    inv.Status = "Scheduled";
                    inv.PreparationStatus = "Scheduled";
                    inv.DriverStatus = "Scheduled";
                    inv.IsLeaderVerified = false;
                    inv.LeaderVerifiedKanbanCount = 0;
                    inv.UpdatedDate = DateTime.Now;
                    inv.UpdatedBy = "AutoResetInvalidDockIn";
                }
                await _context.SaveChangesAsync();
            }

            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    IsCarryOver = g.All(s => s.ScheduledDate.Date < today),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Closed delivery") ? "Closed delivery" : 
                                   (g.Any(s => GetDisplayStatus(s) == "Shortage delivery") ? "Shortage delivery" : 
                                   (g.Any(s => GetDisplayStatus(s) == "Progres preparation") ? "Progres preparation" : "Waiting preparation"))
                })
                .ToList();

            int completedCount = 0;
            int inProgressCount = 0;
            
            var shouldBeDeliveredCount = todaySchedules
                .GroupBy(s => new { 
                    s.CustomerId, 
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(), 
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(), 
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Manifest = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper(),
                    Date = s.ScheduledDate.Date 
                })
                .Count(g => (g.First().ETD <= now) == true);

            // --- ALIGN EXACTLY WITH _ScheduleTablePartial.cshtml ---
            int delayPrepareCount = 0;
            int delayPickupCount = 0;
            int onTimePrepareCount = 0;
            int onTimeDockInCount = 0;
            int onTimeLoadingCount = 0;
            int onTimeDeliveryCount = 0;
            int delayDeliveryCount = 0;
            int delayDockInCount = 0;
            int preparedCount = 0;
            int forceCompletedCount = 0;
            int forceInProgressCount = 0;
            int forceOnTimePrepareCount = 0;
            int forceOnTimeDockInCount = 0;
            int forceOnTimeLoadingCount = 0;
            int forceOnTimeDeliveryCount = 0;
            int forceWaitingProsesCount = 0;
            int shortageCount = 0;
            int sudahProsesAcc = 0;
            int waitingProsesAcc = 0;
            int pastClosedGroupsCount = 0;

            var tableGroups = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                });

            foreach (var g in tableGroups)
            {
                var schedules = g.ToList();
                var item = schedules.First();
                
                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                var daysAhead = (item.ScheduledDate.Date - today.Date).Days;
                var isFutureSchedule = daysAhead > 0;
                var isH1Shift = isFutureSchedule &&
                                (daysAhead == 1 ||
                                 (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                 (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today.Date && item.PickupTime.HasValue && pickupHour < 12;
                
                var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today.Date && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;


                var dockInTimeTarget = item.EnterDockTime;
                var pickupTimeTarget = item.PickupTime;
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    planEndTime = planEndTime.AddDays(-h1ShiftDays);
                }

                var isCarryOverSched = item.ScheduledDate.Date < today.Date;
                var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                
                if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                    var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                    if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                }
                if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                    pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                }

                if (!isCarryOverSched) {
                    var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                    if (isH1Shift || isTodayH1 || isOvernightPrep) {
                        var h1ShiftDays = isH1Shift ? daysAhead : 1;
                        startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                    }
                    if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                    if (pickupTimeTarget.HasValue && !isTodayH1) {
                        var anchor = dockInTimeTarget ?? planEndTime;
                        if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                    }
                }

                var isCompleted = schedules.All(s => s.ActualEndTime.HasValue || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase));

                double totalKanbanTarget = 0;
                double totalKanbanActual = 0;
                foreach(var s in schedules)
                {
                    if (s.DeliveryItems != null)
                    {
                        foreach(var di in s.DeliveryItems)
                        {
                            var qpc = di.Item?.QtyLot ?? 1;
                            if (qpc > 0)
                            {
                                var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                totalKanbanTarget += diKbnTarget;
                                totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                    ? diKbnTarget
                                    : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                            }
                        }
                    }
                }
                bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                var groupFirstPrepTime = schedules.SelectMany(s => s.DeliveryItems ?? Enumerable.Empty<DeliveryItem>()).Where(di => di.PrepScanTime.HasValue).Select(di => (DateTime?)di.PrepScanTime).Min()
                    ?? schedules.SelectMany(s => s.PreparationRecords ?? Enumerable.Empty<PreparationRecord>()).Select(pr => (DateTime?)pr.CreatedDate).Min();

                string displayStatus = "Scheduled";
                if (!isCompleted)
                {
                    if (schedules.All(s => s.ActualEndTime.HasValue))
                    {
                        displayStatus = "Completed";
                    }
                    else if (item.ActualPickupTime.HasValue || schedules.Any(s => s.ActualStartTime.HasValue))
                    {
                        displayStatus = "In Progress";
                    }
                    else if (pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value)
                    {
                        displayStatus = "Waiting Pickup";
                    }
                    else if (item.ActualEnterDockTime.HasValue || schedules.Any(s => s.ActualEnterDockTime.HasValue))
                    {
                        displayStatus = "In Progress";
                    }
                    else if (dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value)
                    {
                        displayStatus = "Waiting Dock In";
                    }
                    else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                    {
                        displayStatus = "Prepared";
                    }
                    else if (schedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") || groupFirstPrepTime.HasValue || totalKanbanActual > 0)
                    {
                        displayStatus = "Inprogres Prepare";
                    }
                    else
                    {
                        displayStatus = "Waiting Prepare";
                    }
                }

                if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                    && !isCompleted
                    && !schedules.Any(s => s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue)
                    && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    displayStatus = "Delayed";
                }

                var isDelayed = displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase);
                var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);

                // --- Replicate the exact UI override: if forced Delayed, force isPrepared = false ---
                if (displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase) && 
                   (isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15))))
                {
                    isPrepared = false;
                }

                var isDelayPickup = false;
                if (!item.ActualPickupTime.HasValue && pickupTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= pickupTimeTarget.Value.AddMinutes(20)) isDelayPickup = true;
                }
                if (isCarryOverSched && !isCompleted && !item.ActualPickupTime.HasValue) {
                    isDelayPickup = true;
                }

                var isDelayDelivery = false;
                if (!item.ActualEndTime.HasValue && !isCompleted) {
                    DateTime? deliveryTargetTime = item.ETD;
                    if (!deliveryTargetTime.HasValue && pickupTimeTarget.HasValue) {
                        deliveryTargetTime = pickupTimeTarget.Value.AddMinutes(20);
                    }
                    if (deliveryTargetTime.HasValue) {
                        var etdTarget = deliveryTargetTime.Value;
                        if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                        if (effectiveNow >= etdTarget) isDelayDelivery = true;
                    }
                    if (!isDelayDelivery && pickupTimeTarget.HasValue) {
                        var fallbackTarget = pickupTimeTarget.Value.AddMinutes(20);
                        if (effectiveNow >= fallbackTarget) isDelayDelivery = true;
                    }
                }
                
                var isDelayDockIn = false;
                if (!item.ActualEnterDockTime.HasValue && dockInTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= dockInTimeTarget.Value) isDelayDockIn = true;
                }
                if (isCarryOverSched && !isCompleted && !item.ActualEnterDockTime.HasValue) {
                    isDelayDockIn = true;
                }

                var groupMaxReadyTime = isKanbanFullyPrepared || isPrepared
                    ? (schedules.Where(s => s.ReadyToDockTime.HasValue).Select(s => (DateTime?)s.ReadyToDockTime).Max()
                       ?? schedules.SelectMany(s => s.DeliveryItems ?? Enumerable.Empty<DeliveryItem>()).Where(di => di.PrepScanTime.HasValue || di.UpdatedDate.HasValue).Select(di => di.PrepScanTime ?? di.UpdatedDate).Max()
                       ?? schedules.SelectMany(s => s.PreparationRecords ?? Enumerable.Empty<PreparationRecord>()).Select(pr => (DateTime?)pr.CreatedDate).Max())
                    : (DateTime?)null;

                var isDelayPrepare = false;
                DateTime effectiveTargetPrep;
                if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                    effectiveTargetPrep = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                else if (dockInTimeTarget.HasValue)
                    effectiveTargetPrep = dockInTimeTarget.Value;
                else
                    effectiveTargetPrep = planEndTime;

                if (isKanbanFullyPrepared || isPrepared)
                {
                    if (groupMaxReadyTime.HasValue)
                    {
                        if (groupMaxReadyTime.Value > effectiveTargetPrep && (stdPMinutes > 0 || dockInTimeTarget.HasValue))
                        {
                            isDelayPrepare = true;
                        }
                    }
                    else
                    {
                        isDelayPrepare = false;
                    }
                }
                else
                {
                    if (effectiveNow > effectiveTargetPrep && (stdPMinutes > 0 || dockInTimeTarget.HasValue))
                    {
                        isDelayPrepare = true;
                    }
                }
                if (isCarryOverSched && !isCompleted && !isKanbanFullyPrepared)
                {
                    isDelayPrepare = true;
                }

                var isPickupLate = false;
                var planBaseDate = item.CreatedDate;
                if (pickupTimeTarget.HasValue) {
                    var pickupEndTarget = pickupTimeTarget.Value.AddMinutes(20);
                    var actArrival = item.ActualPickupTime ?? item.ActualStartTime ?? schedules.Where(s => s.ActualPickupTime.HasValue || s.ActualStartTime.HasValue).Select(s => s.ActualPickupTime ?? s.ActualStartTime).FirstOrDefault();
                    var actDeparture = item.ActualEndTime ?? schedules.Where(s => s.ActualEndTime.HasValue).Select(s => s.ActualEndTime).FirstOrDefault();
                    if (actDeparture.HasValue) {
                        var actualDep = actDeparture.Value;
                        if (planBaseDate > actualDep && planBaseDate > pickupEndTarget) actualDep = planBaseDate;
                        if ((actualDep - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                    else if (actArrival.HasValue) {
                        var actualArr = actArrival.Value;
                        if (planBaseDate > actualArr && planBaseDate > pickupEndTarget) actualArr = planBaseDate;
                        if ((effectiveNow - pickupEndTarget).TotalMinutes > 0 || (actualArr - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                    else {
                        if ((effectiveNow - pickupEndTarget).TotalMinutes > 0) isPickupLate = true;
                    }
                }

                var isDeliveryLate = false;
                if (item.ActualEndTime.HasValue && item.ETD.HasValue) {
                    var actualE = item.ActualEndTime.Value;
                    if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                    var etdForPunctuality = item.ETD.Value;
                    if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                    if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                }

                var isDockDelay = false;
                if (item.ActualEnterDockTime.HasValue && dockInTimeTarget.HasValue) {
                    var actualD = item.ActualEnterDockTime.Value;
                    if (planBaseDate > actualD && planBaseDate > dockInTimeTarget.Value) actualD = planBaseDate;
                    if ((actualD - dockInTimeTarget.Value).TotalMinutes > 0) isDockDelay = true;
                }

                var startPrepDT_Force = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                if (isH1Shift || isTodayH1 || isOvernightPrep)
                {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    startPrepDT_Force = startPrepDT_Force.AddDays(-h1ShiftDays);
                }
                bool isPickupReached_Force = isCarryOverSched || (pickupTimeTarget.HasValue && pickupTimeTarget.Value <= effectiveNow);
                bool isDockReached_Force   = isCarryOverSched || (dockInTimeTarget.HasValue && dockInTimeTarget.Value <= effectiveNow);
                bool isPrepReached_Force   = isCarryOverSched || (planEndTime <= effectiveNow);
                bool isPrepStarted_Force   = isCarryOverSched || (startPMinutes > 0 && effectiveNow >= startPrepDT_Force);

                if (isPickupReached_Force)
                {
                    forceCompletedCount++;
                    forceOnTimeLoadingCount++;
                    forceOnTimeDeliveryCount++;
                }
                else if (isPrepReached_Force || isPrepStarted_Force)
                {
                    forceInProgressCount++;
                }
                else
                {
                    forceWaitingProsesCount++;
                }

                if (isPrepReached_Force) forceOnTimePrepareCount++;
                if (isDockReached_Force) forceOnTimeDockInCount++;

                if (forceOnTimeMode)
                {
                    isDelayPrepare = false;
                    isPickupLate = false;
                    isDelayPickup = false;
                    isDeliveryLate = false;
                    isDelayDelivery = false;
                    isDockDelay = false;
                    isDelayDockIn = false;
                    isDelayed = false;
                }

                var actDockTimeH2 = item.ActualEnterDockTime ?? schedules.Where(s => s.ActualEnterDockTime.HasValue).Select(s => s.ActualEnterDockTime).FirstOrDefault();
                DateTime? actArrivalH2 = null;
                DateTime? actDepartureH2 = null;
                if (actDockTimeH2.HasValue)
                {
                    actArrivalH2 = item.ActualPickupTime ?? item.ActualStartTime ?? schedules.Where(s => s.ActualPickupTime.HasValue || s.ActualStartTime.HasValue).Select(s => s.ActualPickupTime ?? s.ActualStartTime).FirstOrDefault();
                    if (actArrivalH2.HasValue && actArrivalH2.Value < actDockTimeH2.Value.AddMinutes(-30))
                    {
                        actArrivalH2 = null;
                    }

                    actDepartureH2 = item.ActualEndTime ?? schedules.Where(s => s.ActualEndTime.HasValue).Select(s => s.ActualEndTime).FirstOrDefault();
                    if (actDepartureH2.HasValue && actDepartureH2.Value < actDockTimeH2.Value)
                    {
                        actDepartureH2 = null;
                    }

                    if (actDepartureH2.HasValue && !actArrivalH2.HasValue)
                    {
                        actArrivalH2 = actDepartureH2;
                    }
                }

                bool isDeparted = actDockTimeH2.HasValue && actDepartureH2.HasValue;
                bool isPastClosedGroup = isCarryOverSched && isDeparted && isKanbanFullyPrepared;

                if (!isPastClosedGroup)
                {
                    // Hanya hitung sebagai "Terlambat Prepare" jika saat ini MASIH BELUM selesai prepare dan bukan mode manipulasi
                    if (!forceOnTimeMode && isDelayPrepare && !isPrepared && !isCompleted) delayPrepareCount++;

                    bool hasPickupBadge = false;
                    if (!forceOnTimeMode && !isCompleted)
                    {
                        if (actArrivalH2.HasValue || actDepartureH2.HasValue)
                        {
                            hasPickupBadge = isPickupLate;
                        }
                        else
                        {
                            hasPickupBadge = isDelayPickup;
                        }
                    }
                    if (hasPickupBadge) delayPickupCount++;

                    if (isDeparted)
                    {
                        if (isKanbanFullyPrepared)
                        {
                            completedCount++;
                        }
                        else
                        {
                            shortageCount++;
                        }
                    }
                    else if (totalKanbanActual > 0 || isKanbanFullyPrepared || schedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared"))
                    {
                        inProgressCount++;
                    }

                    DateTime? actDelivDT = isDeparted ? actDepartureH2 : (DateTime?)null;
                    if (!forceOnTimeMode && actDelivDT.HasValue)
                    {
                        var endPickupTarget = pickupTimeTarget.HasValue ? pickupTimeTarget.Value.AddMinutes(20) : (DateTime?)null;
                        if (endPickupTarget.HasValue && actDelivDT.Value > endPickupTarget.Value)
                        {
                            delayDeliveryCount++;
                        }
                        else
                        {
                            onTimeDeliveryCount++;
                        }
                    }
                    else if (!forceOnTimeMode && !isCompleted && (isDelayDelivery || isCarryOverSched))
                    {
                        delayDeliveryCount++;
                    }
                    
                    bool hasDockBadge = false;
                    if (!forceOnTimeMode && !isCompleted)
                    {
                        if (item.ActualEnterDockTime.HasValue)
                        {
                            hasDockBadge = isDockDelay;
                        }
                        else
                        {
                            hasDockBadge = isDelayDockIn || isDelayed;
                        }
                    }
                    if (hasDockBadge) delayDockInCount++;
                    
                    if (isPrepared && !isCompleted) preparedCount++;

                    bool hasOnTimePrepareBadge = (isKanbanFullyPrepared || isPrepared || (groupMaxReadyTime.HasValue && isCompleted)) && !isDelayPrepare;
                    if (hasOnTimePrepareBadge) onTimePrepareCount++;

                    bool hasOnTimeDockInBadge = item.ActualEnterDockTime.HasValue && !isDockDelay;
                    if (hasOnTimeDockInBadge) onTimeDockInCount++;

                    bool hasOnTimeLoadingBadge = actArrivalH2.HasValue && !isPickupLate;
                    if (hasOnTimeLoadingBadge) onTimeLoadingCount++;

                    if (totalKanbanActual > 0)
                    {
                        sudahProsesAcc++;
                    }
                    else
                    {
                        waitingProsesAcc++;
                    }
                }
                else
                {
                    pastClosedGroupsCount++;
                }
            }

            if (forceOnTimeMode)
            {
                delayPickupCount = 0;
                delayDeliveryCount = 0;
                delayDockInCount = 0;
                delayPrepareCount = 0;
                shortageCount = 0;
                completedCount = forceCompletedCount;
                inProgressCount = forceInProgressCount;
                onTimePrepareCount = forceOnTimePrepareCount;
                onTimeDockInCount = forceOnTimeDockInCount;
                onTimeLoadingCount = forceOnTimeLoadingCount;
                onTimeDeliveryCount = forceOnTimeDeliveryCount;
                sudahProsesAcc = forceCompletedCount + forceInProgressCount;
                waitingProsesAcc = forceWaitingProsesCount;
            }

            int totalTodayCount = Math.Max(0, groupedSchedules.Count - pastClosedGroupsCount);
            int sudahProses = sudahProsesAcc;
            int waitingProses = waitingProsesAcc;

            int onTimePrepare = onTimePrepareCount;
            if (onTimePrepare < 0) onTimePrepare = 0;

            int onTimeDockIn = onTimeDockInCount;
            if (onTimeDockIn < 0) onTimeDockIn = 0;

            int onTimeLoading = onTimeLoadingCount;
            if (onTimeLoading < 0) onTimeLoading = 0;

            int onTimeDelivery = onTimeDeliveryCount;
            if (onTimeDelivery < 0) onTimeDelivery = 0;

            // ShortageCount dihitung berdasarkan yang belum closed dan qty kanban belum penuh
            if (shortageCount < 0) shortageCount = 0;

            return Json(new
            {
                todaySchedules = totalTodayCount,
                sudahProsesCount = sudahProses,
                waitingProsesCount = waitingProses,
                onTimePrepareCount = onTimePrepare,
                delayPrepareCount,
                onTimeDockInCount = onTimeDockIn,
                notArrivedCount = delayDockInCount,
                delayDockInCount = delayDockInCount,
                onTimeLoadingCount = onTimeLoading,
                delayPickupCount,
                onTimeDeliveryCount = onTimeDelivery,
                delayDeliveryCount,
                completedCount,
                shortageCount,
                shouldBeDeliveredCount,
                inProgressCount,
                preparedCount,
                forceOnTimeMode,
                timestamp = now.ToString("HH:mm")
            });
        }

        [HttpGet]
        public async Task<IActionResult> DashboardDetailKanban(DateTime? date)
        {
            var targetDate = date ?? DateTime.Today;

            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled" && s.ScheduledDate.Date == targetDate.Date)
                .ToListAsync();

            var groupedSchedules = allSchedules
                .GroupBy(s => new {
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper()
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    DisplayStatus = g.All(s => s.ActualEndTime.HasValue) ? "Completed" : 
                                   (g.Any(s => s.ActualStartTime.HasValue || s.ActualEnterDockTime.HasValue || s.PreparationStatus == "In Progress") ? "In Progress" : "Scheduled")
                })
                .ToList();

            var viewModel = new KanbanDashboardViewModel
            {
                SelectedDate = targetDate,
                TotalJadwal = groupedSchedules.Count,
                SudahDelivery = groupedSchedules.Count(g => g.DisplayStatus == "Completed"),
                Groups = new List<KanbanGroupRow>()
            };

            int sumTargetAll = 0;
            int sumActAll = 0;
            int sumMinusLines = 0;

            foreach (var g in groupedSchedules)
            {
                var manifest = (g.First.ScheduleNumber ?? "").Contains("/")
                        ? (g.First.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (g.First.ScheduleNumber ?? "").Trim().ToUpper();

                var row = new KanbanGroupRow
                {
                    Manifest = manifest,
                    Dock = g.First.Area ?? g.First.Customer?.Docking ?? "",
                    Cycle = g.First.Cycle ?? "",
                    IsCompleted = g.DisplayStatus == "Completed"
                };

                // Aggregate items within the group
                var allItems = g.Schedules.SelectMany(s => s.DeliveryItems ?? new List<DeliveryItem>())
                                        .Where(di => di.Item != null)
                                        .ToList();

                var groupedItems = allItems.GroupBy(di => new { di.Item!.ItemCode, di.Item!.ItemName })
                                           .Select(ig => new KanbanItemRow
                                           {
                                               VIN = ig.Key.ItemCode ?? "",
                                               ItemName = ig.Key.ItemName ?? "",
                                               TargetKanban = (int)ig.Sum(x => x.Quantity),
                                               ActKanban = (int)ig.Sum(x => x.ActualQuantity ?? 0)
                                           })
                                           .ToList();

                row.Items = groupedItems;
                row.GroupTargetKanban = groupedItems.Sum(x => x.TargetKanban);
                row.GroupActKanban = groupedItems.Sum(x => x.ActKanban);

                sumTargetAll += row.GroupTargetKanban;
                sumActAll += row.GroupActKanban;
                sumMinusLines += groupedItems.Count(x => x.Variance > 0);

                viewModel.Groups.Add(row);
            }

            viewModel.TotalKanban = sumTargetAll;
            viewModel.TotalActKanban = sumActAll;
            viewModel.KanbanMinus = sumMinusLines;

            return View(viewModel);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var feature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            if (feature?.Error != null)
            {
                try
                {
                    System.IO.File.AppendAllText(@"C:\DeliveryControl\DeliveryControl\error_log.txt", 
                        $"{DateTime.Now}: {feature.Error.ToString()}\n\n");
                }
                catch { }
            }
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    
        public static bool IsDelayPrepareCommonStatic(IEnumerable<DeliverySchedule> schedules, DateTime effectiveNow, DateTime targetDate)
        {
            var item = schedules.First();
            var today = targetDate.Date;
            var daysAhead = (item.ScheduledDate.Date - today).Days;
            var isFutureSchedule = daysAhead > 0;
            var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
            var isH1Shift = isFutureSchedule &&
                            (daysAhead == 1 ||
                             (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                             (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
            var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today && item.PickupTime.HasValue && pickupHour < 12;
            var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
            var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
            var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
            var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

            var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);
            var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);
            if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
            if (isH1Shift || isTodayH1 || isOvernightPrep) {
                var h1ShiftDays = isH1Shift ? daysAhead : 1;
                planEndTime = planEndTime.AddDays(-h1ShiftDays);
            }

            var dockInTimeTarget = item.EnterDockTime;
            var overBumpCap = item.ScheduledDate.Date.AddDays(1);
            while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap)
                dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                
            var underBumpCap = item.ScheduledDate.Date;
            if (isH1Shift || isTodayH1 || isOvernightPrep) {
                var h1ShiftDays = isH1Shift ? daysAhead : 1;
                underBumpCap = underBumpCap.AddDays(-h1ShiftDays);
            }
            while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date < underBumpCap)
                dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);

            if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue)
            {
                var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                if (dockInTimeTarget.Value > pickupToday)
                    dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
            }

            if (item.ScheduledDate.Date >= today)
            {
                var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                }
                if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2)
                    dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
            }

            var effectiveTarget = (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                ? (dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime)
                : (dockInTimeTarget ?? planEndTime);

            var groupTotalTarget = schedules.Sum(s => s.TotalTargetQuantity);
            var groupTotalActual = schedules.Sum(s => s.TotalActualQuantity);
            var isKanbanFullyPrepared = groupTotalTarget > 0 && groupTotalActual >= groupTotalTarget;
            var isPrepared = isKanbanFullyPrepared || schedules.All(s => s.PreparationStatus == "Prepared" || s.DeliveryItems == null || !s.DeliveryItems.Any());

            bool isDelayPrepare = false;
            var displayStatus = schedules.All(s => s.ActualEndTime.HasValue || s.Status == "Completed") ? "Completed" : "Not Started";
            if (displayStatus == "Completed") return false;

            if (item.ScheduledDate.Date < targetDate.Date) {
                if (!isKanbanFullyPrepared) isDelayPrepare = true;
            } else if (!isPrepared) {
                if (effectiveNow > effectiveTarget) isDelayPrepare = true;
            } else {
                var maxReady = schedules.Where(x => x.ReadyToDockTime.HasValue).Select(x => (DateTime?)x.ReadyToDockTime.Value).Max();
                if (maxReady.HasValue && maxReady.Value > effectiveTarget) isDelayPrepare = true;
            }
            return isDelayPrepare;
        }
}
}
