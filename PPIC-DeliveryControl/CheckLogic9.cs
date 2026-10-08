using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using System.Collections.Generic;
using System.IO;

namespace CheckLogic {
    class Program9 {
        static async Task Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var _context = new ApplicationDbContext(options);
            
            var today = DateTime.Today;
            
            var allSnapshotsRaw = await _context.StockSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate.Date == today)
                .ToListAsync();
                
            var todaySnapshots = allSnapshotsRaw
                .GroupBy(s => s.ItemCode)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();
                
            var activeVinSet = await _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted && i.IsActive && (i.StatusItem == null || i.StatusItem.Trim().ToLower() != "no order"))
                .Select(i => (i.VIN ?? "").Trim().ToUpper())
                .ToListAsync();
            var validVins = activeVinSet.Where(v => v != "").ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            var todayAllActive = todaySnapshots
                .Where(s => validVins.Contains(s.ItemCode ?? ""))
                .ToList();
                
            var exactly1_5 = todayAllActive.Where(s => s.DaysCoverage == 1.5m).ToList();
            var exactly1_0 = todayAllActive.Where(s => s.DaysCoverage == 1.0m).ToList();
            var exactly0_5 = todayAllActive.Where(s => s.DaysCoverage == 0.5m).ToList();
            
            Console.WriteLine($"Items exactly 1.5: {exactly1_5.Count}");
            foreach(var item in exactly1_5) Console.WriteLine($" - {item.ItemCode}");
            
            Console.WriteLine($"Items exactly 1.0: {exactly1_0.Count}");
            foreach(var item in exactly1_0) Console.WriteLine($" - {item.ItemCode}");
            
            Console.WriteLine($"Items exactly 0.5: {exactly0_5.Count}");
            foreach(var item in exactly0_5) Console.WriteLine($" - {item.ItemCode}");
        }
    }
}
