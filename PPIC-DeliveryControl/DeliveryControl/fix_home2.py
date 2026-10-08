import re

file_path = "c:\\DeliveryControl_backup1\\DeliveryControl\\Controllers\\HomeController.cs"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Replace EndPrepareTime with StdPrepareTime and add stdPMinutes definition
new_method = """public async Task<IActionResult> GetDashboardStatistics(DateTime? date = null)
        {
            var now = DateTime.Now;
            var today = date?.Date ?? DateTime.Today;
            var isViewingPast = date.HasValue && date.Value.Date < DateTime.Today;
            var effectiveNow = isViewingPast ? today.AddDays(1).AddSeconds(-1) : now;

            var allSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled")
                .ToListAsync();

            var tomorrowDate = today.AddDays(1);
            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today ||
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue) ||
                            (s.ScheduledDate.Date == tomorrowDate && s.Status != "Completed" && !s.ActualEndTime.HasValue &&
                             s.PickupTime.HasValue && s.PickupTime.Value.Hour < 12 &&
                             (s.StartPrepareTime > 720 || (s.Customer != null && s.Customer.StartPrepareTime > 720))))
                .ToList();

            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })
                .Select(g => new { 
                    Schedules = g.ToList(),
                    First = g.First(),
                    IsCarryOver = g.All(s => s.ScheduledDate.Date < today),
                    DisplayStatus = g.All(s => GetDisplayStatus(s) == "Completed") ? "Completed" : 
                                   (g.Any(s => GetDisplayStatus(s) == "In Progress") ? "In Progress" : "Scheduled")
                })
                .ToList();

            var completedCount = groupedSchedules.Count(g => g.DisplayStatus == "Completed");
            var inProgressCount = groupedSchedules.Count(g => g.DisplayStatus == "In Progress");
            var preparedCount = groupedSchedules.Count(g => g.Schedules.All(s => s.PreparationStatus == "Prepared") && g.DisplayStatus != "Completed");
            
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
            int delayDeliveryCount = 0;
            int delayDockInCount = 0;

            var tableGroups = todaySchedules
                .GroupBy(s => new {
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    ScheduleNumber = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper()
                });

            foreach (var g in tableGroups)
            {
                var schedules = g.ToList();
                var item = schedules.First();
                
                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                var isNextDaySchedule = item.ScheduledDate.Date == today.AddDays(1);
                var isH1Shift = isNextDaySchedule && item.PickupTime.HasValue && pickupHour < 12;
                var isTodayH1 = !isNextDaySchedule && item.ScheduledDate.Date == today.Date && item.PickupTime.HasValue && pickupHour < 12;
                
                var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                var isOvernightPrep = !isH1Shift && !isTodayH1 && !isNextDaySchedule && item.ScheduledDate.Date == today.Date && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                var isPastCompletedGroup = item.ScheduledDate.Date < today.Date && schedules.All(s => s.ActualEndTime.HasValue || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)));

                if (isPastCompletedGroup) continue;
                if (isNextDaySchedule && !isH1Shift) continue;

                var dockInTimeTarget = item.EnterDockTime;
                var pickupTimeTarget = item.PickupTime;
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                if (isH1Shift || isTodayH1 || isOvernightPrep) { planEndTime = planEndTime.AddDays(-1); }

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
                    if (isH1Shift || isTodayH1 || isOvernightPrep) startPrepDT2 = startPrepDT2.AddDays(-1);
                    if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                    if (pickupTimeTarget.HasValue && !isTodayH1) {
                        var anchor = dockInTimeTarget ?? planEndTime;
                        if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                    }
                }

                var isCompleted = schedules.All(s => s.ActualEndTime.HasValue || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)));

                var isDelayPickup = false;
                if (!item.ActualPickupTime.HasValue && pickupTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= pickupTimeTarget.Value) isDelayPickup = true;
                }

                var isDelayDelivery = false;
                if (!item.ActualEndTime.HasValue && item.ETD.HasValue && !isCompleted) {
                    var etdTarget = item.ETD.Value;
                    if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                    if (effectiveNow >= etdTarget) isDelayDelivery = true;
                }
                
                var isDelayDockIn = false;
                if (!item.ActualEnterDockTime.HasValue && dockInTimeTarget.HasValue && !isCompleted) {
                    if (effectiveNow >= dockInTimeTarget.Value) isDelayDockIn = true;
                }

                var isPrepared = schedules.All(s => string.Equals((s.PreparationStatus ?? "").Trim(), "Prepared", StringComparison.OrdinalIgnoreCase) || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase));
                var isDelayPrepare = false;
                if (!isCompleted) {
                    if (!isPrepared) {
                        var endPrepRefTime = stdPMinutes > 0 ? (DateTime?)planEndTime : (dockInTimeTarget.HasValue ? dockInTimeTarget : (DateTime?)null);
                        if (endPrepRefTime.HasValue && effectiveNow > endPrepRefTime.Value) isDelayPrepare = true;
                    } else {
                        var groupMaxReadyTime = schedules.Where(s => s.ReadyToDockTime.HasValue).Select(s => (DateTime?)s.ReadyToDockTime).Max();
                        if (groupMaxReadyTime.HasValue) {
                            var endPrepRefTime = stdPMinutes > 0 ? (DateTime?)planEndTime : (dockInTimeTarget.HasValue ? dockInTimeTarget : (DateTime?)null);
                            if (endPrepRefTime.HasValue && groupMaxReadyTime.Value > endPrepRefTime.Value) isDelayPrepare = true;
                        } else {
                            var endPrepRefTime = stdPMinutes > 0 ? (DateTime?)planEndTime : (dockInTimeTarget.HasValue ? dockInTimeTarget : (DateTime?)null);
                            if (endPrepRefTime.HasValue && effectiveNow > endPrepRefTime.Value) isDelayPrepare = true;
                        }
                    }
                }

                if (isDelayPrepare) delayPrepareCount++;
                if (isDelayPickup) delayPickupCount++;
                
                var showDelayDelivery = isDelayDelivery || 
                    (isCarryOverSched && !isCompleted) || 
                    (isTodayH1 && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value) || 
                    (isOvernightPrep && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                
                if (showDelayDelivery) delayDeliveryCount++;
                if (isDelayDockIn) delayDockInCount++;
            }

            return Json(new
            {
                todaySchedules = groupedSchedules.Count,
                completedCount,
                shouldBeDeliveredCount,
                inProgressCount,
                delayPickupCount,
                delayDeliveryCount,
                notArrivedCount = delayDockInCount,
                delayPrepareCount,
                preparedCount,
                timestamp = now.ToString("HH:mm")
            });
        }"""

pattern = re.compile(r'public async Task<IActionResult> GetDashboardStatistics\(DateTime\? date = null\).*?timestamp = now\.ToString\("HH:mm"\)\s*\}\);\s*\}', re.DOTALL)
new_content = pattern.sub(new_method, content)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(new_content)
print("done2")
