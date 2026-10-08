import re

file_path = "c:\\DeliveryControl_backup1\\DeliveryControl\\Controllers\\HomeController.cs"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Replace tableGroups grouping logic
old_grouping = """var tableGroups = todaySchedules
                .GroupBy(s => new {
                    Area = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    ScheduleNumber = (s.ScheduleNumber ?? "").Contains("/")
                        ? (s.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper()
                        : (s.ScheduleNumber ?? "").Trim().ToUpper()
                });"""

new_grouping = """var tableGroups = todaySchedules
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                });"""

new_content = content.replace(old_grouping, new_grouping)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(new_content)
print("done3")
