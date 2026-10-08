using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;

namespace CheckLogic {
    class Program2 {
        static async Task Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var _context = new ApplicationDbContext(options);
            
            var allItems = await _context.Items.Where(i => i.IsActive && !i.IsDeleted).ToListAsync();
            Console.WriteLine($"Total items: {allItems.Count}");
            try {
                int count = 0;
                foreach (var dbItem in allItems) {
                    var itemTag = !string.IsNullOrWhiteSpace(dbItem.VIN) ? dbItem.VIN : dbItem.ItemCode;
                    var unlistedPulling = await _context.PullingRecords
                                    .Where(p => p.Tag == itemTag && p.Remark != "Mismatch" && (p.AdjustNote ?? "") != "Zero Stock")
                                    .ToListAsync();
                    count++;
                }
                Console.WriteLine($"Finished {count} loops successfully.");
            } catch (Exception ex) {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
