using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliveryControl.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            // Pastikan database sudah di-migrate
            try
            {
                // context.Database.Migrate();
            }
            catch
            {
                // Jika migration gagal, mungkin database belum ada atau sudah ada
                // Lanjutkan ke seed data
            }

            // Seed Users - Pastikan superadmin selalu ada
            if (!context.Users.Any(u => u.Username == "superadmin"))
            {
                context.Users.Add(new User
                {
                    Username = "superadmin",
                    Password = BCrypt.Net.BCrypt.HashPassword("Delivery123"), // Password: Delivery123
                    FullName = "Super Administrator",
                    Email = "superadmin@deliverycontrol.com",
                    Role = "Super Admin",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
                context.SaveChanges();
            }

            // Seed Admin default jika belum ada admin sama sekali
            if (!context.Users.Any(u => u.Username == "admin"))
            {
                context.Users.Add(new User
                {
                    Username = "admin",
                    Password = BCrypt.Net.BCrypt.HashPassword("admin123"), // Password: admin123
                    FullName = "Administrator",
                    Email = "admin@deliverycontrol.com",
                    Role = "Admin",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                });
                context.SaveChanges();
            }

            // Create table if not exists via Raw SQL (Only for SQL Server compatibility if needed)
            if (context.Database.IsSqlServer())
            {
                try {
                    context.Database.ExecuteSqlRaw(@"
                        IF OBJECT_ID('dbo.SmartImportHeaderMaps', 'U') IS NULL
                        BEGIN
                            CREATE TABLE [dbo].[SmartImportHeaderMaps] (
                                [Id] int NOT NULL IDENTITY,
                                [CustomerName] nvarchar(50) NOT NULL,
                                [TargetField] nvarchar(50) NOT NULL,
                                [HeaderKeywords] nvarchar(max) NOT NULL,
                                [CreatedDate] datetime2 NOT NULL,
                                CONSTRAINT [PK_SmartImportHeaderMaps] PRIMARY KEY ([Id])
                            )
                        END
                    ");
                } catch { /* Silent */ }
            }

            // Seed SmartImportHeaderMaps — gunakan upsert per CustomerName+TargetField
            var headerMapSeeds = new[]
            {
                // Default / Generic
                new { CustomerName = "GENERIC", TargetField = "Manifest", Keywords = "MANIFEST,DN NO,DN,DELIVERY NO,SURAT JALAN,ORDER NO" },
                new { CustomerName = "GENERIC", TargetField = "PartNo",   Keywords = "PART NO,MATERIAL NO,PART NUMBER,MATERIAL NUMBER,KODE ITEM" },
                new { CustomerName = "GENERIC", TargetField = "Qty",      Keywords = "QTY,QUANTITY,JUMLAH,TOTAL QTY,ORDER QTY,PCS,PC" },
                
                // ADM (Astra Daihatsu Motor)
                new { CustomerName = "ADM", TargetField = "Manifest", Keywords = "Order No,Manifest,DN" },
                new { CustomerName = "ADM", TargetField = "PartNo",   Keywords = "Part No,Material No" },
                new { CustomerName = "ADM", TargetField = "Qty",      Keywords = "Order (pcs),Qty (pcs),Order Qty" },

                // HINO (PT. Hino Motors Manufacturing Indonesia)
                // Kolom tabel: NO | Supply Area | Address Part | Part No. | Uniq No. | Part Name | Total Pack | Pcs/Pack | Total Qty | Remarks
                new { CustomerName = "HINO", TargetField = "PartNo",   Keywords = "Part No.,Part No,Address Part" },
                new { CustomerName = "HINO", TargetField = "Qty",      Keywords = "Total Qty,Total QTY" },
            };

            foreach (var seed in headerMapSeeds)
            {
                bool exists = context.SmartImportHeaderMaps.Any(m =>
                    m.CustomerName == seed.CustomerName && m.TargetField == seed.TargetField);
                if (!exists)
                {
                    context.SmartImportHeaderMaps.Add(new SmartImportHeaderMap
                    {
                        CustomerName   = seed.CustomerName,
                        TargetField    = seed.TargetField,
                        HeaderKeywords = seed.Keywords,
                        CreatedDate    = DateTime.Now
                    });
                }
            }
            context.SaveChanges();

        }

        public static void SeedHistoricalStockSnapshots(ApplicationDbContext context)
        {
            var today = DateTime.Today;
            
            // Check if we have snapshots for today
            var todaySnapshots = context.StockSnapshots
                .Where(s => s.SnapshotDate.Date == today)
                .ToList();

            if (!todaySnapshots.Any()) return;

            // Remove previous test snapshots
            var oldTestSnapshots = context.StockSnapshots
                .Where(s => s.SnapshotDate.Date < today && s.IsManual)
                .ToList();
            
            if (oldTestSnapshots.Any())
            {
                context.StockSnapshots.RemoveRange(oldTestSnapshots);
                context.SaveChanges();
            }

            var rng = new Random();
            var newSnapshots = new List<StockSnapshot>();

            for (int i = 1; i <= 21; i++) // 21 days history
            {
                var pastDate = today.AddDays(-i);
                
                // "Market Sentiment" per day - makes the whole plant move up or down together occasionally
                var sentiment = 0.5 + (rng.NextDouble() * 1.5); // 0.5 (bad) to 2.0 (good)

                foreach (var template in todaySnapshots)
                {
                    // Random factor for this specific item
                    // We want it to swing between 0.2x and 3.0x to cross 1.0 and 1.5 thresholds
                    var itemVolatility = 0.2 + (rng.NextDouble() * 2.8);
                    var combinedFactor = sentiment * itemVolatility;

                    // If combinedFactor is high, item is "healthy" (> 1.5D)
                    // If combinedFactor is low, item is "critical" (< 1.5D)
                    
                    var newQty = (int)Math.Max(5, Math.Round(template.StockQty * combinedFactor));
                    // Base DC today is likely low (since user is testing critical stock)
                    // We need to force some high DC values sometimes
                    var newDC = (decimal)Math.Round(0.5 + (rng.NextDouble() * 2.5), 2); // Random DC between 0.5 and 3.0
                    
                    // Occasionally make it very critical based on sentiment
                    if (sentiment < 0.8) newDC = (decimal)Math.Round(0.1 + (rng.NextDouble() * 0.9), 2);

                    string newLevel = "<1D";
                    if (newDC < 1.0m) newLevel = "<1D";
                    else if (newDC < 1.5m) newLevel = "<1.5D";
                    else if (newDC < 2.0m) newLevel = "1.5-2D";
                    else if (newDC < 3.0m) newLevel = "2-3D";
                    else newLevel = ">3D";

                    newSnapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = pastDate,
                        SnapshotTime = TimeSpan.FromHours(8),
                        CreatedAt = pastDate.AddHours(8).AddMinutes(rng.Next(0, 60)),
                        ItemCode = template.ItemCode,
                        ItemName = template.ItemName,
                        Plant = template.Plant,
                        StockQty = newQty,
                        DaysCoverage = newDC,
                        StockLevel = newLevel,
                        IsManual = true
                    });
                }
            }

            if (newSnapshots.Any())
            {
                context.StockSnapshots.AddRange(newSnapshots);
                context.SaveChanges();
            }
        }
    }
}

