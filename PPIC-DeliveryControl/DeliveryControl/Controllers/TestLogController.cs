using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Services;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller temporary untuk test Activity Log
    /// </summary>
    public class TestLogController : Controller
    {
        private readonly ActivityLogService _logService;

        public TestLogController(ActivityLogService logService)
        {
            _logService = logService;
        }

        // GET: TestLog/CreateTestLog
        public async Task<IActionResult> CreateTestLog()
        {
            try
            {
                await _logService.LogActivity(
                    "Test",
                    "TestAction",
                    "TEST-001",
                    1,
                    "This is a test log to verify Activity Log is working",
                    performedBy: "TestUser"
                );

                return Content("Test log created successfully! Check ActivityLogs table or /ActivityLogs/Index");
            }
            catch (Exception ex)
            {
                return Content($"Error creating test log: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
            }
        }

        // GET: TestLog/FixActualQuantity
        public async Task<IActionResult> FixActualQuantity([FromServices] DeliveryControl.Data.ApplicationDbContext context)
        {
            try
            {
                var schedules = await context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                        .ThenInclude(di => di.Item)
                    .ToListAsync();

                int updatedCount = 0;
                foreach (var schedule in schedules)
                {
                    foreach (var dItem in schedule.DeliveryItems)
                    {
                        if (dItem.Item == null) continue;
                        
                        var normalizedTag = DeliveryControl.Helpers.VinHelper.Normalize(dItem.Item.VIN);
                        int qpc = (dItem.Item.QtyLot != null && dItem.Item.QtyLot > 0) ? dItem.Item.QtyLot.Value : 1;

                        int scanCount = await context.PreparationRecords
                            .CountAsync(p => p.ScheduleId == schedule.ScheduleId &&
                                             p.Tag == normalizedTag &&
                                             p.Remark == "Match");

                        var targetKanban = (int)Math.Ceiling((double)dItem.Quantity / qpc);
                        if (scanCount > targetKanban) scanCount = targetKanban;

                        decimal newActualQty = Math.Min((decimal)scanCount * qpc, dItem.Quantity);

                        if (dItem.ActualQuantity != newActualQty)
                        {
                            dItem.ActualQuantity = newActualQty;
                            dItem.IsCompleted = dItem.ActualQuantity >= dItem.Quantity;
                            updatedCount++;
                        }
                    }
                }
                
                await context.SaveChangesAsync();

                return Content($"Fix applied successfully! Updated {updatedCount} delivery items.");
            }
            catch (Exception ex)
            {
                return Content($"Error fixing data: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
            }
        }
    }
}

