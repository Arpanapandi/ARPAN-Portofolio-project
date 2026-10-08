import re

file_path = "c:\\DeliveryControl_backup1\\DeliveryControl\\Controllers\\HomeController.cs"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Replace indicator counting logic to exclude tomorrow's schedules
old_logic = """if (isDelayPrepare) delayPrepareCount++;
                if (isDelayPickup) delayPickupCount++;
                
                var showDelayDelivery = isDelayDelivery || 
                    (isCarryOverSched && !isCompleted) || 
                    (isTodayH1 && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value) || 
                    (isOvernightPrep && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                
                if (showDelayDelivery) delayDeliveryCount++;
                if (isDelayDockIn) delayDockInCount++;"""

new_logic = """if (item.ScheduledDate.Date <= today.Date) 
                {
                    if (isDelayPrepare) delayPrepareCount++;
                    if (isDelayPickup) delayPickupCount++;
                    
                    var showDelayDelivery = isDelayDelivery || 
                        (isCarryOverSched && !isCompleted) || 
                        (isTodayH1 && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value) || 
                        (isOvernightPrep && !isCompleted && !item.ActualEndTime.HasValue && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                    
                    if (showDelayDelivery) delayDeliveryCount++;
                    if (isDelayDockIn) delayDockInCount++;
                }"""

new_content = content.replace(old_logic, new_logic)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(new_content)
print("done4")
