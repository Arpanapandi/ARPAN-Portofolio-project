using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using System.Collections.Generic;

namespace CheckLogic {
    class Program6 {
        static async Task Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true")
                .Options;
            using var _context = new ApplicationDbContext(options);
            
            var allItems = await _context.Items.AsNoTracking().Where(i => i.IsActive && !i.IsDeleted).ToListAsync();
            var allItemsByVin = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                .GroupBy(i => i.VIN!.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

            var activeItemsNotYetInStock = allItems
                .Where(i => i.IsActive && !string.Equals(i.StatusItem, "No Order", StringComparison.OrdinalIgnoreCase))
                .ToList();
                
            var pullingQueryAll = await _context.PullingRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch" && (r.AdjustNote ?? "") != "Opname Reduce").ToListAsync();
                
            var preparationQueryAll = await _context.PreparationRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch").ToListAsync();
                
            var consumedPullingIds = new HashSet<int>();
            
            // ... omitting exact FIFO match since we just want to know if it's there
            var inStockPieces = pullingQueryAll.Where(p => 
                !consumedPullingIds.Contains(p.PullingId) && 
                (p.AdjustNote ?? "") != "Zero Stock"
            ).ToList();
            
            var groupedByVin = inStockPieces.GroupBy(p =>
            {
                var tag = (p.Tag ?? "").Trim().ToUpper();
                var resolved = tag != "" ? allItemsByVin.GetValueOrDefault(tag) : null;
                var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                if (vin != "") return vin;
                if (p.ItemId.HasValue) return "__ITEM_" + p.ItemId.Value;
                return "__TAG_" + (p.Tag ?? "UNKNOWN").Trim().ToUpper();
            });
            
            var vinWithStock = new HashSet<string>(groupedByVin.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            
            // zero stock markers
            var zeroStockMarkers = pullingQueryAll
                .Where(p => p.AdjustNote == "Zero Stock")
                .GroupBy(p => {
                    var tag = (p.Tag ?? "").Trim().ToUpper();
                    var resolved = tag != "" ? allItemsByVin.GetValueOrDefault(tag) : null;
                    return (resolved?.VIN ?? p.Tag ?? "").Trim().ToUpper();
                })
                .Select(g => g.First())
                .ToList();
                
            foreach (var m in zeroStockMarkers) {
                var tag = (m.Tag ?? "").Trim().ToUpper();
                var resolved = tag != "" ? allItemsByVin.GetValueOrDefault(tag) : null;
                if (resolved != null && !string.IsNullOrEmpty(resolved.VIN)) {
                    vinWithStock.Add(resolved.VIN.Trim().ToUpper());
                }
            }
            
            // depleted
            var depletedGroups = pullingQueryAll
                .Where(p => p.ItemId.HasValue && (p.AdjustNote ?? "") != "Zero Stock" && !(p.AdjustNote ?? "").Contains("Rack Hidden"))
                .GroupBy(p => {
                    var iId = p.ItemId!.Value;
                    var itm = allItems.FirstOrDefault(i => i.ItemId == iId);
                    return (itm?.VIN ?? "").Trim().ToUpper();
                });
                
            foreach (var group in depletedGroups) {
                var vinKey = group.Key;
                if (!string.IsNullOrEmpty(vinKey)) vinWithStock.Add(vinKey);
            }
            
            // Bagian 4
            foreach (var itemObj in activeItemsNotYetInStock) {
                var vinKey = (itemObj.VIN ?? "").Trim().ToUpper();
                if (!vinWithStock.Contains(vinKey)) {
                    vinWithStock.Add(vinKey);
                }
            }
            
            Console.WriteLine($"Total active items: {activeItemsNotYetInStock.Count}");
            Console.WriteLine($"Total unique VINs in stock details: {vinWithStock.Count}");
        }
    }
}
