using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Background service yang otomatis menjalankan stock snapshot setiap hari jam 08:00.
    /// Data snapshot disimpan ke tabel StockSnapshots dan digunakan untuk grafik Trend Critical Stock.
    /// </summary>
    public class StockSnapshotService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StockSnapshotService> _logger;

        // Cutoff time: 08:00 setiap hari
        private readonly TimeSpan _cutoffTime = new TimeSpan(8, 0, 0);

        /// <summary>
        /// Tanggal operasional: jam 00:00 - 07:59 masih dihitung tanggal kemarin
        /// </summary>
        public static DateTime GetOperationalDate(DateTime dateTime)
        {
            if (dateTime.TimeOfDay < new TimeSpan(8, 0, 0))
            {
                return dateTime.Date.AddDays(-1);
            }
            return dateTime.Date;
        }

        public StockSnapshotService(
            IServiceScopeFactory scopeFactory,
            ILogger<StockSnapshotService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("StockSnapshotService started. Hourly capture with 08:00 AM cutoff.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var nextRun = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
                var delay = nextRun - now;

                _logger.LogInformation(
                    "Next stock snapshot: {Next:dd/MM/yyyy HH:mm} (dalam {Min:F0} menit)",
                    nextRun, delay.TotalMinutes);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (!stoppingToken.IsCancellationRequested)
                    await TakeSnapshotAsync(isManual: false);
            }
        }

        /// <summary>
        /// Ambil snapshot stock saat ini untuk semua item.
        /// </summary>
        /// <param name="isManual">true = dipicu manual, false = auto per jam</param>
        public async Task<(int itemCount, string message)> TakeSnapshotAsync(bool isManual = false)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var now = DateTime.Now;
                var snapshotDate = GetOperationalDate(now);
                var snapshotTime = new TimeSpan(now.Hour, 0, 0);

                // Hindari duplikat snapshot otomatis di jam yang sama pada tanggal operasional yang sama
                if (!isManual)
                {
                    var exists = await context.StockSnapshots
                        .AnyAsync(s => s.SnapshotDate.Date == snapshotDate && s.SnapshotTime.Hours == now.Hour && !s.IsManual);
                    if (exists)
                    {
                        _logger.LogWarning("Auto-snapshot untuk jam {Hour}:00 hari ini sudah ada. Skip.", now.Hour);
                        return (0, $"Snapshot otomatis jam {now.Hour:D2}:00 hari ini sudah ada.");
                    }
                }
                else
                {
                    // Untuk manual: hapus snapshot jam yang sama pada tanggal operasional ini lalu buat ulang
                    var sameHourSnapshots = context.StockSnapshots
                        .Where(s => s.SnapshotDate.Date == snapshotDate && s.SnapshotTime.Hours == now.Hour);
                    context.StockSnapshots.RemoveRange(sameHourSnapshots);
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Snapshot jam {Hour}:00 dihapus sebelum snapshot manual baru.", now.Hour);
                }

                // == STOCK CALCULATION: IDENTIK dengan GetStockViewModel (FIFO) ==
                // PENTING: Ambil SEMUA data (tidak filter tanggal) identik Dashboard FG

                // 1. Semua Pulling (exclude Mismatch + Opname Reduce — identik dengan Dashboard)
                var pullings = await context.PullingRecords
                    .Include(p => p.Item)
                    .Where(p => p.Remark != "Mismatch" && (p.AdjustNote ?? "") != "Opname Reduce")
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PullingId)
                    .ToListAsync();

                // 2. Semua Preparation + Shopping (exclude Mismatch)
                //    Identik dengan allConsumptionAllTime di GetStockViewModelInternal
                var prepsRaw = await context.PreparationRecords
                    .Where(p => p.Remark != "Mismatch")
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PreparationId)
                    .Select(p => new { p.Tag, p.Label, p.CreatedDate, p.Plant })
                    .ToListAsync();

                var shopsRaw = await context.ShoppingRecords
                    .OrderBy(p => p.CreatedDate)
                    .Select(p => new { p.Tag, p.Label, p.CreatedDate, p.Plant })
                    .ToListAsync();

                // Gabungkan Preparation + Shopping, sort by waktu
                var preps = prepsRaw
                    .Select(p => new { p.Tag, p.Label, p.CreatedDate, p.Plant })
                    .Concat(shopsRaw.Select(s => new { s.Tag, s.Label, s.CreatedDate, s.Plant }))
                    .OrderBy(p => p.CreatedDate)
                    .Select(p => new PreparationRecord
                    {
                        Tag = p.Tag,
                        Label = p.Label,
                        CreatedDate = p.CreatedDate,
                        Plant = p.Plant
                    })
                    .ToList();

                var allItems = await context.Items.AsNoTracking()
                    .Where(i => !i.IsDeleted)
                    .ToListAsync();

                var allItemsByVin = allItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => i.VIN!.Trim().ToUpper())
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0)
                              .ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1)
                              .First(),
                        StringComparer.OrdinalIgnoreCase);

                var allItemsByCode = allItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.ItemCode))
                    .GroupBy(i => i.ItemCode!.Trim().ToUpper())
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0)
                              .ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1)
                              .First(),
                        StringComparer.OrdinalIgnoreCase);

                var activeItemsCountByVin = allItems
                    .Where(i => i.IsActive && !i.IsDeleted && !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => DeliveryControl.Helpers.VinHelper.Normalize(i.VIN))
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                // 3. FIFO matching: pre-normalize pulling candidates
                var pullingWrapperList = pullings.Select(p => (
                    Record: p,
                    NormalizedLabel: DeliveryControl.Helpers.VinHelper.Normalize(p.Label),
                    UpperLabel: (p.Label ?? "").Trim().ToUpper()
                )).ToList();

                var pullingLookup = pullingWrapperList.ToLookup(p => (p.Record.Tag ?? "").Trim().ToUpper());
                var consumedPullingIds = new HashSet<int>();

                foreach (var prep in preps)
                {
                    var prepTag   = (prep.Tag   ?? "").Trim().ToUpper();
                    var prepLabel = (prep.Label ?? "").Trim().ToUpper();
                    var candidates = pullingLookup[prepTag];

                    // Prioritas 1: Tag + Label persis (FIFO normal scan, tol 10s)
                    var match = candidates.FirstOrDefault(p =>
                        !consumedPullingIds.Contains(p.Record.PullingId) &&
                        p.UpperLabel == prepLabel &&
                        p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                    // Prioritas 2: Fallback label compatible
                    var normPrepLabel = DeliveryControl.Helpers.VinHelper.Normalize(prepLabel);
                    bool labelCompatibleMatch = !string.IsNullOrEmpty(prepLabel) && candidates.Any(p =>
                        !consumedPullingIds.Contains(p.Record.PullingId) &&
                        (p.Record.AdjustNote ?? "") != "Zero Stock" &&
                        p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                        (p.NormalizedLabel == normPrepLabel ||
                         p.NormalizedLabel.StartsWith(normPrepLabel) ||
                         normPrepLabel.StartsWith(p.NormalizedLabel)));

                    bool isUniqueVin = activeItemsCountByVin.TryGetValue(
                        DeliveryControl.Helpers.VinHelper.Normalize(prepTag), out int activeCount) && activeCount == 1;

                    if (match.Record == null)
                    {
                        if (labelCompatibleMatch)
                        {
                            match = candidates.FirstOrDefault(p =>
                                !consumedPullingIds.Contains(p.Record.PullingId) &&
                                (p.Record.AdjustNote ?? "") != "Zero Stock" &&
                                p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                                (p.NormalizedLabel == normPrepLabel ||
                                 p.NormalizedLabel.StartsWith(normPrepLabel) ||
                                 normPrepLabel.StartsWith(p.NormalizedLabel)));
                        }
                        // Fallback jika label beda: ambil pulling tertua yang tersedia
                        if (match.Record == null)
                        {
                            match = candidates.FirstOrDefault(p =>
                                !consumedPullingIds.Contains(p.Record.PullingId) &&
                                (p.Record.AdjustNote ?? "") != "Zero Stock" &&
                                p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                        }
                    }

                    if (match.Record != null)
                        consumedPullingIds.Add(match.Record.PullingId);
                }

                Item? ResolveItem(PullingRecord p)
                {
                    if (p.Item != null && !p.Item.IsDeleted) return p.Item;
                    var tag = (p.Tag ?? "").Trim().ToUpper();
                    return tag != "" ? allItemsByVin.GetValueOrDefault(tag) : null;
                }

                // 4. inStockPieces = pulling yang belum dikonsumsi
                var inStockPieces = pullings
                    .Where(p => !consumedPullingIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                ResolveItem(p) != null)
                    .ToList();

                var snapshots = new List<StockSnapshot>();

                // Helper: identik dengan IsExcludedStatus di StockController
                static bool IsExcluded(string? s) =>
                    string.Equals(s, "No Order", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "PMSP SM", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s, "After Market", StringComparison.OrdinalIgnoreCase);

                // -- BAGIAN 1: Group by Customer|VIN (identik GetStockViewModel) --
                var groupedByVin = inStockPieces.GroupBy(p =>
                {
                    var resolved = ResolveItem(p);
                    var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                    var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                    if (vin != "") return $"{cust}|{vin}";
                    if (p.ItemId.HasValue) return $"__ITEM_{p.ItemId}";
                    return $"__TAG_{(p.Tag ?? "UNKNOWN").Trim().ToUpper()}";
                });

                var vinWithStock = new HashSet<string>(groupedByVin.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

                foreach (var group in groupedByVin)
                {
                    var latestPiece  = group.OrderByDescending(p => p.CreatedDate).First();
                    var resolvedItem = ResolveItem(latestPiece);

                    if (resolvedItem != null && (!resolvedItem.IsActive || IsExcluded(resolvedItem.StatusItem)))
                        continue;

                    var totalVinStock = (decimal)group.Count();
                    var minStock      = (decimal)(resolvedItem?.RackMin ?? 5);
                    var maxStock      = (decimal)(resolvedItem?.RackMax ?? 20);
                    var levelStock    = minStock > 0 ? totalVinStock / minStock : 0;

                    string status;
                    if (levelStock < 1m)               status = "Shortage";
                    else if (totalVinStock > maxStock) status = "Over";
                    else                               status = "Normal";

                    var plant = NormalizePlant(resolvedItem?.Plant ?? latestPiece.Plant);

                    var stockLevel = status switch
                    {
                        "Shortage" => levelStock < 1.0m ? "<1D" : "<1.5D",
                        "Over"     => levelStock < 3.0m ? "2-3D" : ">3D",
                        _          => "1.5-2D"
                    };

                    // PENTING: Gunakan VIN sebagai identifier utama (bukan ItemCode)
                    // karena Dashboard mengelompokkan per Customer|VIN.
                    // Jika pakai ItemCode, item dengan VIN berbeda tapi ItemCode sama
                    // akan di-merge saat GetCriticalStockTrend GroupBy ItemCode.
                    var itemCode = !string.IsNullOrWhiteSpace(resolvedItem?.VIN)
                        ? resolvedItem!.VIN
                        : (!string.IsNullOrWhiteSpace(resolvedItem?.ItemCode)
                            ? resolvedItem!.ItemCode
                            : (!string.IsNullOrWhiteSpace(latestPiece.Item?.VIN)
                                ? latestPiece.Item!.VIN
                                : (latestPiece.Item?.ItemCode ?? group.Key)));

                    var custStr = (resolvedItem?.Customer ?? latestPiece.Item?.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(custStr) && !itemCode.Contains("|"))
                        itemCode = $"{custStr}|{itemCode}";

                    snapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = snapshotDate,
                        SnapshotTime = snapshotTime,
                        ItemCode     = itemCode,
                        ItemName     = resolvedItem?.ItemName ?? latestPiece.Item?.ItemName ?? "-",
                        Plant        = plant,
                        StockQty     = (int)totalVinStock,
                        DaysCoverage = Math.Round(levelStock, 4),
                        StockLevel   = stockLevel,
                        IsManual     = isManual,
                        CreatedAt    = DateTime.Now,
                    });
                }

                // -- BAGIAN 2: Zero Stock items (Manual Adjust) --
                var zeroStockPullings = pullings
                    .Where(p => p.AdjustNote == "Zero Stock" && p.ItemId.HasValue)
                    .GroupBy(p => p.ItemId!.Value)
                    .Select(g => g.OrderByDescending(x => x.CreatedDate).First())
                    .ToList();

                foreach (var zp in zeroStockPullings)
                {
                    var item = ResolveItem(zp);
                    if (item == null || !item.IsActive || IsExcluded(item.StatusItem)) continue;

                    var vinKey = (item.VIN ?? "").Trim().ToUpper();
                    var cust   = (item.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(vinKey)) vinKey = $"{cust}|{vinKey}";
                    else vinKey = $"__ITEM_{item.ItemId}";

                    if (vinWithStock.Contains(vinKey)) continue;

                    var plant = NormalizePlant(item.Plant);
                    var finalItemCode = !string.IsNullOrWhiteSpace(item.VIN) ? item.VIN : item.ItemCode;
                    var custStr = (item.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(custStr) && !string.IsNullOrEmpty(finalItemCode) && !finalItemCode.Contains("|"))
                        finalItemCode = $"{custStr}|{finalItemCode}";

                    snapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = snapshotDate,
                        SnapshotTime = snapshotTime,
                        ItemCode     = finalItemCode ?? "",
                        ItemName     = item.ItemName ?? "-",
                        Plant        = plant,
                        StockQty     = 0,
                        DaysCoverage = 0,
                        StockLevel   = "<1D",
                        IsManual     = isManual,
                        CreatedAt    = DateTime.Now,
                    });
                    vinWithStock.Add(vinKey);
                }

                // -- BAGIAN 3: Depleted items (Stok habis) --
                var depletedGroups = pullings
                    .Where(p => p.ItemId.HasValue && (p.AdjustNote ?? "") != "Zero Stock")
                    .GroupBy(p =>
                    {
                        var resolved = ResolveItem(p);
                        var vin  = (resolved?.VIN ?? "").Trim().ToUpper();
                        var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                        return vin != "" ? $"{cust}|{vin}" : $"__ITEM_{p.ItemId}";
                    });

                foreach (var group in depletedGroups)
                {
                    if (vinWithStock.Contains(group.Key)) continue;

                    var latestPulling = group.OrderByDescending(p => p.CreatedDate).First();
                    var item = ResolveItem(latestPulling);
                    if (item == null || !item.IsActive || IsExcluded(item.StatusItem)) continue;

                    var plant = NormalizePlant(item.Plant);
                    var finalItemCode = !string.IsNullOrWhiteSpace(item.VIN) ? item.VIN : item.ItemCode;
                    var custStr = (item.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(custStr) && !string.IsNullOrEmpty(finalItemCode) && !finalItemCode.Contains("|"))
                        finalItemCode = $"{custStr}|{finalItemCode}";

                    snapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = snapshotDate,
                        SnapshotTime = snapshotTime,
                        ItemCode     = finalItemCode ?? "",
                        ItemName     = item.ItemName ?? "-",
                        Plant        = plant,
                        StockQty     = 0,
                        DaysCoverage = 0,
                        StockLevel   = "<1D",
                        IsManual     = isManual,
                        CreatedAt    = DateTime.Now,
                    });
                    vinWithStock.Add(group.Key);
                }

                // -- BAGIAN 4: Sisa Master Item aktif yang BELUM muncul sama sekali --
                var activeItemsNotYetInStock = allItems
                    .Where(i => i.IsActive && !IsExcluded(i.StatusItem))
                    .ToList();

                foreach (var item in activeItemsNotYetInStock)
                {
                    var vinKey = (item.VIN ?? "").Trim().ToUpper();
                    var cust   = (item.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(vinKey)) vinKey = $"{cust}|{vinKey}";
                    else vinKey = $"__ITEM_{item.ItemId}";

                    if (vinWithStock.Contains(vinKey)) continue;

                    var plant = NormalizePlant(item.Plant);
                    var finalItemCode = !string.IsNullOrWhiteSpace(item.VIN) ? item.VIN : item.ItemCode;
                    var custStr = (item.Customer ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(custStr) && !string.IsNullOrEmpty(finalItemCode) && !finalItemCode.Contains("|"))
                        finalItemCode = $"{custStr}|{finalItemCode}";

                    snapshots.Add(new StockSnapshot
                    {
                        SnapshotDate = snapshotDate,
                        SnapshotTime = snapshotTime,
                        ItemCode     = finalItemCode ?? "",
                        ItemName     = item.ItemName ?? "-",
                        Plant        = plant,
                        StockQty     = 0,
                        DaysCoverage = 0,
                        StockLevel   = "<1D",
                        IsManual     = isManual,
                        CreatedAt    = DateTime.Now,
                    });
                    vinWithStock.Add(vinKey);
                }

                await context.StockSnapshots.AddRangeAsync(snapshots);
                await context.SaveChangesAsync();

                var msg = $"Snapshot selesai: {snapshots.Count} item " +
                          $"({(isManual ? "Manual" : "Auto jam 08:00")}) " +
                          $"pada {DateTime.Now:dd/MM/yyyy HH:mm}";
                _logger.LogInformation(msg);

                // Broadcast ke SignalR client bahwa Critical Stock Snapshot baru sudah di-capture
                try
                {
                    var hubContext = scope.ServiceProvider.GetService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.StockHub>>();
                    if (hubContext != null)
                    {
                        await hubContext.Clients.All.SendAsync("criticalStockCaptured", new
                        {
                            itemCount = snapshots.Count,
                            isManual = isManual,
                            updatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
                        });
                        await hubContext.Clients.All.SendAsync("updateStock");
                    }
                }
                catch (Exception exSig)
                {
                    _logger.LogWarning(exSig, "Gagal mengiriim SignalR notification untuk criticalStockCaptured.");
                }

                return (snapshots.Count, msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saat mengambil stock snapshot");
                return (0, $"Error: {ex.Message}");
            }
        }

        private static string NormalizePlant(string? raw)
        {
            var v = (raw ?? "").Trim();
            return v == "Hose" || v == "Molded" || v == "RVI" ? v : "";
        }
    }
}
