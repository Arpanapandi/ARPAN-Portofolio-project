using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;

namespace CheckLogic {
    class Program5 {
        static async Task Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var _context = new ApplicationDbContext(options);
            
            var endDate = DateTime.Today;
            
            var todayAllRaw = await _context.StockSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate.Date == endDate)
                .ToListAsync();
                
            var todayAll = todayAllRaw
                .GroupBy(s => new { s.SnapshotDate.Date, s.ItemCode })
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();
                
            var activeItemRows = await _context.Items
                .Where(i => !i.IsDeleted && i.IsActive && (i.StatusItem == null || i.StatusItem.Trim().ToLower() != "no order"))
                .Select(i => new { i.VIN, i.ItemCode, i.Plant })
                .ToListAsync();
                
            var activeVins = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in activeItemRows)
            {
                if (!string.IsNullOrWhiteSpace(r.VIN))      activeVins.Add(r.VIN.Trim().ToUpper());
                if (!string.IsNullOrWhiteSpace(r.ItemCode)) activeVins.Add(r.ItemCode.Trim().ToUpper());
            }
            
            var todayAllActive = todayAll.Where(s => activeVins.Contains((s.ItemCode ?? "").Trim().ToUpper())).ToList();
            
            Console.WriteLine($"activeItemRows.Count: {activeItemRows.Count}");
            Console.WriteLine($"activeVins.Count: {activeVins.Count}");
            Console.WriteLine($"todayAll.Count: {todayAll.Count}");
            Console.WriteLine($"todayAllActive.Count: {todayAllActive.Count}");
        }
    }
}
