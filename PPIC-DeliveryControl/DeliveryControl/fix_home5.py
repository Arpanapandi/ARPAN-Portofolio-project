import re

file_path = "c:\\DeliveryControl_backup1\\DeliveryControl\\Controllers\\HomeController.cs"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# 1. Replace groupedSchedules grouping to include CustId
old_group = """            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })"""

new_group = """            var groupedSchedules = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })"""

content = content.replace(old_group, new_group)

# 2. Fix GetTableData query to include H-1 schedules
old_table = """            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today ||
                            // Carry-over: jadwal dari hari-hari sebelumnya yang belum Completed
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue))
                .OrderBy(s => (s.Status == "Completed" || s.ActualEndTime.HasValue) ? 1 : 0)
                .ThenBy(s => {
                    var sp = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
                    return sp > 0 ? sp : int.MaxValue;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.PickupTime)
                .ToList();"""

new_table = """            var todaySchedules = allSchedules
                .Where(s => s.ScheduledDate.Date == today ||
                            // Carry-over: jadwal dari hari-hari sebelumnya yang belum Completed
                            (s.ScheduledDate.Date < today && s.Status != "Completed" && !s.ActualEndTime.HasValue) ||
                            // H-1: jadwal besok yang startPrepTime jatuh malam ini (info prepare hari ini)
                            (s.ScheduledDate.Date == tomorrowDate && s.Status != "Completed" && !s.ActualEndTime.HasValue &&
                             s.PickupTime.HasValue && s.PickupTime.Value.Hour < 12 &&
                             (s.StartPrepareTime > 720 || (s.Customer != null && s.Customer.StartPrepareTime > 720))))
                .OrderBy(s => (s.Status == "Completed" || s.ActualEndTime.HasValue) ? 1 : 0)
                .ThenBy(s => s.ScheduledDate.Date)
                .ThenBy(s => {
                    var sp = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
                    return sp > 0 ? sp : int.MaxValue;
                })
                .ThenBy(s => s.PickupTime)
                .ToList();"""

content = content.replace(old_table, new_table)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
print("done5")
