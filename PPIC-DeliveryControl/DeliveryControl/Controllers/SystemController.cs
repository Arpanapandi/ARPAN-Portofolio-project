using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    [AuthorizeRoles("Super Admin", "Admin")]
    public class SystemController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SystemController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Clear Data Dashboard: Jadwal, Item, Snapshots, dan Activity Logs
        // Menjaga riwayat scan tetap ada dengan cara memutus link ScheduleId & ItemId.
        public async Task<IActionResult> ClearTransactions()
        {
            try
            {
                // 1. Putus hubungan agar riwayat scan (Pulling & Prep) tidak terhapus (Cascade)
                await _context.Database.ExecuteSqlRawAsync("UPDATE PreparationRecords SET ScheduleId = NULL");
                await _context.Database.ExecuteSqlRawAsync("UPDATE PullingRecords SET ItemId = NULL");

                // 2. Clear Data Master & Transaksi Aktif
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM ActivityLogs");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockSnapshots");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM DeliveryItems");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM DeliverySchedules");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Items");

                var connStr = _context.Database.GetConnectionString() ?? "";
                if (!connStr.Contains("Server=") && !connStr.Contains("Database="))
                    await _context.Database.ExecuteSqlRawAsync("VACUUM");

                TempData["SuccessMessage"] = "Data Dashboard (Jadwal, Item Master, Log) telah dikosongkan. Riwayat Scan Pulling & Preparation Aman.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Gagal mengosongkan data: " + ex.Message;
            }
            
            return RedirectToAction("Index", "Home");
        }
        public async Task<IActionResult> ConsolidateSchedules()
        {
            var activeSchedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Include(s => s.PreparationRecords)
                .Where(s => s.Status != "Completed" && s.Status != "Cancelled")
                .ToListAsync();

            // Grouping logic: Manifest (extracted from number before /), Customer, Route, Cycle, Area
            var grouped = activeSchedules
                .GroupBy(s => new {
                    Manifest = (s.ScheduleNumber ?? "").Split('/')[0].Split('-')[0] == "SCH" ? "" : (s.ScheduleNumber ?? "").Split('/')[0],
                    s.CustomerId,
                    Route = s.Route ?? "",
                    Cycle = s.Cycle ?? "",
                    Area = s.Area ?? ""
                })
                .Where(g => g.Count() > 1);

            int mergedCount = 0;

            foreach (var group in grouped)
            {
                var target = group.OrderBy(s => s.CreatedDate).First();
                var others = group.Where(s => s.ScheduleId != target.ScheduleId).ToList();
 
                // Step 1: Consolidate items WITHIN target itself first
                var targetGroups = target.DeliveryItems
                    .GroupBy(di => di.ItemId)
                    .Where(g => g.Count() > 1)
                    .ToList();
                foreach (var tg in targetGroups)
                {
                    var main = tg.First();
                    foreach (var redundant in tg.Skip(1))
                    {
                        main.Quantity += redundant.Quantity;
                        main.ActualQuantity += redundant.ActualQuantity;
                        _context.DeliveryItems.Remove(redundant);
                    }
                }

                // Step 2: Move and merge items from others
                foreach (var source in others)
                {
                    foreach (var item in source.DeliveryItems.ToList())
                    {
                        var existing = target.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                        if (existing != null)
                        {
                            existing.Quantity += item.Quantity;
                            existing.ActualQuantity += item.ActualQuantity;
                            _context.DeliveryItems.Remove(item);
                        }
                        else
                        {
                            item.ScheduleId = target.ScheduleId;
                            target.DeliveryItems.Add(item);
                        }
                    }

                    // Move PreparationRecords
                    foreach (var rec in source.PreparationRecords.ToList())
                    {
                        rec.ScheduleId = target.ScheduleId;
                    }

                    _context.DeliverySchedules.Remove(source);
                    mergedCount++;
                }

                target.TotalTargetQuantity = (decimal)target.DeliveryItems.Sum(di => di.Quantity);
                target.TotalActualQuantity = (decimal)target.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                target.UpdatedDate = DateTime.Now;
                target.UpdatedBy = "SystemConsolidation";
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Berhasil menggabungkan {mergedCount} kartu duplikat.";
            return RedirectToAction("Index", "Home");
        }
    }
}
