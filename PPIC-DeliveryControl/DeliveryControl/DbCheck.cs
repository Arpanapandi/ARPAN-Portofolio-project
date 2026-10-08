using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;

namespace DbCheck {
    class Program {
        static void Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var db = new ApplicationDbContext(options);
            
            var today = DateTime.Today;
            var snaps = db.StockSnapshots.Where(s => s.SnapshotDate == today).ToList();
            
            var under05 = snaps.Count(s => s.DaysCoverage < 0.5m);
            var under1 = snaps.Count(s => s.DaysCoverage < 1.0m);
            var under15 = snaps.Count(s => s.DaysCoverage < 1.5m);
            
            Console.WriteLine($"System snaps: Total={snaps.Count}, <0.5={under05}, <1.0={under1}, <1.5={under15}");
            
            var activeItems = db.Items.Where(i => i.IsActive && i.StatusItem != "No Order").ToList();
            Console.WriteLine($"System Active Items: {activeItems.Count}");
            
            var duplicates = new[] { "HN048A", "HN0390", "TA0180" };
            foreach(var d in duplicates) {
                var s = snaps.FirstOrDefault(x => x.ItemCode == d || x.ItemName == d);
                Console.WriteLine($"Duplicate {d} DaysCoverage: {s?.DaysCoverage}");
            }
        }
    }
}
