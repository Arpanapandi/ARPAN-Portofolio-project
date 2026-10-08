using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using System.Collections.Generic;

namespace CheckLogic {
    class Program7 {
        static async Task Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var _context = new ApplicationDbContext(options);
            
            var today = DateTime.Today;
            
            // Replicate TrendCriticalStock logic for today
            var allSnapshotsRaw = await _context.StockSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate.Date == today)
                .ToListAsync();
                
            var activeItemRows = await _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted && i.IsActive && (i.StatusItem == null || i.StatusItem.Trim().ToLower() != "no order"))
                .Select(i => new { i.VIN, i.ItemCode, i.RackMin, i.StatusItem, i.ItemId })
                .ToListAsync();
                
            var activeVinSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in activeItemRows) {
                if (!string.IsNullOrWhiteSpace(it.VIN)) activeVinSet.Add(it.VIN.Trim().ToUpper());
                if (!string.IsNullOrWhiteSpace(it.ItemCode)) activeVinSet.Add(it.ItemCode.Trim().ToUpper());
            }
            
            var todaySnapshots = allSnapshotsRaw
                .GroupBy(s => s.ItemCode)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();
                
            var todayAllActive = todaySnapshots
                .Where(s => activeVinSet.Contains(s.ItemCode ?? ""))
                .ToList();
                
            var appBelow1_5D = todayAllActive.Where(s => s.DaysCoverage < 1.5m).ToList();
            var appBelow1D = todayAllActive.Where(s => s.DaysCoverage < 1.0m).ToList();
            var appBelow0_5D = todayAllActive.Where(s => s.DaysCoverage < 0.5m).ToList();
            
            // Now let's calculate the pure "Excel" way directly from Items and PullingRecords
            var pullingQueryAll = await _context.PullingRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch" && (r.AdjustNote ?? "") != "Opname Reduce").ToListAsync();
                
            var allItemsByVin = activeItemRows
                .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                .GroupBy(i => i.VIN!.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => (i.StatusItem ?? "").Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
                
            var inStockPieces = pullingQueryAll.Where(p => (p.AdjustNote ?? "") != "Zero Stock").ToList();
            
            var groupedByVin = inStockPieces.GroupBy(p => {
                var tag = (p.Tag ?? "").Trim().ToUpper();
                var resolved = tag != "" ? allItemsByVin.GetValueOrDefault(tag) : null;
                var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                if (vin != "") return vin;
                if (p.ItemId.HasValue) return "__ITEM_" + p.ItemId.Value;
                return "__TAG_" + (p.Tag ?? "UNKNOWN").Trim().ToUpper();
            });
            
            var excelBelow1_5D = new List<string>();
            var excelBelow1D = new List<string>();
            var excelBelow0_5D = new List<string>();
            
            var vinWithStock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach(var g in groupedByVin) {
                var vin = g.Key;
                vinWithStock.Add(vin);
                var resolved = allItemsByVin.GetValueOrDefault(vin);
                if (resolved == null) continue;
                
                var min = resolved.RackMin ?? 5;
                var stock = g.Count();
                var std = min > 0 ? (decimal)stock / min : 9999m;
                
                if (std < 1.5m) excelBelow1_5D.Add(vin);
                if (std < 1.0m) excelBelow1D.Add(vin);
                if (std < 0.5m) excelBelow0_5D.Add(vin);
            }
            
            // Depleted items
            foreach(var item in activeItemRows) {
                var vin = (item.VIN ?? "").Trim().ToUpper();
                if (!vinWithStock.Contains(vin)) {
                    var min = item.RackMin ?? 5;
                    var std = min > 0 ? 0m : 9999m;
                    if (std < 1.5m) excelBelow1_5D.Add(vin);
                    if (std < 1.0m) excelBelow1D.Add(vin);
                    if (std < 0.5m) excelBelow0_5D.Add(vin);
                }
            }
            
            Console.WriteLine($"App <1.5D: {appBelow1_5D.Count}, Excel <1.5D: {excelBelow1_5D.Count}");
            Console.WriteLine($"App <1D: {appBelow1D.Count}, Excel <1D: {excelBelow1D.Count}");
            Console.WriteLine($"App <0.5D: {appBelow0_5D.Count}, Excel <0.5D: {excelBelow0_5D.Count}");
            
            var app15 = appBelow1_5D.Select(s => s.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ex15 = excelBelow1_5D.ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            Console.WriteLine("Missing in App < 1.5D (in Excel but not App):");
            foreach(var v in ex15.Except(app15)) Console.WriteLine(v);
            
            Console.WriteLine("Extra in App < 1.5D (in App but not Excel):");
            foreach(var v in app15.Except(ex15)) Console.WriteLine(v);
            
            var app1 = appBelow1D.Select(s => s.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ex1 = excelBelow1D.ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            Console.WriteLine("Missing in App < 1D:");
            foreach(var v in ex1.Except(app1)) Console.WriteLine(v);
        }
    }
}
