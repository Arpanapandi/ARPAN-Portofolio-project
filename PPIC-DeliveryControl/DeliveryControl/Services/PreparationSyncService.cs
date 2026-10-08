using DeliveryControl.Data;
using DeliveryControl.Helpers;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Menyinkronisasi PreparationRecord pending (ScheduleId = null) ke DeliverySchedule yang sesuai.
    /// Logika: iterasi dari sisi pending records → cari jadwal FIFO yang cocok (sama seperti LookupSchedule).
    /// </summary>
    public class PreparationSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PreparationSyncService> _logger;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.DeliveryHub> _deliveryHubContext;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Hubs.StockHub> _stockHubContext;

        public PreparationSyncService(
            ApplicationDbContext context, 
            ILogger<PreparationSyncService> logger,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.DeliveryHub> deliveryHubContext,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.StockHub> stockHubContext)
        {
            _context = context;
            _logger = logger;
            _deliveryHubContext = deliveryHubContext;
            _stockHubContext = stockHubContext;
        }

        /// <summary>
        /// Sync semua pending PreparationRecords (ScheduleId == null) ke jadwal yang tersedia.
        /// Dipanggil setelah ImportExcel, BulkCreate, atau manual trigger.
        /// Return jumlah records yang berhasil ditautkan ke jadwal.
        ///
        /// ATURAN SYNC (2 tahap):
        ///   TAHAP 1 — Strict (ManifestNumber match):
        ///     - Record punya ManifestNumber → cocokkan ke jadwal dengan ScheduleNumber == ManifestNumber
        ///       DAN ScheduledDate == tanggal scan.
        ///   TAHAP 2 — Fallback (Part No + tanggal):
        ///     - Record masih pending setelah tahap 1 → cocokkan ke jadwal hari yang sama (ScheduledDate == scan date)
        ///       berdasarkan Part No (ItemId) yang ada di jadwal, qty belum terpenuhi.
        ///     - Ini memungkinkan scan hari ini otomatis mengisi jadwal hari ini yang baru diupload.
        /// </summary>
        public async Task<int> SyncAllPendingAsync(DateTime? filterDate = null)
        {
            var effectiveDate = filterDate ?? DateTime.Today;
            _logger.LogWarning("=== SyncAllPending START === filterDate={F}", effectiveDate.ToString("yyyy-MM-dd"));

            var minScan = effectiveDate.AddDays(-3);
            var maxScan = effectiveDate.AddDays(1);
            var pendingQuery = _context.PreparationRecords
                .Where(p => p.ScheduleId == null && p.Remark == "Match" &&
                    (p.TargetDate.HasValue
                        ? p.TargetDate.Value.Date == effectiveDate.Date
                        : (p.CreatedDate.Date >= minScan && p.CreatedDate.Date <= maxScan)))
                .AsQueryable();

            var pendingRecords = await pendingQuery
                .OrderBy(p => p.CreatedDate)
                .ToListAsync();

            _logger.LogWarning("SyncAllPending: pending count = {Count}", pendingRecords.Count);
            if (!pendingRecords.Any()) return 0;

            var allItems = await _context.Items.ToListAsync();
            var allCustomers = await _context.Customers.ToListAsync();

            // -----------------------------------------------------------------------
            // GROUPING: deduplikasi berdasarkan kunci unik (dock + cycle + vin + delivery-date + manifest)
            // sehingga pencarian jadwal dilakukan SEKALI per kombinasi unik, bukan per record.
            // -----------------------------------------------------------------------
            var groups = pendingRecords
                .GroupBy(r => (
                    DockCustId : r.DockCustomerId ?? 0,
                    DockCycle  : (r.DockCycle ?? "").Trim().ToUpper(),
                    Tag        : VinHelper.Normalize(r.Tag),
                    EffDate    : (r.TargetDate?.Date ?? r.CreatedDate.Date).ToString("yyyyMMdd"),
                    Manifest   : (r.ManifestNumber ?? "").Trim().ToUpper()
                ))
                .ToList();

            _logger.LogWarning("SyncAllPending: unique groups = {G} (dari {N} records)", groups.Count, pendingRecords.Count);

            int synced = 0;
            // Kumpulkan jadwal yang dimodifikasi agar qty-update bisa dilakukan batch di akhir
            var modifiedSchedules = new Dictionary<int, DeliverySchedule>();
            // (scheduleId, itemId) → qpc untuk recount setelah bulk-save
            var modifiedPairs = new Dictionary<(int scheduleId, int itemId), int>();

            DateTime nowOuter = DateTime.Now;

            foreach (var grp in groups)
            {
                // Gunakan record pertama dalam grup sebagai representatif lookup jadwal
                var rep = grp.First();
                string normalizedTag = grp.Key.Tag;

                var item = allItems.FirstOrDefault(i =>
                    VinHelper.Normalize(i.VIN) == normalizedTag ||
                    VinHelper.IsMatch(i.VIN, rep.Tag) ||
                    (!string.IsNullOrEmpty(i.ItemCode) && VinHelper.Normalize(i.ItemCode) == normalizedTag));

                if (item == null) continue;

                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;

                var scanDate = rep.TargetDate?.Date ?? rep.CreatedDate.Date;
                bool hasTargetDate = rep.TargetDate.HasValue;
                DeliverySchedule? schedule = null;

                var repCustomer = rep.DockCustomerId.HasValue
                    ? allCustomers.FirstOrDefault(c => c.CustomerId == rep.DockCustomerId.Value)
                    : null;
                bool isSelectedAdm = repCustomer != null &&
                                      (string.Equals(repCustomer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(repCustomer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                // TAHAP 1: Strict match via ManifestNumber (BERLAKU UNTUK SEMUA)
                string? matchManifestNumber = rep.ManifestNumber;

                if (!string.IsNullOrEmpty(matchManifestNumber))
                {
                    var manifestUpper = matchManifestNumber.Trim().ToUpper();
                    var manifestQuery = _context.DeliverySchedules
                        .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                        .Include(s => s.Customer)
                        .Where(s => s.Status != "Cancelled" &&
                                    s.ScheduleNumber.ToUpper() == manifestUpper &&
                                    (hasTargetDate
                                        ? s.ScheduledDate.Date == scanDate
                                        : (s.ScheduledDate.Date >= scanDate.AddDays(-2) && s.ScheduledDate.Date <= scanDate.AddDays(1))) &&
                                    s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                    if (rep.DockCustomerId.HasValue && rep.DockCustomerId.Value > 0)
                        manifestQuery = manifestQuery.Where(s => s.CustomerId == rep.DockCustomerId.Value);
                    if (!string.IsNullOrEmpty(rep.DockCycle) && rep.DockCycle != "-")
                        manifestQuery = manifestQuery.Where(s => s.Cycle == rep.DockCycle);

                    var potentialSchedules = await manifestQuery
                        .OrderByDescending(s => s.ScheduledDate.Date == scanDate ? 1 : 0)
                        .ThenBy(s => s.ScheduledDate).ThenBy(s => s.ScheduleNumber)
                        .ToListAsync();

                    foreach (var ps in potentialSchedules)
                    {
                        if (!VinHelper.IsKanbanBelongsToManifest(rep.Kanban, ps.ScheduleNumber))
                            continue;

                        bool isPreScannedTarget = hasTargetDate && rep.CreatedDate.Date < ps.ScheduledDate.Date;
                        bool isSameDay     = rep.CreatedDate.Date == ps.ScheduledDate.Date;
                        bool isDebtFilling = !hasTargetDate && (scanDate == ps.ScheduledDate.Date.AddDays(1) || scanDate == ps.ScheduledDate.Date.AddDays(2));
                        bool isPreScanH1 = !hasTargetDate && ps.ScheduledDate.Date == scanDate.AddDays(1);

                        if (hasTargetDate || isSameDay || isDebtFilling || isPreScannedTarget || isPreScanH1) { schedule = ps; break; }
                    }
                }

                // TAHAP 2: Fallback via Part No + tanggal
                if (schedule == null)
                {
                    var fallbackQuery = _context.DeliverySchedules
                        .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                        .Include(s => s.Customer)
                        .Where(s => s.Status != "Cancelled" &&
                                    (hasTargetDate
                                        ? s.ScheduledDate.Date == scanDate
                                        : (s.ScheduledDate.Date >= scanDate.AddDays(-2) && s.ScheduledDate.Date <= scanDate.AddDays(1))) &&
                                    s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                    if (rep.DockCustomerId.HasValue && rep.DockCustomerId.Value > 0)
                        fallbackQuery = fallbackQuery.Where(s => s.CustomerId == rep.DockCustomerId.Value);
                    if (!string.IsNullOrEmpty(rep.DockCycle) && rep.DockCycle != "-")
                        fallbackQuery = fallbackQuery.Where(s => s.Cycle == rep.DockCycle);

                    var potentialSchedules = await fallbackQuery
                        .OrderByDescending(s => s.ScheduledDate.Date == scanDate ? 1 : 0)
                        .ThenBy(s => s.ScheduledDate).ThenBy(s => s.ScheduleNumber)
                        .ToListAsync();

                    foreach (var ps in potentialSchedules)
                    {
                        if (!VinHelper.IsKanbanBelongsToManifest(rep.Kanban, ps.ScheduleNumber))
                            continue;

                        // Pre-scan eksplisit: operator set TargetDate & scan sebelum hari delivery
                        bool isPreScannedTarget = hasTargetDate && rep.CreatedDate.Date < ps.ScheduledDate.Date;

                        // StartPrepTime diabaikan sesuai request user (langsung sinkron tanpa nunggu jam)

                        bool isSameDay     = rep.CreatedDate.Date == ps.ScheduledDate.Date;
                        bool isDebtFilling = !hasTargetDate && (scanDate == ps.ScheduledDate.Date.AddDays(1) || scanDate == ps.ScheduledDate.Date.AddDays(2));
                        
                        // Pre-scan H-1 tanpa TargetDate: Scan dilakukan kemarin untuk jadwal hari ini
                        bool isPreScanH1 = !hasTargetDate && ps.ScheduledDate.Date == scanDate.AddDays(1);

                        // Jika operator memilih TargetDate eksplisit, jadwal sudah dipastikan cocok dari query SQL (s.ScheduledDate.Date == scanDate).
                        if (hasTargetDate || isSameDay || isDebtFilling || isPreScannedTarget || isPreScanH1) { schedule = ps; break; }
                    }
                }

                // TAHAP 3: VIN-based direct match in specific manifest (Berlaku untuk semua)
                if (schedule == null && !string.IsNullOrEmpty(matchManifestNumber))
                {
                    var t3ManifestUpper = matchManifestNumber.Trim().ToUpper();
                    var t3Query = _context.DeliverySchedules
                        .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                        .Include(s => s.Customer)
                        .Where(s => s.ScheduleNumber.ToUpper() == t3ManifestUpper &&
                                    s.Status != "Cancelled" &&
                                    (hasTargetDate
                                        ? s.ScheduledDate.Date == scanDate
                                        : (s.ScheduledDate.Date >= scanDate.AddDays(-2) && s.ScheduledDate.Date <= scanDate.AddDays(1))));

                    if (rep.DockCustomerId.HasValue && rep.DockCustomerId.Value > 0)
                        t3Query = t3Query.Where(s => s.CustomerId == rep.DockCustomerId.Value);
                    if (!string.IsNullOrEmpty(rep.DockCycle) && rep.DockCycle != "-")
                        t3Query = t3Query.Where(s => s.Cycle == rep.DockCycle);

                    var t3Schedules = await t3Query
                        .OrderByDescending(s => s.ScheduledDate.Date == scanDate ? 1 : 0)
                        .ThenBy(s => s.ScheduledDate)
                        .ToListAsync();
                    
                    foreach (var t3Sched in t3Schedules)
                    {
                        if (!VinHelper.IsKanbanBelongsToManifest(rep.Kanban, t3Sched.ScheduleNumber))
                            continue;

                        bool isPreScannedTarget = hasTargetDate && rep.CreatedDate.Date < t3Sched.ScheduledDate.Date;
                        bool isSameDay     = rep.CreatedDate.Date == t3Sched.ScheduledDate.Date;
                        bool isDebtFilling = !hasTargetDate && (scanDate == t3Sched.ScheduledDate.Date.AddDays(1) || scanDate == t3Sched.ScheduledDate.Date.AddDays(2));
                        bool isPreScanH1 = !hasTargetDate && t3Sched.ScheduledDate.Date == scanDate.AddDays(1);

                        if (hasTargetDate || isSameDay || isDebtFilling || isPreScannedTarget || isPreScanH1)
                        {
                            var t3DI = t3Sched.DeliveryItems.FirstOrDefault(di =>
                                di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, rep.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, rep.Tag) ||
                                    VinHelper.IsMatch(di.ExternalPartNo, rep.Tag)) &&
                                (di.ActualQuantity ?? 0) < di.Quantity);

                            if (t3DI != null)
                            {
                                schedule = t3Sched;
                                var t3Item = allItems.FirstOrDefault(i => i.ItemId == t3DI.ItemId);
                                if (t3Item != null) { item = t3Item; qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1; }
                                break;
                            }
                        }
                    }
                }

                if (schedule == null) continue;

                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity)
                            ?? schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                if (dItem == null)
                {
                    dItem = schedule.DeliveryItems.FirstOrDefault(di =>
                                di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, rep.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, rep.Tag)) &&
                                (di.ActualQuantity ?? 0) < di.Quantity)
                            ?? schedule.DeliveryItems.FirstOrDefault(di =>
                                di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, rep.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, rep.Tag)));
                    if (dItem != null)
                    {
                        var remappedItem = allItems.FirstOrDefault(i => i.ItemId == dItem.ItemId);
                        if (remappedItem != null) { item = remappedItem; qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1; }
                    }
                }
                if (dItem == null) continue;

                // Assign SEMUA records dalam grup ini ke jadwal yang sama (bulk, tanpa save per-record)
                foreach (var rec in grp)
                {
                    rec.ScheduleId     = schedule.ScheduleId;
                    rec.ManifestNumber = schedule.ScheduleNumber;
                }
                dItem.PrepScanTime = rep.CreatedDate;

                synced += grp.Count();

                // Catat jadwal & pair yang perlu recount qty setelah bulk-save
                modifiedSchedules[schedule.ScheduleId] = schedule;
                modifiedPairs[(schedule.ScheduleId, item.ItemId)] = qpc;
            }

            if (synced == 0) return 0;

            // -----------------------------------------------------------------------
            // BULK SAVE — satu round-trip untuk semua assignment sekaligus
            // -----------------------------------------------------------------------
            await _context.SaveChangesAsync();

            // -----------------------------------------------------------------------
            // RECOUNT ActualQuantity untuk setiap (scheduleId, itemId) yang dimodifikasi
            // -----------------------------------------------------------------------
            foreach (var ((schedId, itemId), itemQpc) in modifiedPairs)
            {
                var sched = modifiedSchedules[schedId];
                var relatedItems = sched.DeliveryItems.Where(di => di.ItemId == itemId).ToList();
                if (!relatedItems.Any()) continue;

                var item = allItems.FirstOrDefault(i => i.ItemId == itemId);
                if (item == null) continue;

                // Ambil tag candidates gabungan dari seluruh related items
                var tagCandidates = new List<string>();
                foreach (var ri in relatedItems)
                {
                    var riTags = VinHelper.GetItemTags(item.VIN, item.ItemCode, ri.ExternalPartNo ?? item.CustomerPartNumber);
                    tagCandidates.AddRange(riTags);
                }
                tagCandidates = tagCandidates
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t.Trim().ToUpper())
                    .Distinct()
                    .ToList();

                var records = await _context.PreparationRecords
                    .Where(p => p.ScheduleId == schedId &&
                                tagCandidates.Contains(p.Tag) &&
                                p.Remark == "Match")
                    .OrderBy(p => p.CreatedDate)
                    .ToListAsync();

                bool isAdm = sched.Customer != null && 
                             (string.Equals(sched.Customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                              string.Equals(sched.Customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                var filteredRecords = records;
                if (isAdm)
                {
                    filteredRecords = records
                        .Where(r => VinHelper.IsKanbanBelongsToManifest(r.Kanban, sched.ScheduleNumber))
                        .ToList();
                }

                var uniqueRecords = new List<PreparationRecord>();
                var seenBarcodes = new HashSet<string>();

                foreach (var r in filteredRecords)
                {
                    if (!string.IsNullOrEmpty(r.Kanban))
                    {
                        var kbnUpper = r.Kanban.Trim().ToUpper();
                        if ((kbnUpper.StartsWith("KBN") || kbnUpper.StartsWith("DN")) && kbnUpper.Length >= 12)
                        {
                            if (seenBarcodes.Contains(kbnUpper))
                            {
                                continue;
                            }
                            seenBarcodes.Add(kbnUpper);
                        }
                    }
                    uniqueRecords.Add(r);
                }

                int totalScans = uniqueRecords.Count;
                int scansRemaining = totalScans;

                foreach (var ri in relatedItems)
                {
                    int targetKbn = (int)Math.Ceiling((double)ri.Quantity / itemQpc);
                    int assignedScans = Math.Min(scansRemaining, targetKbn);
                    scansRemaining -= assignedScans;

                    decimal newActualQty = Math.Min((decimal)assignedScans * itemQpc, ri.Quantity);
                    
                    ri.ActualQuantity = newActualQty;
                    ri.IsCompleted = ri.ActualQuantity >= ri.Quantity;
                }
            }

            // Update status semua jadwal yang dimodifikasi
            foreach (var sched in modifiedSchedules.Values)
            {
                sched.TotalActualQuantity = sched.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                if (sched.Status == "Scheduled") sched.Status = "In Progress";
                sched.PreparationStatus = "In Progress";
                sched.UpdatedDate = DateTime.Now;

                if (sched.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    sched.PreparationStatus = "Prepared";
                    if (!sched.ReadyToDockTime.HasValue)
                        sched.ReadyToDockTime = DateTime.Now;
                }
            }

            // BULK SAVE kedua — qty + status update
            await _context.SaveChangesAsync();

            await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "sync" });
            await _stockHubContext.Clients.All.SendAsync("updateStock");

            return synced;
        }

        /// <summary>
        /// Wrapper untuk ImportExcel/BulkCreate — selalu jalankan SyncAllPendingAsync.
        /// </summary>
        public Task<int> SyncPendingForSchedulesAsync(IEnumerable<int> scheduleIds)
            => SyncAllPendingAsync();

        /// <summary>
        /// Hitung berapa banyak pending records yang belum punya ScheduleId.
        /// </summary>
        public async Task<int> CountPendingAsync()
            => await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");

        /// <summary>
        /// SYNC BY PART NO — Sinkronisasi pending records ke jadwal berdasarkan kecocokan Part No.
        /// 
        /// ATURAN TANGGAL (PENTING):
        ///   Item pending HANYA bisa masuk ke jadwal jika ScheduledDate == tanggal scan item itu sendiri.
        ///   Contoh: scan tgl 6 → hanya bisa masuk jadwal tgl 6.
        ///            scan tgl 7 → TIDAK BISA masuk jadwal tgl 6, meskipun item-nya sama.
        ///
        /// ATURAN UMUM:
        ///   1. Cari semua pending records (ScheduleId == null, Remark == "Match").
        ///   2. Untuk setiap pending record, cari item berdasarkan Tag.
        ///   3. Cari jadwal dengan ScheduledDate == tanggal scan, punya DeliveryItem dengan ItemId sama
        ///      DAN qty belum terpenuhi.
        ///   4. Jika ketemu → tautkan pending record ke jadwal tersebut, update ActualQuantity.
        ///   5. Jika tidak ada jadwal yang cocok untuk tanggal scan → record tetap pending.
        ///
        /// Return: jumlah records yang berhasil ditautkan.
        /// </summary>
        public async Task<int> SyncPendingByPartNoAsync()
        {
            var today = DateTime.Today;
            _logger.LogWarning("=== SyncPendingByPartNo START === today={Today}", today);

            // Load semua pending records yang bukan mismatch
            var pendingRecords = await _context.PreparationRecords
                .Where(p => p.ScheduleId == null && p.Remark == "Match")
                .OrderBy(p => p.CreatedDate)
                .ToListAsync();

            _logger.LogWarning("Pending records count: {Count}", pendingRecords.Count);
            if (!pendingRecords.Any())
            {
                _logger.LogWarning("NO pending records found. Returning 0.");
                return 0;
            }

            foreach (var pr in pendingRecords)
                _logger.LogWarning("  Pending: Id={Id}, Tag={Tag}, Label={Label}, Remark={Remark}, Created={Created}", 
                    pr.PreparationId, pr.Tag, pr.Label, pr.Remark, pr.CreatedDate);

            // Load semua Items untuk matching Tag → ItemId
            var allItems = await _context.Items.ToListAsync();
            _logger.LogWarning("All items count: {Count}", allItems.Count);

            // Load semua ItemMappings untuk matching Part No via external mapping
            var allMappings = await _context.ItemMappings
                .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                .ToListAsync();

            var allCustomers = await _context.Customers.ToListAsync();
            int synced = 0;

            foreach (var rec in pendingRecords)
            {
                string normalizedTag = VinHelper.Normalize(rec.Tag);
                _logger.LogWarning("Processing pending Id={Id}, Tag={Tag}, NormalizedTag={NTag}", rec.PreparationId, rec.Tag, normalizedTag);

                // Tanggal saat item ini di-scan — KUNCI: hanya boleh masuk jadwal tanggal yang sama
                // Gunakan TargetDate jika operator memilih tanggal jadwal ke depan.
                var scanDate = rec.TargetDate?.Date ?? rec.CreatedDate.Date;

                // Cari item yang cocok dengan Tag (VIN/ItemCode)
                var item = allItems.FirstOrDefault(i =>
                    VinHelper.Normalize(i.VIN) == normalizedTag ||
                    VinHelper.IsMatch(i.VIN, rec.Tag) ||
                    (!string.IsNullOrEmpty(i.ItemCode) && VinHelper.Normalize(i.ItemCode) == normalizedTag));

                if (item == null)
                {
                    _logger.LogWarning("  => NO ITEM MATCH for tag '{Tag}'. Skipping.", rec.Tag);
                    continue;
                }
                _logger.LogWarning("  => Item found: ItemId={ItemId}, VIN={VIN}, ItemCode={Code}, ScanDate={ScanDate}", 
                    item.ItemId, item.VIN, item.ItemCode, scanDate.ToString("yyyy-MM-dd"));

                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;

                // ATURAN TANGGAL & START PREP
                // Rentang tanggal: H-1 (hutang kemarin), hari ini (same-day), H+1 (early bird besok)
                var potentialSchedules = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                        .ThenInclude(di => di.Item)
                    .Include(s => s.Customer)
                    .Where(s => s.Status != "Cancelled" &&
                                 (s.ScheduledDate.Date >= scanDate.AddDays(-2) && s.ScheduledDate.Date <= scanDate.AddDays(2)) &&
                                 s.DeliveryItems.Any(di =>
                                     di.ItemId == item.ItemId &&
                                     (di.ActualQuantity ?? 0) < di.Quantity))
                    .OrderByDescending(s => s.ScheduledDate.Date == scanDate ? 1 : 0) // same-day first
                    .ThenBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .ToListAsync();

                var customer = allCustomers.FirstOrDefault(c => c.CustomerId == rec.DockCustomerId);
                bool isSelectedAdm = customer != null &&
                                      (string.Equals(customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                string? matchManifestNumber = isSelectedAdm ? rec.ManifestNumber : null;

                // Filter by cycle jika record punya DockCycle
                if (!string.IsNullOrEmpty(rec.DockCycle) && rec.DockCycle != "-")
                    potentialSchedules = potentialSchedules.Where(s => s.Cycle == rec.DockCycle).ToList();

                // Filter by dock jika record punya DockCustomerId
                if (rec.DockCustomerId.HasValue && rec.DockCustomerId.Value > 0)
                    potentialSchedules = potentialSchedules.Where(s => s.CustomerId == rec.DockCustomerId.Value).ToList();

                // Filter by manifest jika ada (Hanya ADM)
                if (!string.IsNullOrEmpty(matchManifestNumber))
                    potentialSchedules = potentialSchedules.Where(s => s.ScheduleNumber.ToUpper() == matchManifestNumber.Trim().ToUpper()).ToList();

                DeliverySchedule? schedule = null;
                DateTime now = DateTime.Now;
                foreach (var ps in potentialSchedules)
                {
                    // StartPrepTime diabaikan sesuai request user (langsung sinkron tanpa nunggu jam)
                    
                    // Validasi tanggal scan (efektif) terhadap jadwal
                    bool isSameDay = scanDate == ps.ScheduledDate.Date;
                    bool isHMinus1Valid = scanDate == ps.ScheduledDate.Date.AddDays(-1) || scanDate == ps.ScheduledDate.Date.AddDays(-2); // Scan H-1 atau H-2 (early prep)
                    bool isDebtFilling = scanDate == ps.ScheduledDate.Date.AddDays(1) ||
                                         scanDate == ps.ScheduledDate.Date.AddDays(2); // Scan H+1 atau H+2 (hutang)

                    if (isSameDay || isHMinus1Valid || isDebtFilling || !string.IsNullOrEmpty(matchManifestNumber))
                    {
                        bool isAdmSched = ps.Customer != null && 
                                          (string.Equals(ps.Customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                                           string.Equals(ps.Customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));
                        
                        if (isAdmSched && !VinHelper.IsKanbanBelongsToManifest(rec.Kanban, ps.ScheduleNumber))
                            continue;

                        schedule = ps;
                        break;
                    }
                }

                if (schedule == null)
                {
                    _logger.LogWarning("  => NO VALID SCHEDULE (considering StartPrep) on scanDate={ScanDate} for ItemId={ItemId}. Skipping.", 
                        scanDate.ToString("yyyy-MM-dd"), item.ItemId);
                    continue;
                }
                _logger.LogWarning("  => Matched Schedule: Id={SId}, Number={Num}, Date={Date}", 
                    schedule.ScheduleId, schedule.ScheduleNumber, schedule.ScheduledDate.ToString("yyyy-MM-dd"));

                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity)
                            ?? schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                // Fallback: cari via VIN/ItemCode jika ItemId tidak cocok (item remap scenario)
                if (dItem == null)
                {
                    dItem = schedule.DeliveryItems.FirstOrDefault(di =>
                        di.Item != null && (
                            VinHelper.IsMatch(di.Item.VIN, rec.Tag) ||
                            VinHelper.IsMatch(di.Item.ItemCode, rec.Tag) ||
                            VinHelper.IsMatch(di.ExternalPartNo, rec.Tag)) &&
                        (di.ActualQuantity ?? 0) < di.Quantity);
                    if (dItem != null)
                    {
                        var remappedItem = allItems.FirstOrDefault(i => i.ItemId == dItem.ItemId);
                        if (remappedItem != null) { item = remappedItem; qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1; }
                    }
                }
                if (dItem == null) continue;

                // Assign ke jadwal ini
                rec.ScheduleId = schedule.ScheduleId;
                rec.ManifestNumber = schedule.ScheduleNumber; // Update manifest reference
                dItem.PrepScanTime = rec.CreatedDate;

                // Simpan record terlebih dahulu agar CountAsync di bawah melihat record yang baru.
                await _context.SaveChangesAsync();

                var relatedItems = schedule.DeliveryItems.Where(di => di.ItemId == item.ItemId).ToList();
                if (relatedItems.Any())
                {
                    // Ambil tag candidates gabungan dari seluruh related items
                    var tagCandidates = new List<string>();
                    foreach (var ri in relatedItems)
                    {
                        var riTags = VinHelper.GetItemTags(item.VIN, item.ItemCode, ri.ExternalPartNo ?? item.CustomerPartNumber);
                        tagCandidates.AddRange(riTags);
                    }
                    tagCandidates = tagCandidates
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Select(t => t.Trim().ToUpper())
                        .Distinct()
                        .ToList();

                    var records = await _context.PreparationRecords
                        .Where(p => p.ScheduleId == schedule.ScheduleId &&
                                    tagCandidates.Contains(p.Tag) &&
                                    p.Remark == "Match")
                        .OrderBy(p => p.CreatedDate)
                        .ToListAsync();

                    bool isAdm = schedule.Customer != null && 
                                 (string.Equals(schedule.Customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                                  string.Equals(schedule.Customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                    var filteredRecords = records;
                    if (isAdm)
                    {
                        filteredRecords = records
                            .Where(r => VinHelper.IsKanbanBelongsToManifest(r.Kanban, schedule.ScheduleNumber))
                            .ToList();
                    }

                    var uniqueRecords = new List<PreparationRecord>();
                    var seenBarcodes = new HashSet<string>();

                    foreach (var r in filteredRecords)
                    {
                        if (!string.IsNullOrEmpty(r.Kanban))
                        {
                            var kbnUpper = r.Kanban.Trim().ToUpper();
                            if ((kbnUpper.StartsWith("KBN") || kbnUpper.StartsWith("DN")) && kbnUpper.Length >= 12)
                            {
                                if (seenBarcodes.Contains(kbnUpper))
                                {
                                    continue;
                                }
                                seenBarcodes.Add(kbnUpper);
                            }
                        }
                        uniqueRecords.Add(r);
                    }

                    int totalScans = uniqueRecords.Count;
                    int scansRemaining = totalScans;

                    foreach (var ri in relatedItems)
                    {
                        int targetKbn = (int)Math.Ceiling((double)ri.Quantity / qpc);
                        int assignedScans = Math.Min(scansRemaining, targetKbn);
                        scansRemaining -= assignedScans;

                        decimal newActualQtyB = Math.Min((decimal)assignedScans * qpc, ri.Quantity);
                        
                        // RATCHET: hanya update jika nilai baru LEBIH TINGGI dari yang tersimpan.
                        if (newActualQtyB > (ri.ActualQuantity ?? 0))
                            ri.ActualQuantity = newActualQtyB;
                        ri.IsCompleted = (ri.ActualQuantity ?? 0) >= ri.Quantity;
                    }
                }

                // Sinkronkan TotalActualQuantity dari semua item dalam schedule ini
                schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                if (schedule.Status == "Scheduled") schedule.Status = "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                // Jika semua item schedule selesai
                if (schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    schedule.PreparationStatus = "Prepared";
                    if (!schedule.ReadyToDockTime.HasValue)
                        schedule.ReadyToDockTime = DateTime.Now;
                }

                synced++;

                // Simpan perubahan dItem dan schedule
                await _context.SaveChangesAsync();
            }

            if (synced > 0)
            {
                await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "sync" });
                await _stockHubContext.Clients.All.SendAsync("updateStock");
            }

            return synced;
        }

        /// <summary>
        /// Preview: hitung berapa banyak pending records yang BISA di-sync ke jadwal
        /// via Part No matching (tanpa benar-benar melakukan sync).
        /// ATURAN TANGGAL: item pending hanya bisa masuk jadwal yang ScheduledDate == tanggal scan item.
        /// Return { canSync: jumlah yang bisa di-sync, total: total pending }
        /// </summary>
        public async Task<(int CanSync, int Total)> PreviewSyncByPartNoAsync()
        {
            var pendingRecords = await _context.PreparationRecords
                .Where(p => p.ScheduleId == null && p.Remark == "Match")
                .OrderBy(p => p.CreatedDate)
                .ToListAsync();

            int total = pendingRecords.Count;
            if (total == 0) return (0, 0);

            var allItems = await _context.Items.ToListAsync();

            // Kumpulkan tanggal scan unik — ambil jadwal untuk rentang tanggal yang relevan (H-1 s/d H+1)
            var scanDates = pendingRecords.Select(r => r.CreatedDate.Date).Distinct().ToList();
            var minDate = scanDates.Min().AddDays(-2);
            var maxDate = scanDates.Max().AddDays(1);

            // Ambil semua jadwal yang tanggalnya ada di rentang tanggal scan (in-memory matching)
            var relevantSchedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Where(s => s.Status != "Cancelled" &&
                             s.ScheduledDate.Date >= minDate && s.ScheduledDate.Date <= maxDate)
                .ToListAsync();

            int canSync = 0;
            foreach (var rec in pendingRecords)
            {
                string normalizedTag = VinHelper.Normalize(rec.Tag);
                var item = allItems.FirstOrDefault(i =>
                    VinHelper.Normalize(i.VIN) == normalizedTag ||
                    VinHelper.IsMatch(i.VIN, rec.Tag) ||
                    (!string.IsNullOrEmpty(i.ItemCode) && VinHelper.Normalize(i.ItemCode) == normalizedTag));

                if (item == null) continue;

                var scanDate = rec.TargetDate?.Date ?? rec.CreatedDate.Date;

                // Hitung match jika jadwal ada di rentang H-1/same-day/H+1
                bool hasMatch = relevantSchedules.Any(s =>
                    (s.ScheduledDate.Date == scanDate || s.ScheduledDate.Date == scanDate.AddDays(1) || 
                     s.ScheduledDate.Date == scanDate.AddDays(-1) || s.ScheduledDate.Date == scanDate.AddDays(-2)) &&
                    s.DeliveryItems.Any(di =>
                        di.ItemId == item.ItemId &&
                        (di.ActualQuantity ?? 0) < di.Quantity));

                if (hasMatch) canSync++;
            }

            return (canSync, total);
        }

        public async Task<(int, int)> CleanupExistingInvalidSyncsAsync()
        {
            var today = DateTime.Today;
            var schedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date == today && (s.Status == "Scheduled" || s.Status == "In Progress"))
                .ToListAsync();

            int schedulesAffected = 0;
            int recordsDetached = 0;

            // TAHAP 1: Detach invalid records
            foreach (var s in schedules)
            {
                var startMinutes = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
                if (startMinutes <= 0) continue;

                DateTime allowedStart = s.ScheduledDate.Date.AddMinutes(startMinutes);
                if (s.PickupTime.HasValue && s.PickupTime.Value.Hour < 12 && startMinutes > 720)
                    allowedStart = allowedStart.AddDays(-1);

                // Cari records yang di-scan sebelum allowedStart
                var invalidRecords = await _context.PreparationRecords
                    .Where(r => r.ScheduleId == s.ScheduleId && r.CreatedDate < allowedStart)
                    .ToListAsync();

                if (invalidRecords.Any())
                {
                    schedulesAffected++;
                    foreach (var ir in invalidRecords)
                    {
                        ir.ScheduleId = null;
                        ir.ManifestNumber = null;
                        recordsDetached++;
                    }
                }
            }

            await _context.SaveChangesAsync();

            // TAHAP 2: Recalculate Dashboard Progress dari awal (Selalu jalankan untuk memastikan status sinkron)
            foreach (var s in schedules)
            {
                bool isAdm = s.Customer != null && 
                             (string.Equals(s.Customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                              string.Equals(s.Customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                // Ambil SEMUA records yang tersisa untuk schedule ini
                var currentRecords = await _context.PreparationRecords
                    .Where(r => r.ScheduleId == s.ScheduleId)
                    .ToListAsync();

                // Detach records yang tidak sesuai manifest (cross-manifest) - hanya untuk ADM
                if (isAdm)
                {
                    var invalidManifestRecords = currentRecords
                        .Where(r => !VinHelper.IsKanbanBelongsToManifest(r.Kanban, s.ScheduleNumber))
                        .ToList();
                    if (invalidManifestRecords.Any())
                    {
                        schedulesAffected++;
                        foreach (var ir in invalidManifestRecords)
                        {
                            ir.ScheduleId = null;
                            ir.ManifestNumber = VinHelper.ExtractManifestFromKanban(ir.Kanban, s.ScheduleNumber) ?? ir.ManifestNumber;
                            recordsDetached++;
                        }
                        // Refresh currentRecords setelah detach
                        currentRecords = currentRecords.Where(r => r.ScheduleId != null).ToList();
                    }
                }

                s.TotalActualQuantity = 0;
                foreach (var di in s.DeliveryItems)
                {
                    var itemTagCandidates = VinHelper.GetItemTags(di.Item?.VIN, di.Item?.ItemCode, di.ExternalPartNo ?? di.Item?.CustomerPartNumber)
                        .Select(t => VinHelper.Normalize(t))
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Distinct()
                        .ToList();
                    
                    // Hitung dari data real di DB setelah detach. Untuk ADM, saring cross-manifest scans.
                    var matchRecords = currentRecords
                        .Where(r => {
                            var rNorm = VinHelper.Normalize(r.Tag);
                            return itemTagCandidates.Any(tc => tc == rNorm || rNorm.Contains(tc) || tc.Contains(rNorm)) &&
                                   (!isAdm || VinHelper.IsKanbanBelongsToManifest(r.Kanban, s.ScheduleNumber));
                        })
                        .ToList();

                    int qpc = (di.Item?.QtyLot != null && di.Item.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                    di.ActualQuantity = matchRecords.Count * qpc;
                    
                    // Update status bit (CRITICAL: Pastikan ini false jika qty tidak cukup)
                    di.IsCompleted = di.ActualQuantity >= di.Quantity;
                    
                    // Update PrepScanTime ke yang terbaru dari sisa records (jika ada)
                    di.PrepScanTime = matchRecords.Any() ? matchRecords.Max(r => r.CreatedDate) : (DateTime?)null;
                    
                    s.TotalActualQuantity += di.ActualQuantity ?? 0;
                }

                // Reset status schedule jika jadi kosong
                if (s.TotalActualQuantity <= 0)
                {
                    s.TotalActualQuantity = 0;
                    s.PreparationStatus = "Scheduled";
                    s.Status = "Scheduled"; 
                }
                else if (s.TotalActualQuantity < s.TotalTargetQuantity)
                {
                    s.PreparationStatus = "In Progress";
                    s.Status = "In Progress";
                }
                else
                {
                    s.PreparationStatus = "Prepared";
                }
            }
            
            await _context.SaveChangesAsync();
            
            await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "cleanup_full" });
            await _stockHubContext.Clients.All.SendAsync("updateStock");

            return (schedulesAffected, recordsDetached);
        }

        /// <summary>
        /// Hitung ulang ActualQuantity untuk semua DeliveryItems dalam rentang tanggal tertentu.
        /// Dijalankan otomatis saat startup untuk memperbaiki data yang mungkin salah akibat bug
        /// increment atau dedup yang tidak tepat.
        /// DN kanbans: unik per unit → dedup by barcode.
        /// Non-DN kanbans: same barcode = multiple kanban fisik → hitung total scan records.
        /// </summary>
        public async Task RecalcActualQtyForDateRangeAsync(DateTime minDate, DateTime maxDate)
        {
            _logger.LogInformation("RecalcActualQty: scanning schedules {Min:yyyy-MM-dd} to {Max:yyyy-MM-dd}", minDate, maxDate);

            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date >= minDate &&
                            s.ScheduledDate.Date <= maxDate &&
                            s.Status != "Cancelled" &&
                            !s.ActualEnterDockTime.HasValue &&
                            s.Status != "Completed")
                .ToListAsync();

            if (!schedules.Any()) return;

            var scheduleIds = schedules.Select(s => s.ScheduleId).ToList();

            // Load all linked prep records for these schedules in one shot
            var allLinked = await _context.PreparationRecords
                .Where(p => p.ScheduleId != null && scheduleIds.Contains(p.ScheduleId!.Value) && p.Remark == "Match")
                .ToListAsync();

            int fixed_count = 0;
            bool recordsUpdated = false;

            foreach (var sched in schedules)
            {
                bool isAdm = sched.Customer != null && 
                             (string.Equals(sched.Customer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                              string.Equals(sched.Customer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                sched.TotalActualQuantity = 0;

                // Group items by ItemId to handle duplicates
                var groupedItems = sched.DeliveryItems
                    .Where(di => di.Item != null)
                    .GroupBy(di => di.ItemId)
                    .ToList();

                foreach (var group in groupedItems)
                {
                    var itemId = group.Key;
                    var relatedItems = group.ToList();
                    var firstItem = relatedItems.First();
                    var item = firstItem.Item!;
                    int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;

                    // Build tag candidates for this group of duplicate items
                    var tagCandidates = new List<string>();
                    foreach (var ri in relatedItems)
                    {
                        var riTags = VinHelper.GetItemTags(item.VIN, item.ItemCode, ri.ExternalPartNo ?? item.CustomerPartNumber);
                        tagCandidates.AddRange(riTags);
                    }
                    tagCandidates = tagCandidates
                        .Where(t => !string.IsNullOrEmpty(t))
                        .Select(t => t.Trim().ToUpper())
                        .Distinct()
                        .ToList();

                    var itemScans = allLinked
                        .Where(p => p.ScheduleId == sched.ScheduleId &&
                                    tagCandidates.Contains(p.Tag))
                        .OrderBy(p => p.CreatedDate)
                        .ToList();

                    // Saring kanban cross-manifest (Bug 1 & Bug 2) - Hanya untuk ADM, dan lakukan detach record jika tidak cocok
                    if (isAdm)
                    {
                        var invalidScans = itemScans
                            .Where(p => !VinHelper.IsKanbanBelongsToManifest(p.Kanban, sched.ScheduleNumber))
                            .ToList();
                        if (invalidScans.Any())
                        {
                            foreach (var ms in invalidScans)
                            {
                                ms.ScheduleId = null;
                                ms.ManifestNumber = VinHelper.ExtractManifestFromKanban(ms.Kanban, sched.ScheduleNumber) ?? ms.ManifestNumber;
                            }
                            recordsUpdated = true;
                            itemScans = itemScans.Where(p => p.ScheduleId != null).ToList();
                        }
                    }

                    var uniqueRecords = new List<PreparationRecord>();
                    var seenBarcodes = new HashSet<string>();

                    foreach (var r in itemScans)
                    {
                        if (!string.IsNullOrEmpty(r.Kanban))
                        {
                            var kbnUpper = r.Kanban.Trim().ToUpper();
                            if ((kbnUpper.StartsWith("KBN") || kbnUpper.StartsWith("DN")) && kbnUpper.Length >= 12)
                            {
                                if (seenBarcodes.Contains(kbnUpper))
                                {
                                    continue;
                                }
                                seenBarcodes.Add(kbnUpper);
                            }
                        }
                        uniqueRecords.Add(r);
                    }

                    int totalScans = uniqueRecords.Count;
                    int scansRemaining = totalScans;

                    foreach (var ri in relatedItems)
                    {
                        int targetKbn = (int)Math.Ceiling((double)ri.Quantity / qpc);
                        int assignedScans = Math.Min(scansRemaining, targetKbn);
                        scansRemaining -= assignedScans;

                        decimal correctQty = Math.Min((decimal)assignedScans * qpc, ri.Quantity);
                        if (ri.ActualQuantity != correctQty)
                        {
                            ri.ActualQuantity = correctQty;
                            fixed_count++;
                        }
                        ri.IsCompleted = (ri.ActualQuantity ?? 0) >= ri.Quantity;
                        sched.TotalActualQuantity += ri.ActualQuantity ?? 0;
                    }
                }
            }

            if (fixed_count > 0 || recordsUpdated)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("RecalcActualQty: fixed {Count} DeliveryItem(s), recordsUpdated={Updated}.", fixed_count, recordsUpdated);
            }
        }

        /// <summary>
        /// Tandai jadwal yang sudah melewati due time (atau sudah lintas hari) sebagai "Delayed".
        /// Dipanggil dari background service setiap 5 menit agar status DB selalu ter-update
        /// meskipun tidak ada user yang load halaman dashboard.
        /// </summary>
        public async Task AutoMarkDelayedAsync()
        {
            var now = DateTime.Now;
            var candidates = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.Status != "Completed" && s.Status != "Cancelled" && !s.ActualEndTime.HasValue)
                .ToListAsync();

            bool anyUpdated = false;
            foreach (var s in candidates)
            {
                if (ShouldBeDelayed(s, now) &&
                    !string.Equals(s.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                {
                    s.Status = "Delayed";
                    anyUpdated = true;
                }
            }

            if (anyUpdated)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("AutoMarkDelayed: marked {Count} schedule(s) as Delayed.", candidates.Count(s => s.Status == "Delayed"));
                await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "delayed" });
            }
        }

        private static bool ShouldBeDelayed(DeliverySchedule s, DateTime referenceNow)
        {
            if (s.ActualEndTime.HasValue) return false;
            if (string.Equals(s.Status, "Completed", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(s.Status, "Cancelled",  StringComparison.OrdinalIgnoreCase)) return false;

            var dueAt = GetScheduleDueTime(s);
            // Non-cross-day: langsung delayed saat hari berganti
            var isCrossDay = dueAt.HasValue && dueAt.Value.Date > s.ScheduledDate.Date;
            if (!isCrossDay && s.ScheduledDate.Date < referenceNow.Date) return true;
            // Cross-day (overnight): delayed saat waktu aktual melewati due time
            return dueAt.HasValue && referenceNow > dueAt.Value;
        }

        private static DateTime? GetScheduleDueTime(DeliverySchedule s)
        {
            if (s.PickupTime.HasValue)    return NormalizeTarget(s, s.PickupTime.Value);
            if (s.ETD.HasValue)           return NormalizeTarget(s, s.ETD.Value);
            if (s.EnterDockTime.HasValue) return NormalizeTarget(s, s.EnterDockTime.Value);
            return s.ScheduledDate.Date.AddDays(1).AddSeconds(-1);
        }

        private static DateTime NormalizeTarget(DeliverySchedule s, DateTime target)
        {
            var normalized = s.ScheduledDate.Date.Add(target.TimeOfDay);
            var startMin   = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
            if (startMin > 0)
            {
                var startAt = s.ScheduledDate.Date.AddMinutes(startMin);
                if (normalized < startAt) normalized = normalized.AddDays(1);
            }
            return normalized;
        }
    }
}
