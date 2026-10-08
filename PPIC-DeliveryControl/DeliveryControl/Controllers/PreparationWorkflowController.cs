using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using DeliveryControl.Helpers;
using DeliveryControl.Filters;
using DeliveryControl.Services;
using Microsoft.Extensions.Caching.Memory;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.Authorize]
    [AuthorizeRoles("Admin", "Preparation", "User")]
    public class PreparationWorkflowController : Controller
    {
        private static readonly System.Threading.SemaphoreSlim _scanLock = new System.Threading.SemaphoreSlim(1, 1);

        private readonly ApplicationDbContext _context;
        private readonly IHubContext<StockHub> _stockHubContext;
        private readonly IHubContext<DeliveryHub> _deliveryHubContext;
        private readonly DeliveryControl.Services.ActivityLogService _logService;
        private readonly PreparationSyncService _syncService;
        private readonly DeliveryControl.Services.StockCacheService _stockCache;
        private readonly IMemoryCache _memoryCache;

        public PreparationWorkflowController(
            ApplicationDbContext context, 
            IHubContext<StockHub> stockHubContext,
            IHubContext<DeliveryHub> deliveryHubContext,
            DeliveryControl.Services.ActivityLogService logService,
            PreparationSyncService syncService,
            DeliveryControl.Services.StockCacheService stockCache,
            IMemoryCache memoryCache)
        {
            _context = context;
            _stockHubContext = stockHubContext;
            _deliveryHubContext = deliveryHubContext;
            _logService = logService;
            _syncService = syncService;
            _stockCache = stockCache;
            _memoryCache = memoryCache;
        }

        private async Task<string?> GetSystemSettingAsync(string key)
        {
            return await _memoryCache.GetOrCreateAsync($"SystemSetting_{key}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                var setting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
                return setting?.Value;
            });
        }

        public async Task<IActionResult> CleanupInvalidSyncs()
        {
            var result = await _syncService.CleanupExistingInvalidSyncsAsync();
            return Json(new { 
                success = true, 
                message = $"Cleanup selesai. {result.Item1} jadwal terdampak, {result.Item2} kanban dikosongkan (dikembalikan ke Pending).",
                details = result
            });
        }

        private async Task<HashSet<string>> GetAllowedDockCodesAsync()
        {
            // PENTING: gunakan Session, bukan Claims — sistem ini session-based
            var sessionRole = HttpContext.Session.GetString("Role");
            var userIdStr = HttpContext.Session.GetString("UserId");
            
            if (sessionRole == "Admin") return new HashSet<string>();
            
            if (int.TryParse(userIdStr, out int userId))
            {
                var dockData = await _context.UserDockAccesses
                    .Where(uda => uda.UserId == userId)
                    .Select(uda => new { uda.Dock.DockCode, uda.Dock.DockName })
                    .ToListAsync();

                // Kembalikan set gabungan DockCode + DockName dalam uppercase
                return dockData
                    .SelectMany(d => new[] { d.DockCode ?? "", d.DockName ?? "" })
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToUpper())
                    .ToHashSet();
            }
            
            return new HashSet<string>();
        }

        public async Task<IActionResult> Index(DateTime? filterDate, string? vin)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            var isAdmin = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Admin" || roleStr == "Admin" || roleStr == "Super Admin";
            var sessionRole = HttpContext.Session.GetString("Role");
            var isAdminOrPrep = isAdmin || sessionRole == "Preparation";
            var isUser = sessionRole == "User";
            var allowedDockCodes = await GetAllowedDockCodesAsync();
            
            ViewData["TargetVin"] = vin; // Simpan untuk UI
            ViewData["IsAdmin"] = isAdmin; // Untuk toggle Mode Ketik Manual & Skip Stock
            ViewData["IsAdminOrPrep"] = isAdminOrPrep; // Untuk indicator Skip Stock (tampil semua role)
            ViewData["IsUser"] = isUser; // Role User: read-only
            // Status Skip Stock dari DB SystemSettings (global — berlaku untuk semua role/user)
            var skipSettingVal = await GetSystemSettingAsync("SkipStockValidation");
            ViewData["SkipStockActive"] = skipSettingVal == "1";

            var validateProdIntSettingVal = await GetSystemSettingAsync("ValidateProdIntLabel");
            ViewData["ValidateProdIntLabelActive"] = string.IsNullOrEmpty(validateProdIntSettingVal) || validateProdIntSettingVal == "1";

            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            if (!string.IsNullOrEmpty(vin))
            {
                query = query.Where(s => s.ScheduleNumber.Contains(vin) || s.DeliveryItems.Any(di => di.Item.ItemCode.Contains(vin)));
            }

            if (filterDate.HasValue)
            {
                var dStart = filterDate.Value.Date;
                var dEnd = dStart.AddDays(1);
                query = query.Where(s => s.ScheduledDate >= dStart && s.ScheduledDate < dEnd);
                ViewData["CurrentFilterDate"] = filterDate.Value.ToString("yyyy-MM-dd");
                ViewData["FilterTitle"] = "Jadwal Tanggal " + filterDate.Value.ToString("dd MMM yyyy");
            }
            else
            {
                // Default: Today and Tomorrow, plus uncompleted carry-over schedules
                var tomorrow = today.AddDays(1);
                var dEnd = tomorrow.AddDays(1);
                query = query.Where(s => (s.ScheduledDate >= today && s.ScheduledDate < dEnd) || 
                                         (s.ScheduledDate < today && (s.Status != "Completed" || s.PreparationStatus != "Prepared" || s.DeliveryItems.Any(di => (di.ActualQuantity ?? 0) < di.Quantity))));
                ViewData["CurrentFilterDate"] = "";
                ViewData["FilterTitle"] = "Jadwal Hari Ini & Besok";
            }

            // Terapkan filter dock di memory SETELAH filter tanggal dan VIN (menghindari double query ToListAsync pada table besar)
            var rawSchedules = await query.ToListAsync();

            if (!isAdmin && allowedDockCodes.Any())
            {
                rawSchedules = rawSchedules
                    .Where(s =>
                        allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                        (s.Customer != null && (
                            allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                    .ToList();
            }



            // Self-Correct Status for Old Data (In-Memory Fix)
            foreach (var s in rawSchedules)
            {
                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.Status != "Completed" || s.PreparationStatus != "Prepared")
                    {
                        s.Status = "Completed";
                        s.PreparationStatus = "Prepared";
                    }
                }
            }

            var schedules = rawSchedules
                .OrderBy(s => ((s.Status == "Preparing" || s.Status == "In Progress" || s.PreparationStatus == "Preparing" || s.PreparationStatus == "In Progress") && s.PreparationStatus != "Prepared" && s.Status != "Completed") ? 0 : 
                              (s.Status == "Completed" || s.PreparationStatus == "Prepared") ? 2 : 1) // 0=Top, 1=Scheduled, 2=Bottom
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .ToList();
            
            return View(schedules);
        }

        /// <summary>
        /// Admin-only: Aktifkan/nonaktifkan Skip Stock Validation secara global (via DB SystemSettings).
        /// Berlaku untuk SEMUA user/role yang login karena disimpan di database.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SetSkipStockMode([FromBody] SetSkipStockModeRequest req)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";
            if (!isAdmin)
                return Json(new { success = false, message = "Hanya Admin yang bisa mengaktifkan mode ini." });

            const string key = "SkipStockValidation";
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSetting { Key = key, Description = "Skip validasi stock habis saat preparation (diaktifkan Admin)" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.Active ? "1" : "0";
            await _context.SaveChangesAsync();
            _memoryCache.Remove($"SystemSetting_{key}");

            return Json(new { success = true, active = req.Active });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SetValidateProdIntLabelMode([FromBody] SetValidateProdIntLabelModeRequest req)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";
            if (!isAdmin)
                return Json(new { success = false, message = "Hanya Admin yang bisa mengaktifkan mode ini." });

            const string key = "ValidateProdIntLabel";
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSetting { Key = key, Description = "Validasi duplikat label untuk part PROD Internal" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.Active ? "1" : "0";
            await _context.SaveChangesAsync();
            _memoryCache.Remove($"SystemSetting_{key}");

            return Json(new { success = true, active = req.Active });
        }

        [HttpGet]
        public async Task<IActionResult> LookupSchedule(string tag, string? label, string? kanban, bool skipStockValidation = false, string? manifestNumber = null, int? dockCustomerId = null, string? dockCycle = null)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Json(new { success = false });

            // Override skipStockValidation dengan nilai dari DB SystemSettings (di-set oleh Admin)
            // Agar role Preparation pun bisa bypass karena flag disimpan di DB (bukan per-session)
            if (!skipStockValidation)
            {
                var skipSettingVal = await GetSystemSettingAsync("SkipStockValidation");
                if (skipSettingVal == "1") skipStockValidation = true;
            }

            try
            {
                tag = tag.Trim();
                label = (label ?? "").Trim();
                kanban = (kanban ?? "").Trim();

                // 1. Find ALL Items by Tag (Internal) - Supporting Flexible LB Prefix/Suffix
                var normalizedTag = VinHelper.Normalize(tag);
                // Disambiguasi Molded vs Hose: raw tag "NA2910X" (tanpa LB) → Molded, "NA2910LBX" → Hose
                var rawTagUp = tag.Trim().ToUpper();
                bool lookupWithLB = rawTagUp.TrimEnd('X').EndsWith("LB");

                var items = await _context.Items
                    .Include(i => i.RackLocations)
                    .Where(i => (i.VIN != null && i.VIN != "" && (i.VIN.Contains(normalizedTag) || normalizedTag.Contains(i.VIN))) || 
                                (i.ItemCode != null && i.ItemCode != "" && (i.ItemCode.Contains(normalizedTag) || normalizedTag.Contains(i.ItemCode))))
                    .ToListAsync();
                
                // Filter in-memory for exact flexible match
                items = items.Where(i => VinHelper.IsMatch(i.VIN, tag) || VinHelper.IsMatch(i.ItemCode, tag) || VinHelper.IsLabelContainsVin(tag, i.VIN)).ToList();

                // Prioritaskan item yang bukan "No Order" sebelum melakukan disambiguasi
                var validItems = items.Where(i => !string.Equals(i.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase)).ToList();
                if (validItems.Any())
                {
                    items = validItems;
                }

                // Jika ada lebih dari 1 item match (misal NA2910 Molded dan NA2910LB Hose),
                // pilih yang sesuai suffix LB dari raw scan agar tidak salah plant
                if (items.Count > 1)
                {
                    var preferred = lookupWithLB
                        ? items.Where(i => (i.VIN ?? "").Trim().ToUpper().EndsWith("LB")).ToList()
                        : items.Where(i => !(i.VIN ?? "").Trim().ToUpper().EndsWith("LB")).ToList();
                    if (preferred.Any()) items = preferred;
                }
                
                if (!items.Any())
                {
                    // Fallback to pulling records
                    var pulling = await _context.PullingRecords
                        .OrderByDescending(p => p.CreatedDate)
                        .FirstOrDefaultAsync(p => p.Tag == normalizedTag);
                    if (pulling != null)
                    {
                        var pItem = await _context.Items
                            .Include(i => i.RackLocations)
                            .FirstOrDefaultAsync(i => i.ItemId == pulling.ItemId);
                        if (pItem != null) items.Add(pItem);
                    }
                }

                if (!items.Any())
                {
                    return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });
                }

                // Jika masih ada item tapi statusnya No Order (kasus di mana validItems.Any() tadi false), tolak
                if (items.All(i => string.Equals(i.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase)))
                {
                    return Json(new { success = false, message = "item tidak ada order" });
                }

                // Prioritas: customer NON-EKS (HMMI) didahulukan dari EKS (HMMI EKS)
                items = ItemPriorityHelper.PrioritizeNonEks(items);

                // 2. Filter Items by active Schedules (with Dock Access Control)
                var roleStr = HttpContext.Session.GetString("Role");
                var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";
                var allowedDockCodes = await GetAllowedDockCodesAsync();

                var scheduleQuery = _context.DeliverySchedules
                    .Where(s => s.Status == "Scheduled" || s.Status == "In Progress" || s.Status == "Shortage Delivery");

                if (dockCustomerId.HasValue && dockCustomerId.Value > 0)
                {
                    scheduleQuery = scheduleQuery.Where(s => s.CustomerId == dockCustomerId.Value);
                }

                if (!string.IsNullOrEmpty(dockCycle) && dockCycle != "-")
                {
                    scheduleQuery = scheduleQuery.Where(s => s.Cycle == dockCycle);
                }

                if (!isAdmin && allowedDockCodes.Any())
                {
                    var allSchedList = await scheduleQuery.Include(s => s.Customer).ToListAsync();
                    var matchSchedIds = allSchedList
                        .Where(s =>
                            allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                            (s.Customer != null && (
                                allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                                allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                                allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                        .Select(s => s.ScheduleId).ToHashSet();
                    scheduleQuery = scheduleQuery.Where(s => matchSchedIds.Contains(s.ScheduleId));
                }

                var activeScheduleItemIds = await scheduleQuery
                    .SelectMany(s => s.DeliveryItems)
                    .Select(di => di.ItemId)
                    .Distinct()
                    .ToListAsync();

                var itemsInSchedule = ItemPriorityHelper.PrioritizeNonEks(items.Where(i => activeScheduleItemIds.Contains(i.ItemId)).ToList());
                
                // If NO items in schedule, pick the first one (sudah diurutkan non-EKS lebih dulu)
                var targetItem = itemsInSchedule.FirstOrDefault() ?? items.First();

                // Rack locations: akan diisi setelah FIFO dihitung (gunakan actual FG stock)
                List<object> rackLocations;

                // Calculate current stock using FIFO — same logic as Dashboard FG (GetStockViewModelInternal)
                // Bug Fix #1: exclude Mismatch records (previously counted raw totals)
                // Bug Fix #2: match by Tag/VIN across ALL items with same VIN, not just targetItem.ItemId
                var vinUpper = (targetItem.VIN ?? "").Trim().ToUpper();
                var itemIds = items.Select(i => i.ItemId).ToList();
                var allPullingForVinAll = await _context.PullingRecords
                    .Where(p => 
                        ((p.ItemId.HasValue && itemIds.Contains(p.ItemId.Value)) || ((p.Tag ?? "").ToUpper() == vinUpper))
                        && p.Remark != "Mismatch"
                        && (p.AdjustNote ?? "") != "Opname Reduce"
                    )
                    .OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId)
                    .ToListAsync();
                    
                // Only consider non-zero stock items for active stock / FIFO matching
                var allPullingForVin = allPullingForVinAll.Where(p => (p.AdjustNote ?? "") != "Zero Stock" && (p.AdjustNote ?? "") != "Opname Reduce").ToList();
                
                var allPrepForVin = await _context.PreparationRecords
                    .Where(r => (r.Tag ?? "").ToUpper() == vinUpper && r.Remark != "Mismatch")
                    .OrderBy(r => r.CreatedDate).ThenBy(r => r.PreparationId)
                    .ToListAsync();
                // Cek apakah VIN ini unik di master data (untuk relaxed fallback)
                // Pre-compute normalized VIN as local string (EF Core cannot translate VinHelper.Normalize() to SQL)
                var normalizedVinUpper = VinHelper.Normalize(vinUpper);
                var activeItemCountForVin = await _context.Items
                    .CountAsync(i => i.IsActive && !i.IsDeleted && i.VIN != null &&
                                    (i.VIN.ToUpper() == vinUpper || i.VIN.ToUpper() == normalizedVinUpper));
                bool isUniqueVin = activeItemCountForVin == 1;

                // FIFO matching: pasangkan setiap Prep ke Pulling tertua dengan Tag+Label yang sama
                var consumedPullingIds = new HashSet<int>();
                foreach (var prep in allPrepForVin)
                {
                    var prepLabel = (prep.Label ?? "").Trim().ToUpper();
                    // Prioritas 1: Tag + Label exact match (FIFO normal)
                    var match = allPullingForVin.FirstOrDefault(p =>
                        !consumedPullingIds.Contains(p.PullingId) &&
                        (p.Label ?? "").Trim().ToUpper() == prepLabel &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                    // Prioritas 2: Fallback label compatible (menggunakan VinHelper.Normalize)
                    var normPrepLabel = VinHelper.Normalize(prepLabel);
                    bool labelCompatibleMatch = !string.IsNullOrEmpty(prepLabel) && allPullingForVin.Any(p =>
                        !consumedPullingIds.Contains(p.PullingId) &&
                        (p.AdjustNote ?? "") != "Zero Stock" &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                        (VinHelper.Normalize(p.Label) == normPrepLabel ||
                         VinHelper.Normalize(p.Label).StartsWith(normPrepLabel) ||
                         normPrepLabel.StartsWith(VinHelper.Normalize(p.Label))));

                    if (match == null)
                    {
                        // Jika labelCompatibleMatch: prioritaskan pulling yang label-nya compatible
                        if (labelCompatibleMatch)
                        {
                            match = allPullingForVin.FirstOrDefault(p =>
                                !consumedPullingIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                                (VinHelper.Normalize(p.Label) == normPrepLabel ||
                                 VinHelper.Normalize(p.Label).StartsWith(normPrepLabel) ||
                                 normPrepLabel.StartsWith(VinHelper.Normalize(p.Label))));
                        }
                        // Fallback jika label beda: ambil pulling tertua yang tersedia
                        if (match == null)
                        {
                            match = allPullingForVin.FirstOrDefault(p =>
                                !consumedPullingIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                        }
                    }
                    if (match != null) consumedPullingIds.Add(match.PullingId);
                }
                var currentStock = allPullingForVin.Count(p => !consumedPullingIds.Contains(p.PullingId));

                // ── Rack locations: UNION of all possible sources ──
                var inStockPieces = allPullingForVin.Where(p => !consumedPullingIds.Contains(p.PullingId)).ToList();
                var rackDict = new Dictionary<string, object>();
                
                Action<string, int, string, int> AddRack = (r, nr, p, id) => {
                    var key = $"{r}_{nr}";
                    if (!rackDict.ContainsKey(key))
                    {
                        rackDict[key] = new { rack = r, noRack = nr, plant = p, itemId = id };
                    }
                };

                // 1. Racks dari stock nyata (active)
                foreach(var p in inStockPieces)
                {
                    AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", p.ItemId ?? targetItem.ItemId);
                }

                // 2. Zero Stock markers (selalu muncul)
                foreach(var p in allPullingForVinAll.Where(x => (x.AdjustNote ?? "").Contains("Zero Stock") && !(x.AdjustNote ?? "").Contains("Rack Hidden")))
                {
                    AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", targetItem.ItemId);
                }

                // 3. Historical racks DIHAPUS SESUAI PERMINTAAN USER (TIDAK DITAMPILKAN LAGI WALAUPUN ADA STOK AKTIF)
                // if (currentStock > 0)
                // {
                //     foreach(var p in allPullingForVinAll.Where(x => !(x.AdjustNote ?? "").Contains("Rack Hidden")))
                //     {
                //         AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", targetItem.ItemId);
                //     }
                // }


                rackLocations = rackDict.Values
                    .OrderBy(r => ((dynamic)r).rack)
                    .ThenBy(r => ((dynamic)r).noRack)
                    .ToList();

                // --- EARLY VALIDATION: Check for empty stock right away ---
                if (!skipStockValidation && currentStock <= 0)
                {
                    return Json(new { 
                        success = false, 
                        message = $"STOCK HABIS! ({targetItem.ItemName}) Tidak bisa lanjut scan." 
                    });
                }

                // --- Ambil external PartNo dari ItemMappings (sekali, dipakai label & kanban validation) ---
                // Gunakan VinHelper untuk flexible match karena VIN di ItemMappings mungkin disimpan dengan format berbeda
                string normalizedTargetVin = VinHelper.Normalize(targetItem.VIN);
                var allItemMappings = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                // Filter di memory dengan flexible VIN matching
                var matchedMappings = allItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.VIN) && 
                                (VinHelper.Normalize(m.VIN) == normalizedTargetVin || 
                                 VinHelper.IsMatch(m.VIN, targetItem.VIN)))
                    .ToList();
                var externalPartNos = matchedMappings
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosUpper = externalPartNos.Select(p => p.ToUpper()).ToList();

        DeliverySchedule? schedule = null;
        var today = DateTime.Today;

        // ── TWO POINT CHECK DETECTION (Berdasarkan Konfigurasi Customer) ────────
        // Jika customer No Kanban (misal AHM), user cukup scan Rak & Label (Otomatis skip Kanban)
        var noKanbanSettingVal = await GetSystemSettingAsync("TwoPointCheck_CustomerCodes");
        var noKanbanCodes = noKanbanSettingVal?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim().ToUpper()).ToList() ?? new List<string>();

        bool isTwoPointCheck = false;
        {
            // ── PRIORITAS 0: Cek dari dock yang dipilih user (dockCustomerId) ─────────────────
            // Jika user memilih dock yang sudah dikonfigurasi TPC, semua scan di dock itu TPC.
            // Ini menangani kasus item tidak punya field Customer atau mapping yang cocok.
            if (!isTwoPointCheck && dockCustomerId.HasValue && dockCustomerId.Value > 0 && noKanbanCodes.Any())
            {
                var dockCustomer = await _context.Customers.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CustomerId == dockCustomerId.Value);
                if (dockCustomer != null)
                {
                    string dcCode = (dockCustomer.CustomerCode ?? "").Trim().ToUpper();
                    string dcName = (dockCustomer.CustomerName ?? "").Trim().ToUpper();
                    if (noKanbanCodes.Any(nc =>
                        dcCode == nc || dcCode.Contains(nc) || nc.Contains(dcCode) ||
                        dcName == nc || dcName.Contains(nc) || nc.Contains(dcName)))
                    {
                        isTwoPointCheck = true;
                    }
                }
            }

            // ── PRIORITAS 1: Cek langsung dari Item.Customer (field master item) ──────────────
            // Ini adalah cara paling cepat dan akurat — tidak bergantung pada histori jadwal.
            // Item.Customer berisi kode customer yang di-assign saat import/mapping master data.
            if (!string.IsNullOrWhiteSpace(targetItem.Customer))
            {
                var itemCustUpper = targetItem.Customer.Trim().ToUpper();
                if (noKanbanCodes.Any(nc => itemCustUpper == nc || itemCustUpper.Contains(nc) || nc.Contains(itemCustUpper)))
                {
                    isTwoPointCheck = true;
                }
            }

            // ── PRIORITAS 2: Cek dari ItemMappings.Customer (jika Item.Customer kosong) ─────
            if (!isTwoPointCheck && matchedMappings.Any())
            {
                var mappingCustomers = matchedMappings
                    .Select(m => (m.Customer ?? "").Trim().ToUpper())
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Distinct()
                    .ToList();

                if (mappingCustomers.Any(mc => noKanbanCodes.Any(nc => mc == nc || mc.Contains(nc) || nc.Contains(mc))))
                {
                    isTwoPointCheck = true;
                }
            }

            // ── PRIORITAS 3: Fallback histori DeliveryItems (item sudah pernah ada di jadwal) ─
            if (!isTwoPointCheck)
            {
                var potentialTpcCustomers = await _context.DeliveryItems
                    .Where(di => di.ItemId == targetItem.ItemId)
                    .Select(di => di.DeliverySchedule.Customer.CustomerCode)
                    .Distinct()
                    .ToListAsync();

                if (potentialTpcCustomers.Any(custCode => noKanbanCodes.Any(nc =>
                    !string.IsNullOrEmpty(custCode) &&
                    (custCode.ToUpper().Contains(nc) || nc.Contains(custCode.ToUpper())))))
                {
                    isTwoPointCheck = true;
                }
            }
        }


        if (!string.IsNullOrEmpty(kanban))
        {
            string kanbanUpper = kanban.ToUpper();

            // Bypass validasi kanban untuk customer TwoPointCheck — kanban "TWO-POINT-CHECK" adalah marker khusus
            // yang dikirim client saat customer dikonfigurasi no-kanban. Langsung skip ke schedule lookup.
            bool isTwoPointMarker = isTwoPointCheck && kanbanUpper == "TWO-POINT-CHECK";

            // Jika ada external PartNo dari ItemMappings → validasi UTAMA kanban
            // Jika tidak ada → fallback ke PartNo / VIN / ItemCode dari master Item
            bool kanbanValid;
            List<string> expectedKanbanCodes;

            if (isTwoPointMarker)
            {
                // TwoPointCheck customer: kanban "TWO-POINT-CHECK" selalu valid, skip validasi
                kanbanValid = true;
                expectedKanbanCodes = new List<string>();
            }
            else if (!string.IsNullOrEmpty(manifestNumber) &&
                     manifestNumber.Trim().ToUpper().StartsWith("DN") &&
                     kanbanUpper.StartsWith("DN") &&
                     kanbanUpper.StartsWith(manifestNumber.Trim().ToUpper()))
            {
                // Shortcut: kanban berformat Delivery Note (DN) yang diawali nomor manifest → valid.
                // Pada sistem KBN, kanban barcode adalah nomor delivery note + serial lot, BUKAN nomor part.
                // Contoh: manifest=DN4126050032438A, kanban=DN4126050032438ANX-2516000002 → valid.
                // Keamanan dijamin oleh manifest-kanban DN check di bawah (kanban harus diawali manifest persis).
                // Tidak perlu cek VIN/PartNo karena manifest sudah terikat ke item melalui schedule.
                kanbanValid = true;
                expectedKanbanCodes = new List<string>();
            }
            else if (externalPartNosUpper.Any())
            {
                kanbanValid = externalPartNosUpper.Any(ep => kanbanUpper == ep || kanbanUpper.Contains(ep) || (ep.StartsWith("IRM-") && kanbanUpper.Contains(ep.Substring(4))));
                expectedKanbanCodes = externalPartNos;
            }
            else
            {
                string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();
                string itemVin = (targetItem.VIN ?? "").ToUpper();
                bool matchPartNo = !string.IsNullOrEmpty(partNo)  && (kanbanUpper == partNo || kanbanUpper.Contains(partNo) || (partNo.StartsWith("IRM-") && kanbanUpper.Contains(partNo.Substring(4))));
                bool matchVin    = !string.IsNullOrEmpty(itemVin) && (kanbanUpper == itemVin || kanbanUpper.Contains(itemVin));
                bool matchCode   = !string.IsNullOrEmpty(targetItem.ItemCode) && kanbanUpper == targetItem.ItemCode.ToUpper();
                kanbanValid = matchPartNo || matchVin || matchCode;
                expectedKanbanCodes = new List<string>(
                    new[] { targetItem.CustomerPartNumber, targetItem.VIN, targetItem.ItemCode }
                    .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Take(1));
            }

            if (kanbanValid)
            {
                // Validasi ekstra: kanban barcode harus sesuai manifest yang dipilih.
                // Format DN (Delivery Note): kanban dimulai dengan nomor manifest → bisa divalidasi langsung.
                // Contoh: manifest=DN4126050032436A, kanban=DN4126050032436AGT-5297000001 → valid.
                //          kanban=DN4126050032438AGT-5297000001 (C1 manifest) → TOLAK.
                // Ini mencegah operator salah scan kanban C1 saat menyiapkan manifest C2.
                if (!isTwoPointMarker &&
                    !string.IsNullOrEmpty(manifestNumber) &&
                    manifestNumber.Trim().ToUpper().StartsWith("DN") &&
                    kanbanUpper.StartsWith("DN") &&
                    !kanbanUpper.StartsWith(manifestNumber.Trim().ToUpper()))
                {
                    // Coba manifest lain: satu item bisa ada di beberapa DN aktif pada hari yang sama
                    // (misal DN...542A dan DN...544A untuk part yang sama di pengiriman berbeda)
                    var altManifests = await _context.DeliverySchedules
                        .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress" || s.Status == "Shortage Delivery") &&
                                     s.ScheduledDate.Date >= today.AddDays(-2) &&
                                     s.ScheduleNumber.ToUpper().StartsWith("DN") &&
                                     s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId))
                        .Select(s => s.ScheduleNumber)
                        .ToListAsync();
                    var correctManifest = altManifests.FirstOrDefault(m => kanbanUpper.StartsWith(m.Trim().ToUpper()));
                    if (correctManifest != null)
                    {
                        // Kanban cocok dengan DN lain yang valid → ganti manifest, lanjut tanpa error
                        manifestNumber = correctManifest;
                    }
                    else
                    {
                        // Jadwal belum diupload: Ekstrak manifest dari kanban agar bisa masuk PENDING
                        manifestNumber = VinHelper.ExtractManifestFromKanban(kanbanUpper, manifestNumber);
                    }
                }

                var baseQuery = _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress" || s.Status == "Shortage Delivery") &&
                                 s.ScheduledDate.Date >= today.AddDays(-2) &&
                                 s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                if (dockCustomerId.HasValue && dockCustomerId.Value > 0)
                {
                    baseQuery = baseQuery.Where(s => s.CustomerId == dockCustomerId.Value);
                }

                if (!string.IsNullOrEmpty(dockCycle) && dockCycle != "-")
                {
                    baseQuery = baseQuery.Where(s => s.Cycle == dockCycle);
                }

                if (!string.IsNullOrEmpty(manifestNumber))
                {
                    var manifestUpper = manifestNumber.Trim().ToUpper();
                    schedule = await baseQuery.Where(s => s.ScheduleNumber.ToUpper() == manifestUpper).FirstOrDefaultAsync();
                }
                else
                {
                    schedule = await baseQuery
                        .OrderBy(s => s.ScheduledDate)
                        .ThenBy(s => s.ScheduleNumber)
                        .FirstOrDefaultAsync();
                }
            }
            else
            {
                var validCodesStr = string.Join(" / ", expectedKanbanCodes.Distinct());
                return Json(new {
                    success = true,
                    found   = false,
                    step    = "kanban",
                    item = new {
                        itemName           = targetItem.ItemName,
                        vin                = targetItem.VIN,
                        customerPartNumber = targetItem.CustomerPartNumber,
                        qtyLot             = targetItem.QtyLot ?? 0,
                        currentStock       = currentStock,
                        externalPartNos    = externalPartNos
                    },
                    message = $"KANBAN tidak sesuai! Barcode harus mengandung: {(string.IsNullOrEmpty(validCodesStr) ? "Part No terdaftar" : validCodesStr)}",
                    isTwoPointCheck = isTwoPointCheck
                });
            }
        }

        if (!string.IsNullOrEmpty(label))
        {
            string labelUpper = label.ToUpper();
            string vin = (targetItem.VIN ?? "").ToUpper();
            string partNo = (targetItem.CustomerPartNumber ?? "").ToUpper();

            // Label valid jika mengandung VIN (dengan berbagai format), CustomerPartNumber,
            // ATAU salah satu external PartNo dari ItemMappings
            bool labelValid = VinHelper.IsLabelContainsVin(label, targetItem.VIN)
                || (!string.IsNullOrEmpty(partNo) && labelUpper.Contains(partNo))
                || externalPartNosUpper.Any(ep => labelUpper.Contains(ep));

            if (!labelValid)
            {
                var normalizedVinDisplay = VinHelper.Normalize(targetItem.VIN);
                return Json(new { 
                    success = true, 
                    found = false, 
                    step = "label",
                    item = new { 
                        itemName = targetItem.ItemName, 
                        vin = targetItem.VIN, 
                        customerPartNumber = targetItem.CustomerPartNumber,
                        qtyLot = targetItem.QtyLot ?? 0,
                        currentStock = currentStock,
                        externalPartNos = externalPartNos
                    },
                    message = $"❌ LABEL TIDAK VALID! Label harus mengandung kode VIN: \"{normalizedVinDisplay}\". Scan ulang label yang benar.",
                    isTwoPointCheck = isTwoPointCheck
                });
            }
        }

        // If not all 3 are provided, we don't look for schedule yet
        // Exception: TwoPointCheck customer — kanban boleh kosong atau "TWO-POINT-CHECK", cukup tag+label
        bool isTwoPointAndLabelReady = isTwoPointCheck && !string.IsNullOrEmpty(label);
        bool kanbanIsEffectivelyEmpty = string.IsNullOrEmpty(kanban) || kanban.Equals("TWO-POINT-CHECK", StringComparison.OrdinalIgnoreCase);

        if (!isTwoPointAndLabelReady && (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(kanban)))
        {
            return Json(new { 
                success = true, 
                found = false, 
                step = "partial",
                item = new { 
                    itemName = targetItem.ItemName, 
                    vin = targetItem.VIN, 
                    customerPartNumber = targetItem.CustomerPartNumber,
                    currentStock = currentStock,
                    externalPartNos = externalPartNos  // Dikirim ke client setelah label di-scan
                },
                rackLocations = rackLocations,
                isTwoPointCheck = isTwoPointCheck,
                message = "Dilanjutkan ke scan berikutnya..." 
            });
        }

        // Untuk TwoPointCheck: set kanban sebagai marker agar proses lanjut ke schedule lookup
        if (isTwoPointCheck && kanbanIsEffectivelyEmpty)
        {
            kanban = "TWO-POINT-CHECK";
        }

        // 4. Find FIFO Schedule if not already found via Kanban
        if (schedule == null)
        {
            var fallbackQuery = _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress" || s.Status == "Shortage Delivery") &&
                             s.ScheduledDate.Date >= today.AddDays(-2) &&
                             s.DeliveryItems.Any(di => di.ItemId == targetItem.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

            if (dockCustomerId.HasValue && dockCustomerId.Value > 0)
            {
                fallbackQuery = fallbackQuery.Where(s => s.CustomerId == dockCustomerId.Value);
            }

            if (!string.IsNullOrEmpty(dockCycle) && dockCycle != "-")
            {
                fallbackQuery = fallbackQuery.Where(s => s.Cycle == dockCycle);
            }

            schedule = await fallbackQuery
                .OrderBy(s => s.ScheduledDate)
                .ThenBy(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();
        }

        if (schedule == null)
        {
            return Json(new { 
                success = true, 
                found = false, 
                step = "final",
                item = new { 
                    itemName = targetItem.ItemName, 
                    vin = targetItem.VIN, 
                    customerPartNumber = targetItem.CustomerPartNumber,
                    currentStock = currentStock,
                    externalPartNos = externalPartNos
                },
                isTwoPointCheck = isTwoPointCheck,
                message = "Produk OK, tapi JADWAL tidak ditemukan" 
            });
        }

                var deliveryItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == targetItem.ItemId);

                return Json(new { 
                    success = true, 
                    found = true,
                    step = "complete",
                    item = new { targetItem.ItemName, targetItem.VIN, targetItem.CustomerPartNumber, targetItem.QtyLot, currentStock, externalPartNos, TargetPartNo = !string.IsNullOrEmpty(targetItem.CustomerPartNumber) ? targetItem.CustomerPartNumber : targetItem.VIN },
                    schedule = new { 
                        schedule.ScheduleId, 
                        schedule.ScheduleNumber, 
                        CustomerName = schedule.Customer?.CustomerCode,
                        Dock = schedule.Customer?.CustomerName,
                        Date = schedule.ScheduledDate.ToString("dd MMM yyyy"),
                        TargetQty = deliveryItem?.Quantity ?? 0,
                        ActualQty = deliveryItem?.ActualQuantity ?? 0
                    },
                    isTwoPointCheck = isTwoPointCheck
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Endpoint untuk mendapatkan daftar dock yang boleh diakses user saat ini.
        /// Jika HasAllDockAccess==true ATAU Admin/Super Admin → kembalikan semua Customer aktif.
        /// Jika tidak → filter berdasarkan UserDockAccess → Customer yang terhubung via Dock.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAvailableDocks()
        {
            var roleStr = HttpContext.Session.GetString("Role");
            var userIdStr = HttpContext.Session.GetString("UserId");
            var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";

            List<Customer> allowedCustomers;

            if (isAdmin)
            {
                allowedCustomers = await _context.Customers
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CustomerName)
                    .ThenBy(c => c.Cycle)
                    .ToListAsync();
            }
            else if (int.TryParse(userIdStr, out int userId))
            {
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

                if (user?.HasAllDockAccess == true)
                {
                    allowedCustomers = await _context.Customers
                        .Where(c => c.IsActive)
                        .OrderBy(c => c.CustomerName)
                        .ThenBy(c => c.Cycle)
                        .ToListAsync();
                }
                else
                {
                    var allowedCustomerIds = await _context.UserDockAccesses
                        .Where(uda => uda.UserId == userId)
                        .Join(_context.Docks,
                            uda => uda.DockId,
                            d => d.DockId,
                            (uda, d) => d.CustomerId)
                        .Distinct()
                        .ToListAsync();

                    var legacyCustomerIds = await _context.UserDocks
                        .Where(ud => ud.UserId == userId)
                        .Select(ud => ud.CustomerId)
                        .ToListAsync();

                    var allAllowedIds = allowedCustomerIds.Concat(legacyCustomerIds).Distinct().ToList();

                    allowedCustomers = await _context.Customers
                        .Where(c => c.IsActive && allAllowedIds.Contains(c.CustomerId))
                        .OrderBy(c => c.CustomerName)
                        .ThenBy(c => c.Cycle)
                        .ToListAsync();
                }
            }
            else
            {
                allowedCustomers = new List<Customer>();
            }

            var noKanbanSetting = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "TwoPointCheck_CustomerCodes");
            var noKanbanCodes = noKanbanSetting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToUpper()).ToList() ?? new List<string>();

            var result = allowedCustomers.Select(c => {
                string cCode = (c.CustomerCode ?? "").Trim().ToUpper();
                string cName = (c.CustomerName ?? "").Trim().ToUpper();
                string cCycle = (c.Cycle ?? "").Trim().ToUpper();
                
                // Broad matching: check if any TPC code is mentioned in Code, Name, or Cycle
                bool isTpc = false;
                if (noKanbanCodes.Any())
                {
                    // 1. Direct contains
                    bool directMatch = noKanbanCodes.Any(nc => 
                        cCode.Contains(nc) || nc.Contains(cCode) || 
                        cName.Contains(nc) || nc.Contains(cName) ||
                        cCycle.Contains(nc));
                    
                    // 2. Word-based match (splitting by common delimiters like KRM-MKM)
                    var allWords = (cCode + " " + cName + " " + cCycle)
                        .Split(new[] { ' ', '-', '/', '_', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => w.ToUpper().Trim())
                        .ToList();
                    
                    bool wordMatch = allWords.Any(w => noKanbanCodes.Contains(w));
                    
                    isTpc = directMatch || wordMatch;
                }

                return new
                {
                    customerId    = c.CustomerId,
                    customerCode  = c.CustomerCode,
                    dockName      = c.CustomerName,
                    cycle         = c.Cycle ?? "-",
                    dockDisplay   = string.IsNullOrEmpty(c.Cycle) ? c.CustomerName : $"{c.CustomerName} \u2014 {c.Cycle}",
                    startPrepTime = c.StartPrepareTime,
                    isTwoPointCheck = isTpc
                };
            }).ToList();

            return Json(result);
        }

        [HttpPost]
        [AuthorizeRoles("Admin", "Preparation")]
        public async Task<IActionResult> Save([FromBody] PreparationSaveRequest request)
        {
            if (request == null) return Json(new { success = false, message = "Data kosong." });

            // Map to PreparationRecord
            var record = new PreparationRecord
            {
                Tag            = request.Tag ?? "",
                Label          = request.Label ?? "",
                Kanban         = request.Kanban ?? "",
                ManifestNumber = request.ManifestNumber,
                SkipStockValidation = request.SkipStockValidation,
                DockName       = request.DockName,
                DockCycle      = request.DockCycle,
                DockCustomerId = request.DockCustomerId,
                Rack           = request.Rack,
                Column         = request.Column,
                // Jika operator pilih tanggal → pakai tanggal itu. Jika tidak pilih → default hari ini.
                // TargetDate selalu terisi agar Delivery Date tidak pernah tampil "-" di log transaksi.
                TargetDate     = (!string.IsNullOrEmpty(request.TargetDate) &&
                                   DateTime.TryParse(request.TargetDate, out var parsedTD))
                                  ? parsedTD.Date
                                  : DateTime.Today
            };

            if (string.IsNullOrWhiteSpace(record.Tag) || string.IsNullOrWhiteSpace(record.Label) || string.IsNullOrWhiteSpace(record.Kanban))
            {
                return Json(new { success = false, message = "Tag, Label, dan Kanban wajib diisi!" });
            }

            // Retry loop: tangani SQLite database locked (error code 5 = SQLITE_BUSY)
            // yang bisa terjadi saat beberapa scan dalam antrian dikirim hampir bersamaan.
            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await ExecuteSave(record);
                }
                catch (DbUpdateConcurrencyException) when (attempt < maxRetries)
                {
                    await Task.Delay(80 * attempt);
                }
            }

            // Final attempt — biarkan exception naik sebagai retryable response
            try { return await ExecuteSave(record); }
            catch (Exception ex)
            {
                return Json(new { success = false, retryable = true, message = "Sistem sedang sibuk, scan akan dicoba ulang otomatis. (" + ex.Message + ")" });
            }
        }

        private async Task<IActionResult> ExecuteSave(PreparationRecord record)
        {
            // Override SkipStockValidation dengan nilai dari DB SystemSettings (di-set oleh Admin)
            // Berlaku untuk semua role termasuk Preparation karena disimpan di DB bukan per-session
            if (!record.SkipStockValidation)
            {
                var skipSetting = await _context.SystemSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "SkipStockValidation");
                if (skipSetting?.Value == "1") record.SkipStockValidation = true;
            }

            await _scanLock.WaitAsync();
            try
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try 
                {
                // Simpan raw tag sebelum normalisasi — dipakai untuk disambiguasi Molded vs Hose
                // Operator Molded scan "NA2910X" (tanpa LB) → harus match item VIN "NA2910" (Molded)
                // Operator Hose    scan "NA2910LBX"           → harus match item VIN "NA2910LB" (Hose)
                var originalRawTag = record.Tag.Trim().ToUpper();
                // Apakah VIN yang di-scan mengandung suffix LB (sebelum 'X' visual indicator dihapus)?
                var rawTagStrippedX = originalRawTag.TrimEnd('X');
                bool scannedWithLB = rawTagStrippedX.EndsWith("LB");

                record.Tag = VinHelper.Normalize(record.Tag);
                record.Label = record.Label.Trim();
                record.Kanban = record.Kanban.Trim();

                // --- SUZUKI Kanban Extraction ---
                // Ekstrak Part No (misal: 42150-52S00 atau 42150-52S00-000) dari scan QR 
                // yang membaca seluruh teks termasuk nama PT.
                if (record.Kanban.Length > 20)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(record.Kanban, @"[A-Z0-9]{4,6}-[A-Z0-9]{4,6}(?:-[A-Z0-9]{2,4})?");
                    if (match.Success)
                    {
                        record.Kanban = match.Value;
                    }
                    else if (record.Kanban.Length > 100)
                    {
                        record.Kanban = record.Kanban.Substring(0, 100);
                    }
                }



                // Susulan (TargetDate masa lalu): CreatedDate = akhir hari tanggal tsb
                // agar FIFO benar — prep susulan mengonsumsi pulling yang ada di H-N
                record.CreatedDate = (record.TargetDate.HasValue && record.TargetDate.Value.Date < DateTime.Today)
                    ? record.TargetDate.Value.Date.AddHours(23).AddMinutes(59).AddSeconds(59)
                    : DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";

                // 1. Identification & Item Lookup
                var normalizedSaveTag = VinHelper.Normalize(record.Tag);
                var itemsMatch = await _context.Items
                    .Where(i => (i.VIN != null && i.VIN != "" && (i.VIN.Contains(normalizedSaveTag) || normalizedSaveTag.Contains(i.VIN))) || 
                                (i.ItemCode != null && i.ItemCode != "" && (i.ItemCode.Contains(normalizedSaveTag) || normalizedSaveTag.Contains(i.ItemCode))))
                    .ToListAsync();
                
                // Saring ke exact normalized match, lalu disambiguasi berdasarkan suffix LB dari raw scan:
                // - raw tag "NA2910X"   (tanpa LB) → pilih item VIN "NA2910"   (Molded, tanpa LB)
                // - raw tag "NA2910LBX" (dengan LB) → pilih item VIN "NA2910LB" (Hose, dengan LB)
                // Ini mencegah VIN.Contains() yang broad menarik item dari plant yang salah.
                var exactMatches = itemsMatch
                    .Where(i => VinHelper.IsMatch(i.VIN, record.Tag) || VinHelper.IsMatch(i.ItemCode, record.Tag) || VinHelper.IsLabelContainsVin(record.Tag, i.VIN))
                    .ToList();

                var validExactMatches = exactMatches.Where(i => !string.Equals(i.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase)).ToList();
                if (validExactMatches.Any())
                {
                    exactMatches = validExactMatches;
                }

                Item? item;
                if (exactMatches.Count > 1)
                {
                    item = scannedWithLB
                        ? exactMatches.FirstOrDefault(i => (i.VIN ?? "").Trim().ToUpper().EndsWith("LB"))
                          ?? exactMatches.First()
                        : exactMatches.FirstOrDefault(i => !(i.VIN ?? "").Trim().ToUpper().EndsWith("LB"))
                          ?? exactMatches.First();
                }
                else
                {
                    item = exactMatches.FirstOrDefault();
                }
                
                if (item == null)
                {
                    var pulling = await _context.PullingRecords.OrderByDescending(p => p.CreatedDate).FirstOrDefaultAsync(p => p.Tag == normalizedSaveTag);
                    if (pulling != null)
                    {
                        item = await _context.Items.FindAsync(pulling.ItemId);
                    }
                }

                if (item == null) return Json(new { success = false, message = "TAG / VIN tidak terdaftar (Master)" });

                if (string.Equals(item.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase))
                {
                    return Json(new { success = false, message = "item tidak ada order" });
                }

                // 1a. VALIDASI LABEL UNIK PADA PREPARATION (Bisa di-toggle ON/OFF via switch UI)
                var validateProdIntSetting = await _context.SystemSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "ValidateProdIntLabel");
                bool isValidateProdIntActive = validateProdIntSetting == null || validateProdIntSetting.Value == "1";

                if (isValidateProdIntActive)
                {
                    bool isDuplicateLabel = await _context.PreparationRecords.AnyAsync(p => p.Label == record.Label && p.Remark != "Mismatch");
                    if (isDuplicateLabel)
                    {
                        var ngLog = new ScanNGLog
                        {
                            Module = "Preparation",
                            Tag = record.Tag,
                            Label = record.Label,
                            Kanban = record.Kanban,
                            Reason = $"Error: Label barcode ({record.Label}) sudah pernah di-scan pada proses Preparation sebelumnya!",
                            CreatedDate = DateTime.Now,
                            CreatedBy = record.CreatedBy
                        };
                        _context.ScanNGLogs.Add(ngLog);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        return Json(new { success = false, message = "Error: Label barcode ini sudah pernah di-scan pada proses Preparation sebelumnya!" });
                    }
                }

                // Fallback rack: jika client tidak kirim rack (item single-rack lama / sebelum update),
                // gunakan primary rack dari Items master data.
                if (string.IsNullOrEmpty(record.Rack) || record.Rack == "-")
                    record.Rack = item.Rack ?? "-";
                if (record.Column == null || record.Column == 0)
                    record.Column = item.NoRack ?? 0;

                // 1b. VALIDASI LABEL: Label harus mengandung kode VIN item
                // Ini adalah validasi server-side (double-check dari client-side)
                string labelSaveUpper = record.Label.ToUpper();
                string itemPartNoSave2 = (item.CustomerPartNumber ?? "").ToUpper();
                // Ambil external PartNos dari ItemMappings untuk validasi label
                string normalizedItemVinForLabel = VinHelper.Normalize(item.VIN);
                var allMappingsLabel = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                var matchedMappingsLabel = allMappingsLabel
                    .Where(m => !string.IsNullOrEmpty(m.VIN) &&
                                (VinHelper.Normalize(m.VIN) == normalizedItemVinForLabel ||
                                 VinHelper.IsMatch(m.VIN, item.VIN)))
                    .ToList();
                var externalPartNosLabel = matchedMappingsLabel
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosLabelUpper = externalPartNosLabel.Select(p => p.ToUpper()).ToList();

                bool labelSaveValid = VinHelper.IsLabelContainsVin(record.Label, item.VIN)
                    || (!string.IsNullOrEmpty(itemPartNoSave2) && labelSaveUpper.Contains(itemPartNoSave2))
                    || externalPartNosLabelUpper.Any(ep => labelSaveUpper.Contains(ep));

                bool isMismatch = false;

                if (!labelSaveValid)
                {
                    isMismatch = true;
                }

                // 2. Final Schedule Matching (Strict Part Number Validation + FIFO)
                string kanbanSaveUpper = record.Kanban.ToUpper();

                // Ambil external PartNo dari ItemMappings — gunakan sebagai prioritas utama validasi kanban
                // Gunakan VinHelper untuk flexible match karena VIN di ItemMappings mungkin disimpan dengan format berbeda
                string normalizedItemVin = VinHelper.Normalize(item.VIN);
                var allMappingsSave = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();
                var matchedMappingsSave = allMappingsSave
                    .Where(m => !string.IsNullOrEmpty(m.VIN) && 
                                (VinHelper.Normalize(m.VIN) == normalizedItemVin || 
                                 VinHelper.IsMatch(m.VIN, item.VIN)))
                    .ToList();
                var externalPartNosSave = matchedMappingsSave
                    .Select(m => m.CustomerPartNumber!.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .ToList();
                var externalPartNosSaveUpper = externalPartNosSave.Select(p => p.ToUpper()).ToList();

                DeliverySchedule? schedule = null;
                var today = DateTime.Today;

                bool kanbanSaveValid;
                List<string> validSaveCodes;

                if (!string.IsNullOrEmpty(record.ManifestNumber) &&
                    record.ManifestNumber.Trim().ToUpper().StartsWith("DN") &&
                    kanbanSaveUpper.StartsWith("DN") &&
                    kanbanSaveUpper.StartsWith(record.ManifestNumber.Trim().ToUpper()))
                {
                    // Shortcut: kanban berformat Delivery Note (DN) yang diawali nomor manifest → valid.
                    // Pada sistem KBN, kanban barcode = delivery note number + serial lot (bukan VIN/PartNo).
                    // Keamanan silang-manifest dijamin oleh DN manifest-kanban check di atas (sebelum blok ini).
                    kanbanSaveValid = true;
                    validSaveCodes = new List<string>();
                }
                else if (externalPartNosSaveUpper.Any())
                {
                    // Prioritas: validasi hanya via ItemMappings
                    kanbanSaveValid = externalPartNosSaveUpper.Any(ep => kanbanSaveUpper == ep || kanbanSaveUpper.Contains(ep));
                    validSaveCodes = externalPartNosSave;
                }
                else
                {
                    // Fallback: validasi via PartNo / VIN / ItemCode dari master Item
                    string partNoSave = (item.CustomerPartNumber ?? "").ToUpper();
                    string vinSave    = (item.VIN ?? "").ToUpper();
                    bool matchPartNo  = !string.IsNullOrEmpty(partNoSave) && (kanbanSaveUpper == partNoSave || kanbanSaveUpper.Contains(partNoSave));
                    bool matchVin     = !string.IsNullOrEmpty(vinSave)    && (kanbanSaveUpper == vinSave    || kanbanSaveUpper.Contains(vinSave));
                    bool matchCode    = !string.IsNullOrEmpty(item.ItemCode) && kanbanSaveUpper == item.ItemCode.ToUpper();
                    kanbanSaveValid   = matchPartNo || matchVin || matchCode;
                    validSaveCodes    = new List<string>(
                        new[] { item.CustomerPartNumber, item.VIN, item.ItemCode }
                        .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Take(1));
                }

                // --- TWO POINT CHECK BYPASS VALIDATION (Server-Side) ---
                if (!kanbanSaveValid && record.Kanban == "TWO-POINT-CHECK")
                {
                    var noKanbanSettingSave = await _context.SystemSettings.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Key == "TwoPointCheck_CustomerCodes");
                    var noKanbanCodesSave = noKanbanSettingSave?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(c => c.Trim().ToUpper()).ToList() ?? new List<string>();

                    bool isTpcItem = false;

                    // Prioritas 1: Cek langsung dari Item.Customer
                    if (!string.IsNullOrWhiteSpace(item.Customer))
                    {
                        var itemCustUp = item.Customer.Trim().ToUpper();
                        isTpcItem = noKanbanCodesSave.Any(nc => itemCustUp == nc || itemCustUp.Contains(nc) || nc.Contains(itemCustUp));
                    }

                    // Prioritas 2: Cek dari ItemMappings.Customer
                    if (!isTpcItem)
                    {
                        var normVinSave2 = VinHelper.Normalize(item.VIN);
                        var allMaps2 = await _context.ItemMappings.Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber)).ToListAsync();
                        var mapCusts = allMaps2
                            .Where(m => !string.IsNullOrEmpty(m.VIN) && (VinHelper.Normalize(m.VIN) == normVinSave2 || VinHelper.IsMatch(m.VIN, item.VIN)))
                            .Select(m => (m.Customer ?? "").Trim().ToUpper())
                            .Where(c => !string.IsNullOrEmpty(c))
                            .Distinct()
                            .ToList();
                        isTpcItem = mapCusts.Any(mc => noKanbanCodesSave.Any(nc => mc == nc || mc.Contains(nc) || nc.Contains(mc)));
                    }

                    // Prioritas 3: Fallback histori DeliverySchedules
                    if (!isTpcItem)
                    {
                        var tpcCustCheck = await _context.DeliverySchedules
                            .Include(s => s.Customer)
                            .Where(s => s.DeliveryItems.Any(di => di.ItemId == item.ItemId))
                            .Select(s => s.Customer.CustomerCode)
                            .Distinct()
                            .ToListAsync();
                        isTpcItem = tpcCustCheck.Any(c => noKanbanCodesSave.Any(nc => c!.ToUpper().Contains(nc) || nc.Contains(c!.ToUpper())));
                    }

                    if (isTpcItem)
                    {
                        kanbanSaveValid = true;
                        isMismatch = false; // Override mismatch jika label tadi valid
                    }
                }

                if (!kanbanSaveValid)
                {
                    isMismatch = true;
                }

                // If Mismatch: do NOT save to PreparationRecords (so it won't pollute plant transaction log), log to ScanNGLogs instead
                if (isMismatch)
                {
                    string ngReason = !labelSaveValid
                        ? $"Label ({record.Label}) tidak mengandung VIN ({item.VIN})"
                        : $"KANBAN tidak sesuai! Barcode harus mengandung: {string.Join(" / ", validSaveCodes)}";

                    var ngLog = new ScanNGLog
                    {
                        Module = "Preparation",
                        Tag = record.Tag,
                        Label = record.Label,
                        Kanban = record.Kanban,
                        Reason = ngReason,
                        CreatedDate = DateTime.Now,
                        CreatedBy = record.CreatedBy
                    };
                    _context.ScanNGLogs.Add(ngLog);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Log activity
                    await _logService.LogActivity(
                        module: "Preparation",
                        action: "NG",
                        entityName: item.ItemName + " (" + item.VIN + ")",
                        entityId: ngLog.Id,
                        description: $"[SCAN NG / MISMATCH] Tag: {record.Tag}, Label: {record.Label}, Kanban: {record.Kanban} - Reason: {ngReason}",
                        performedBy: record.CreatedBy
                    );

                    return Json(new { 
                        success = false, 
                        isMismatch = true,
                        remark = "Mismatch",
                        message = !labelSaveValid 
                            ? $"❌ LABEL TIDAK VALID! Label ({record.Label}) tidak sesuai dengan VIN ({item.VIN})."
                            : $"❌ KANBAN TIDAK VALID! Barcode harus mengandung: {string.Join(" / ", validSaveCodes)}"
                    });
                }

                record.Remark = "Match";

                var activeCustomer = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == record.DockCustomerId);
                bool isAdm = activeCustomer != null && 
                             (string.Equals(activeCustomer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) || 
                              string.Equals(activeCustomer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                // Validasi manifest-kanban: mencegah scan kanban manifest lain diterima (Bug 1 & Bug 2) - Berlaku untuk semua
                // Hanya berlaku jika kanban mengandung prefix manifest (diawali "KBN" atau "DN").
                if (record.Kanban != "TWO-POINT-CHECK" &&
                    !string.IsNullOrEmpty(record.ManifestNumber) &&
                    !DeliveryControl.Helpers.VinHelper.IsKanbanBelongsToManifest(record.Kanban, record.ManifestNumber))
                {
                    // Coba manifest lain: cari manifest aktif pada tanggal/dock/cycle yang sama yang cocok dengan barcode kanban ini dan belum penuh
                    var altQuery = _context.DeliverySchedules
                        .Where(s => (s.Status == "Scheduled" || s.Status == "In Progress" || s.Status == "Shortage Delivery") &&
                                     (record.TargetDate.HasValue
                                         ? s.ScheduledDate.Date == record.TargetDate.Value.Date
                                         : (s.ScheduledDate.Date >= today.AddDays(-2) && s.ScheduledDate.Date <= today.AddDays(2))) &&
                                     s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                    if (record.DockCustomerId.HasValue && record.DockCustomerId.Value > 0)
                    {
                        altQuery = altQuery.Where(s => s.CustomerId == record.DockCustomerId.Value);
                    }
                    if (!string.IsNullOrEmpty(record.DockCycle) && record.DockCycle != "-")
                    {
                        altQuery = altQuery.Where(s => s.Cycle == record.DockCycle);
                    }

                    var altManifestsEx = await altQuery
                        .Select(s => s.ScheduleNumber)
                        .ToListAsync();
                    var correctManifestEx = altManifestsEx.FirstOrDefault(m => DeliveryControl.Helpers.VinHelper.IsKanbanBelongsToManifest(record.Kanban, m));
                    if (correctManifestEx != null)
                    {
                        // Kanban cocok dengan manifest lain yang valid → perbarui manifest dan lanjut
                        record.ManifestNumber = correctManifestEx;
                    }
                    else
                    {
                        // Jadwal belum diupload: Ekstrak manifest dari kanban agar tersimpan di database sebagai PENDING
                        record.ManifestNumber = VinHelper.ExtractManifestFromKanban(record.Kanban, record.ManifestNumber);
                    }
                }

                // Determine if selected customer is ADM
                var dockCustomer = record.DockCustomerId.HasValue 
                    ? await _context.Customers.FindAsync(record.DockCustomerId.Value) 
                    : null;
                bool isSelectedAdm = dockCustomer != null &&
                                      (string.Equals(dockCustomer.CustomerCode, "ADM", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(dockCustomer.CustomerName, "ADM", StringComparison.OrdinalIgnoreCase));

                // Manifest-based matching berlaku untuk semua customer
                string? matchManifestNumber = record.ManifestNumber;

                // Find Schedule — Validasi 5 Elemen: VIN + Dock + Cycle + Manifest + Tanggal Delivery Date
                var baseQuery = _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .Where(s => s.Status != "Cancelled" &&
                                 (record.TargetDate.HasValue
                                     ? s.ScheduledDate.Date == record.TargetDate.Value.Date
                                     : s.ScheduledDate.Date >= today.AddDays(-2)) &&
                                 s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                // Filter by Dock: jika operator memilih dock tertentu, hanya cari jadwal dock tersebut
                if (record.DockCustomerId.HasValue && record.DockCustomerId.Value > 0)
                {
                    baseQuery = baseQuery.Where(s => s.CustomerId == record.DockCustomerId.Value);
                }

                // Filter by Cycle: jika operator memilih cycle tertentu (C1/C2/dst), scan hanya boleh masuk ke
                // jadwal dengan cycle yang sama.
                if (!string.IsNullOrEmpty(record.DockCycle) && record.DockCycle != "-")
                {
                    baseQuery = baseQuery.Where(s => s.Cycle == record.DockCycle);
                }

                // Find Schedules by Manifest if available
                if (!string.IsNullOrEmpty(matchManifestNumber))
                {
                    var manifestUpper = matchManifestNumber.Trim().ToUpper();
                    baseQuery = baseQuery.Where(s => s.ScheduleNumber.ToUpper() == manifestUpper);
                }

                var potentialSchedulesList = await baseQuery
                    .OrderBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .ToListAsync();

                // PRIORITAS: Hari ini (sama) HARUS diproses sebelum lintas hari (hutang).
                var potentialSchedules = potentialSchedulesList
                    .OrderByDescending(s => s.ScheduledDate.Date == today ? 1 : 0) // same-day first
                    .ThenBy(s => s.ScheduledDate)
                    .ThenBy(s => s.ScheduleNumber)
                    .ToList();
                
                DateTime now = DateTime.Now;

                // Susulan (TargetDate masa lalu): langsung ambil jadwal pertama tanpa cek StartPrepTime
                if (record.TargetDate.HasValue && record.TargetDate.Value.Date < today)
                {
                    schedule = potentialSchedules.FirstOrDefault();
                }

                // Pre-scan ke depan (TargetDate > today): jadwal sudah ada, operator sudah konfirmasi tanggal
                if (schedule == null && record.TargetDate.HasValue && record.TargetDate.Value.Date > today)
                {
                    schedule = potentialSchedules.FirstOrDefault(s => s.ScheduledDate.Date == record.TargetDate.Value.Date);
                }

                if (schedule == null) foreach (var ps in potentialSchedules)
                {
                    // Case 1: Hari yang Sama (Now == Schedule)
                    if (now.Date == ps.ScheduledDate.Date)
                    {
                        schedule = ps;
                        break;
                    }

                    // Case 3: Lintas Hari / Hutang (Jadwal H-1 atau H-2 yang belum tuntas) — PRIORITAS RENDAH
                    var daysDiff = (now.Date - ps.ScheduledDate.Date).Days;
                    if (daysDiff >= 1 && daysDiff <= 2)
                    {
                        schedule = ps;
                        break;
                    }

                    // Case 4: Pre-scan H-1 tanpa TargetDate (Scan hari ini untuk jadwal besok)
                    if (daysDiff == -1 && !record.TargetDate.HasValue)
                    {
                        schedule = ps;
                        break;
                    }
                }

                // FALLBACK: VIN-based direct match dalam manifest tertentu
                if (schedule == null && !string.IsNullOrEmpty(matchManifestNumber))
                {
                    var fbManifestUpper = matchManifestNumber.Trim().ToUpper();
                    var fbQuery = _context.DeliverySchedules
                        .Include(s => s.Customer)
                        .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                        .Where(s => s.ScheduleNumber.ToUpper() == fbManifestUpper &&
                                    s.Status != "Cancelled" &&
                                    (record.TargetDate.HasValue
                                        ? s.ScheduledDate.Date == record.TargetDate.Value.Date
                                        : (s.ScheduledDate.Date >= today.AddDays(-2) && s.ScheduledDate.Date <= today.AddDays(2))));

                    if (record.DockCustomerId.HasValue && record.DockCustomerId.Value > 0)
                    {
                        fbQuery = fbQuery.Where(s => s.CustomerId == record.DockCustomerId.Value);
                    }
                    if (!string.IsNullOrEmpty(record.DockCycle) && record.DockCycle != "-")
                    {
                        fbQuery = fbQuery.Where(s => s.Cycle == record.DockCycle);
                    }

                    var fbSched = await fbQuery.FirstOrDefaultAsync();
                    if (fbSched != null)
                    {
                        var fbDI = fbSched.DeliveryItems.FirstOrDefault(di =>
                            (di.ActualQuantity ?? 0) < di.Quantity && (
                                (di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, record.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, record.Tag))) ||
                                (!string.IsNullOrEmpty(di.ExternalPartNo) &&
                                 VinHelper.IsMatch(di.ExternalPartNo, record.Tag))));

                        if (fbDI == null)
                        {
                            var diItemIds = fbSched.DeliveryItems
                                .Where(di => (di.ActualQuantity ?? 0) < di.Quantity)
                                .Select(di => di.ItemId).ToList();
                            if (diItemIds.Any())
                            {
                                var potentialItems = await _context.Items
                                    .Where(i => diItemIds.Contains(i.ItemId)).ToListAsync();
                                var matchedItem = potentialItems.FirstOrDefault(i =>
                                    VinHelper.IsMatch(i.VIN, record.Tag) ||
                                    VinHelper.IsMatch(i.ItemCode, record.Tag));
                                if (matchedItem != null)
                                    fbDI = fbSched.DeliveryItems.FirstOrDefault(di =>
                                        di.ItemId == matchedItem.ItemId &&
                                        (di.ActualQuantity ?? 0) < di.Quantity);
                            }
                        }

                        if (fbDI != null)
                        {
                            schedule = fbSched;
                            var fbItem = await _context.Items.FindAsync(fbDI.ItemId);
                            if (fbItem != null) item = fbItem;
                        }
                    }
                }

                // FALLBACK CROSS-MANIFEST (Spillover) UNTUK TMMIN & LAINNYA
                // Jika manifest yang discan sudah penuh (atau tidak ditemukan), tapi pada kombinasi Dock + Cycle + Tanggal Delivery Date 
                // ini masih ada manifest lain yang butuh VIN yang sama dan kuotanya belum penuh, maka alihkan ke manifest tersebut.
                if (schedule == null)
                {
                    var spilloverQuery = _context.DeliverySchedules
                        .Include(s => s.Customer)
                        .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                        .Where(s => s.Status != "Cancelled" &&
                                    (record.TargetDate.HasValue
                                         ? s.ScheduledDate.Date == record.TargetDate.Value.Date
                                         : s.ScheduledDate.Date >= today.AddDays(-2)) &&
                                    s.DeliveryItems.Any(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity));

                    if (record.DockCustomerId.HasValue && record.DockCustomerId.Value > 0)
                    {
                        spilloverQuery = spilloverQuery.Where(s => s.CustomerId == record.DockCustomerId.Value);
                    }
                    if (!string.IsNullOrEmpty(record.DockCycle) && record.DockCycle != "-")
                    {
                        spilloverQuery = spilloverQuery.Where(s => s.Cycle == record.DockCycle);
                    }

                    var potentialSpillover = await spilloverQuery
                        .OrderByDescending(s => s.ScheduledDate.Date == today ? 1 : 0) // same-day first
                        .ThenBy(s => s.ScheduledDate)
                        .ThenBy(s => s.ScheduleNumber)
                        .ToListAsync();

                    if (potentialSpillover.Any())
                    {
                        if (record.TargetDate.HasValue && record.TargetDate.Value.Date < today)
                            schedule = potentialSpillover.FirstOrDefault();
                        else if (record.TargetDate.HasValue && record.TargetDate.Value.Date > today)
                            schedule = potentialSpillover.FirstOrDefault(s => s.ScheduledDate.Date == record.TargetDate.Value.Date);
                        else
                        {
                            foreach (var ps in potentialSpillover)
                            {
                                if (now.Date == ps.ScheduledDate.Date) { schedule = ps; break; }
                                var daysDiff = (now.Date - ps.ScheduledDate.Date).Days;
                                if (daysDiff >= 1 && daysDiff <= 2) { schedule = ps; break; }
                                if (daysDiff == -1 && !record.TargetDate.HasValue) { schedule = ps; break; }
                            }
                        }

                        if (schedule != null)
                        {
                            // Update ManifestNumber di record agar sesuai dengan jadwal baru yang terpilih
                            record.ManifestNumber = schedule.ScheduleNumber;
                            record.Remark = "Cross-Manifest Match";
                        }
                    }
                }

                // INTERLOCK KANBAN PENUH
                if (schedule == null) 
                {
                    // Cek apakah ada jadwal pada Dock + Cycle + Delivery Date ini untuk VIN tersebut yang kuotanya SUDAH PENUH
                    var fullCheckQuery = _context.DeliverySchedules
                        .Where(s => s.Status != "Cancelled" &&
                                     (record.TargetDate.HasValue
                                         ? s.ScheduledDate.Date == record.TargetDate.Value.Date
                                         : s.ScheduledDate.Date >= today.AddDays(-2)) &&
                                     s.DeliveryItems.Any(di => di.ItemId == item.ItemId));
                    
                    if (record.DockCustomerId.HasValue && record.DockCustomerId.Value > 0)
                    {
                        fullCheckQuery = fullCheckQuery.Where(s => s.CustomerId == record.DockCustomerId.Value);
                    }
                    if (!string.IsNullOrEmpty(record.DockCycle) && record.DockCycle != "-")
                    {
                        fullCheckQuery = fullCheckQuery.Where(s => s.Cycle == record.DockCycle);
                    }

                    var anyFullSchedule = await fullCheckQuery.AnyAsync();
                    if (anyFullSchedule)
                    {
                        // INTERLOCK: Jadwal/kanban untuk VIN pada Dock/Cycle/Tanggal ini sudah penuh
                        return Json(new { success = false, message = "Error: kanban sudah penuh" });
                    }

                    // --- PENDING SAVE: Jadwal belum tersedia ATAU belum waktunya prepare ---
                    // SyncService akan otomatis mengisi ScheduleId ketika admin buat/upload jadwal baru.
                    record.Plant = string.IsNullOrWhiteSpace(item.Plant) ? "-" : item.Plant.Trim();
                    record.ScheduleId = null; // eksplisit null = pending, belum terikat jadwal
                    // DockName/DockCycle/DockCustomerId sudah ter-set dari request

                    _context.PreparationRecords.Add(record);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // Broadcast ke preparation hub saja (bukan stockHub — agar tidak trigger notif di dashboard FG)
                    await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "pending" });
                    
                    // Dashboard FG tetap perlu diupdate karena meskipun pending, stock di rak tetap berkurang secara visual (FIFO)
                    _stockCache.Invalidate();
                    await _stockHubContext.Clients.All.SendAsync("updateStock");

                    return Json(new { 
                        success = true, 
                        isPending = true,
                        message = "Part tersimpan sebagai PENDING — akan tersinkronisasi ke jadwal secara otomatis saat jadwal tersedia." 
                    });
                }

                // DOCK ACCESS CONTROL
                var roleStr = HttpContext.Session.GetString("Role");
                var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";
                if (!isAdmin)
                {
                    var allowedDockCodes = await GetAllowedDockCodesAsync();
                    bool isAuthorized = !allowedDockCodes.Any()
                        || allowedDockCodes.Contains((schedule.Area ?? "").Trim().ToUpper())
                        || (schedule.Customer != null && (
                            allowedDockCodes.Contains((schedule.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((schedule.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((schedule.Customer.Area ?? "").Trim().ToUpper())));
                    if (!isAuthorized)
                    {
                        return Json(new { success = false, message = "Anda tidak memiliki akses ke Dock ini" });
                    }
                }

                // --- VALIDASI 1: Cek apakah kebutuhan QTY sudah terpenuhi ---
                // Prioritaskan mengambil item yang ActualQuantity-nya masih kurang dari Quantity.
                // Jika ada duplikasi (misal Molded & Hose dalam 1 jadwal) dan salah satunya sudah penuh,
                // kita ingin memastikan mengambil yang belum penuh.
                var dItem = schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId && (di.ActualQuantity ?? 0) < di.Quantity)
                            ?? schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == item.ItemId);
                
                // Fallback: cari dItem by VIN jika ItemId tidak cocok (item remap / duplicate VIN scenario)
                if (dItem == null)
                {
                    dItem = schedule.DeliveryItems.FirstOrDefault(di =>
                                di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, record.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, record.Tag)) &&
                                (di.ActualQuantity ?? 0) < di.Quantity)
                            ?? schedule.DeliveryItems.FirstOrDefault(di =>
                                di.Item != null && (
                                    VinHelper.IsMatch(di.Item.VIN, record.Tag) ||
                                    VinHelper.IsMatch(di.Item.ItemCode, record.Tag)));
                    
                    if (dItem != null)
                    {
                        var remappedItem = await _context.Items.FindAsync(dItem.ItemId);
                        if (remappedItem != null) item = remappedItem;
                    }
                }
                // Re-read ActualQuantity fresh dari DB untuk mencegah race condition.
                // Tanpa ini, dua scan yang masuk hampir bersamaan keduanya membaca ActualQty=0 dari
                // EF identity-map cache, keduanya lolos cek, dan keduanya commit → over-scan.
                // ReloadAsync() memaksa EF membaca langsung dari DB, melewati cache.
                if (dItem != null)
                {
                    await _context.Entry(dItem).ReloadAsync();
                }

                // Guard duplikasi kanban DN dihapus: pada praktiknya beberapa kanban fisik
                // dengan barcode yang sama bisa ada dalam satu DN (qty > 1).
                // Over-scan ditangani sepenuhnya oleh cek QTY di bawah ini.

                if (dItem != null && (dItem.ActualQuantity ?? 0) >= dItem.Quantity)
                {
                    return Json(new { success = false, message = $"QTY sudah CUKUP untuk jadwal ini" });
                }

                // --- VALIDASI 2: Cek Saldo Stok Riil (Per Label & FIFO) ---
                // Only count Match records for stock validation (Mismatch records don't affect stock)
                var countPulled = await _context.PullingRecords.CountAsync(p => p.Tag == normalizedSaveTag && p.Label == record.Label && p.Remark == "Match");
                var countPrepared = await _context.PreparationRecords.CountAsync(p => p.Tag == normalizedSaveTag && p.Label == record.Label && p.Remark == "Match");

                var allPullings = await _context.PullingRecords.Where(p => p.Tag == normalizedSaveTag && p.Remark == "Match").OrderBy(p => p.CreatedDate).ToListAsync();
                var allPreps = await _context.PreparationRecords.Where(p => p.Tag == normalizedSaveTag && p.Remark == "Match").ToListAsync();

                var consumedPullingIds = new HashSet<int>();
                foreach (var p in allPreps)
                {
                    var pLabel = (p.Label ?? "").Trim().ToUpper();
                    // Prioritas 1: match Tag + Label persis
                    var matchPrep = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId) && (pl.Label ?? "").Trim().ToUpper() == pLabel);
                    // Prioritas 2: match Tag saja (untuk Manual Adjust di mana Label bisa berbeda)
                    if (matchPrep == null)
                        matchPrep = allPullings.FirstOrDefault(pl => !consumedPullingIds.Contains(pl.PullingId));
                    if (matchPrep != null) consumedPullingIds.Add(matchPrep.PullingId);
                }

                // Sisa stock yang belum dikonsumsi oleh preparation lain
                var availablePullings = allPullings.Where(pl => !consumedPullingIds.Contains(pl.PullingId)).ToList();

                PullingRecord? targetPulling = null;
                if (!availablePullings.Any())
                {
                    // Jika mode Skip Stock Validation aktif, biarkan lanjut meski tidak ada pulling di rak
                    if (!record.SkipStockValidation)
                        return Json(new { success = false, message = "STOCK tidak tersedia di Rak" });
                    // Mode skip: targetPulling = null, proses tetap lanjut tanpa linking ke pulling
                }
                else
                {
                    // Cari Pulling yang label-nya cocok dengan yang di-scan user
                    targetPulling = availablePullings.FirstOrDefault(pl => 
                        (pl.Label ?? "").Trim().ToUpper() == record.Label.Trim().ToUpper());

                    // Jika tidak ada exact match label, ambil yang paling lama (FIFO dari sisa available)
                    if (targetPulling == null)
                    {
                        targetPulling = availablePullings.First(); // Sudah di-order by CreatedDate
                    }
                }

                record.Plant = string.IsNullOrWhiteSpace(item.Plant) ? "-" : item.Plant.Trim();
                record.ScheduleId = schedule.ScheduleId;

                // 3. Update Stats — hitung ulang ActualQuantity dari scan yang tersimpan.
                // DN kanbans (unique per unit): hitung unique barcodes.
                // Non-DN kanbans (same barcode = multiple kanbans fisik): hitung total scan records.
                int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;

                schedule.Status = schedule.ActualEndTime.HasValue ? "Shortage Delivery" : "In Progress";
                schedule.PreparationStatus = "In Progress";
                schedule.UpdatedDate = DateTime.Now;

                // Catat apakah item ini sudah terpenuhi SEBELUM scan ini masuk.
                // Digunakan untuk memunculkan notifikasi peringatan di scanner UI.
                bool wasAlreadyComplete = dItem?.IsCompleted == true;

                // Simpan record ke DB terlebih dahulu (masih dalam transaksi yang sama),
                // baru kemudian hitung ulang dari DB. Dengan cara ini CountAsync akan
                // melihat record yang baru saja disimpan, sehingga menghilangkan race condition
                // di mana dua scan bersamaan membaca count yang sama sebelum salah satu commit.
                _context.PreparationRecords.Add(record);
                await _context.SaveChangesAsync(); // Persist record ke DB (belum commit ke luar transaksi)

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

                    var filteredRecords = records
                        .Where(r => VinHelper.IsKanbanBelongsToManifest(r.Kanban, schedule.ScheduleNumber))
                        .ToList();

                    var uniqueRecords = new List<PreparationRecord>();
                    var seenBarcodes = new HashSet<string>();

                    foreach (var r in filteredRecords)
                    {
                        if (!string.IsNullOrEmpty(r.Kanban))
                        {
                            var kbnUpper = r.Kanban.Trim().ToUpper();
                            // DN kanbans tidak di-dedup: satu DN bisa punya beberapa fisik dengan barcode sama (qty > 1)
                            if (kbnUpper.StartsWith("KBN") && kbnUpper.Length >= 12)
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

                        decimal newActualQty = Math.Min((decimal)assignedScans * qpc, ri.Quantity);
                        ri.ActualQuantity = newActualQty;
                        if (assignedScans > 0)
                        {
                            ri.PrepScanTime = DateTime.Now;
                        }
                        ri.IsCompleted = ri.ActualQuantity >= ri.Quantity;
                    }

                    // Hitung ulang TotalActualQuantity dari semua item di schedule ini
                    schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);

                    // Update main schedule status if all items are done
                    if (schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                    {
                        if (schedule.PreparationStatus != "Prepared")
                        {
                            schedule.PreparationStatus = "Prepared";
                        }
                    }
                }

                // --- AUTO PREPARED LOGIC: Automate "Ready to Pickup" when all scans for the group are done ---
                var groupDate = schedule.ScheduledDate.Date;
                var groupSchedules = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => s.ScheduledDate.Date == groupDate &&
                                s.Cycle == schedule.Cycle &&
                                s.Route == schedule.Route &&
                                s.Area == schedule.Area &&
                                s.Status != "Cancelled")
                    .ToListAsync();

                // Cek mode otomatis & skip leader verif dari DB agar berlaku global
                var autoModeSetting = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
                bool autoEnterDockActive = autoModeSetting?.Value == "1";

                var skipLeaderSetting = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
                bool skipLeaderVerifActive = skipLeaderSetting?.Value == "1";

                bool isGroupReady = false;
                string groupManifest = (schedule.ScheduleNumber ?? "").Split('/')[0].Trim().ToUpper();

                if (groupSchedules.All(gs => gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)))
                {
                    isGroupReady = true;
                    var scanTime = DateTime.Now;
                    foreach (var gs in groupSchedules)
                    {
                        // Mark all as Prepared
                        gs.PreparationStatus = "Prepared";
                        // Jika sudah departure sebelumnya, update status menjadi Completed (Closed Delivery)
                        if (gs.ActualEndTime.HasValue)
                        {
                            gs.Status = "Completed";
                            gs.DriverStatus = "Completed";
                        }
                        else
                        {
                            gs.Status = "In Progress"; // MUST be In Progress for Driver Portal visibility
                        }
                        gs.UpdatedDate = scanTime;

                        // Set ReadyToDockTime for ALL in group (not just unset ones)
                        if (!gs.ReadyToDockTime.HasValue)
                        {
                            gs.ReadyToDockTime = scanTime;
                        }

                        // Auto-set ActualEnterDockTime server-side saat kanban 100% selesai
                        // Hanya jika AutoEnterDockMode ON DAN SkipLeaderVerificationMode ON
                        if (autoEnterDockActive && skipLeaderVerifActive && !gs.ActualEnterDockTime.HasValue)
                        {
                            gs.ActualEnterDockTime = scanTime;
                        }
                    }
                }
                else
                {
                    // Even if not fully prepared, ensure all members of the group are "In Progress" 
                    // so the card appears in the Driver Portal as soon as preparation starts.
                    foreach (var gs in groupSchedules)
                    {
                        if (gs.Status == "Scheduled")
                        {
                            gs.Status = "In Progress";
                            gs.UpdatedDate = DateTime.Now;
                        }
                    }
                }

                await _context.SaveChangesAsync(); // Simpan ActualQuantity + group schedule updates
                await transaction.CommitAsync();

                // --- POST-COMMIT SAFEGUARD: Verifikasi group readiness dengan data DB yang sudah committed ---
                if (!isGroupReady)
                {
                    var freshGroup = await _context.DeliverySchedules
                        .AsNoTracking()
                        .Include(s => s.DeliveryItems)
                        .Where(s => s.ScheduledDate.Date == groupDate &&
                                    s.Cycle == schedule.Cycle &&
                                    s.Route == schedule.Route &&
                                    s.Area == schedule.Area &&
                                    s.Status != "Cancelled")
                        .ToListAsync();

                    // Schedule tanpa item dianggap selesai (tidak memblokir group)
                    bool freshGroupReady = freshGroup.Any() &&
                                          freshGroup.All(gs =>
                                              !gs.DeliveryItems.Any() ||
                                              gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity));

                    if (freshGroupReady)
                    {
                        isGroupReady = true;
                        var dockTime = DateTime.Now;
                        var toUpdate = await _context.DeliverySchedules
                            .Where(s => s.ScheduledDate.Date == groupDate &&
                                        s.Cycle == schedule.Cycle &&
                                        s.Route == schedule.Route &&
                                        s.Area == schedule.Area &&
                                        s.Status != "Cancelled" &&
                                        s.ActualEnterDockTime == null)
                            .ToListAsync();
                        if (toUpdate.Any())
                        {
                            foreach (var gs in toUpdate)
                            {
                                if (autoEnterDockActive && skipLeaderVerifActive)
                                    gs.ActualEnterDockTime = dockTime;
                                gs.ReadyToDockTime ??= dockTime;
                                gs.PreparationStatus = "Prepared";
                                if (gs.ActualEndTime.HasValue)
                                {
                                    gs.Status = "Completed";
                                    gs.DriverStatus = "Completed";
                                }
                                else
                                {
                                    gs.Status = "In Progress";
                                }
                                gs.UpdatedDate = dockTime;
                            }
                            await _context.SaveChangesAsync();
                        }
                    }
                }

                // --- BROADCAST: Send all SignalR notifications AFTER data is fully committed ---
                var totalTarget = schedule.DeliveryItems.Sum(di => di.Quantity);
                var totalActual = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                var totalPercent = totalTarget > 0 ? ((double)totalActual / (double)totalTarget * 100) : 0;

                var broadcastData = new {
                    action = isGroupReady ? (autoEnterDockActive ? "enterDock" : "readyToDock") : "preparation",
                    scheduleNumber = schedule.ScheduleNumber,
                    manifest = groupManifest,
                    status = schedule.Status,
                    preparationStatus = schedule.PreparationStatus,
                    totalPercent = totalPercent,
                    customerName = schedule.Customer?.CustomerName,
                    items = schedule.DeliveryItems.Select(di => new {
                        itemId = di.ItemId,
                        vin = di.Item?.VIN,
                        actual = di.ActualQuantity ?? 0,
                        target = di.Quantity,
                        percent = (di.Quantity > 0) ? ((double)(di.ActualQuantity ?? 0) / (double)di.Quantity * 100) : 0
                    }).ToList()
                };

                await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", broadcastData);

                // Jika group ready: broadcast enterDock juga agar DashboardControlFG
                // langsung refresh kolom Act Dock In tanpa portal preparation perlu terbuka
                if (isGroupReady)
                {
                    await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new {
                        action = "enterDock",
                        scheduleNumber = schedule.ScheduleNumber
                    });
                }
                
                // NOTIF DASHBOARD FG: Penting agar dashboard stock berkurang real-time
                _stockCache.Invalidate();
                await _stockHubContext.Clients.All.SendAsync("updateStock");

                return Json(new {
                    success = true,
                    isAlreadyComplete = wasAlreadyComplete,
                    message = wasAlreadyComplete && dItem != null
                        ? $"\u26A0\uFE0F Kanban sudah PENUH! Target terpenuhi. Scan tetap tersimpan."
                        : $"Berhasil! Persiapan tersimpan.",
                    scheduleDetails = new
                    {
                        schedule.ScheduleId,
                        schedule.ScheduleNumber,
                        CustomerName = schedule.Customer?.CustomerName,
                        TotalPercent = totalPercent,
                        Items = schedule.DeliveryItems.Select(di => {
                            var iQpc = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                            return new {
                                itemName = di.Item?.ItemName ?? "Unknown",
                                vin = di.Item?.VIN ?? "-",
                                target = di.Quantity,
                                actual = di.ActualQuantity ?? 0,
                                targetKanban = (int)Math.Ceiling((double)di.Quantity / iQpc),
                                actualKanban = (int)Math.Ceiling((double)(di.ActualQuantity ?? 0) / iQpc)
                            };
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                // This catch belongs to the inner try (for transaction)
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Terjadi kesalahan internal. " + ex.Message });
            }
            }
            finally
            {
                // This finally belongs to the outer try (for _scanLock)
                _scanLock.Release();
            }
        } // end ExecuteSave

        [HttpPost]
        [AuthorizeRoles("Admin", "Preparation")]
        public async Task<IActionResult> ConfirmSelesai(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null) return Json(new { success = false, message = "Jadwal tidak ditemukan." });

            schedule.PreparationStatus = "Prepared";
            schedule.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync();
            await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "preparation", scheduleNumber = schedule.ScheduleNumber });

            return Json(new { success = true });
        }

        /// <summary>
        /// Endpoint JSON untuk menampilkan daftar PreparationRecord yang masih pending (ScheduleId == null).
        /// Mendukung server-side pagination via query string ?page=1&pageSize=20
        /// Response: { total: N, page: N, pageSize: N, totalPages: N, items: [...] }
        /// </summary>
        [HttpGet]
        [ResponseCache(NoStore = true, Duration = 0)]
        public async Task<IActionResult> GetPendingPreparations(int page = 1, int pageSize = 20, string keyword = "", string dateFrom = "", string dateTo = "")
        {
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;
            if (page < 1) page = 1;

            var query = _context.PreparationRecords
                .Where(p => p.ScheduleId == null && p.Remark != "Deleted");

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                keyword = keyword.Trim().ToLower();
                query = query.Where(p => (!string.IsNullOrEmpty(p.Tag) && p.Tag.ToLower().Contains(keyword)) ||
                                         (!string.IsNullOrEmpty(p.Label) && p.Label.ToLower().Contains(keyword)) ||
                                         (!string.IsNullOrEmpty(p.Kanban) && p.Kanban.ToLower().Contains(keyword)));
            }

            // ── Filter tanggal: berlaku pada tanggal efektif delivery (TargetDate ?? CreatedDate.Date) ──
            if (DateTime.TryParse(dateFrom, out var fromDate))
            {
                fromDate = fromDate.Date;
                // Termasuk record di mana TargetDate >= fromDate, atau (TargetDate == null && CreatedDate.Date >= fromDate)
                query = query.Where(p => (p.TargetDate != null && p.TargetDate.Value >= fromDate)
                                      || (p.TargetDate == null && p.CreatedDate.Date >= fromDate));
            }
            if (DateTime.TryParse(dateTo, out var toDate))
            {
                toDate = toDate.Date;
                // Termasuk record di mana TargetDate <= toDate, atau (TargetDate == null && CreatedDate.Date <= toDate)
                query = query.Where(p => (p.TargetDate != null && p.TargetDate.Value <= toDate)
                                      || (p.TargetDate == null && p.CreatedDate.Date <= toDate));
            }

            query = query.OrderByDescending(p => p.CreatedDate);

            var total = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(total / (double)pageSize);


            var rawItems = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new {
                    p.PreparationId,
                    p.CreatedDate,
                    p.TargetDate,
                    p.Tag,
                    p.Label,
                    p.Kanban,
                    p.CreatedBy,
                    p.DockName,
                    p.DockCycle,
                    p.DockCustomerId
                })
                .ToListAsync();

            var items = rawItems.Select(p => new {
                preparationId  = p.PreparationId,
                createdDate    = p.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                targetDate     = p.TargetDate.HasValue ? p.TargetDate.Value.ToString("dd/MM/yyyy") : (string?)null,
                deliveryDate   = (p.TargetDate ?? p.CreatedDate.Date).ToString("dd/MM/yyyy"),
                tag            = p.Tag,
                label          = p.Label,
                kanban         = p.Kanban,
                createdBy      = p.CreatedBy ?? "-",
                dockName       = p.DockName ?? "-",
                dockCycle      = p.DockCycle ?? "-",
                dockCustomerId = p.DockCustomerId
            }).ToList();

            return Json(new { total, page, pageSize, totalPages, items });
        }

        /// <summary>
        /// Hapus PreparationRecord pending (ScheduleId == null).
        /// mode = "today" → hanya hari ini, mode = "all" → semua.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeletePendingPreparations([FromBody] DeletePendingRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Mode))
                return Json(new { success = false, message = "Mode tidak valid." });

            IQueryable<PreparationRecord> query = _context.PreparationRecords
                .Where(p => p.ScheduleId == null && p.Remark != "Deleted");

            if (req.Mode == "today")
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                query = query.Where(p => p.CreatedDate >= today && p.CreatedDate < tomorrow);
            }
            else if (req.Mode != "all")
            {
                return Json(new { success = false, message = "Mode tidak dikenali. Gunakan 'today' atau 'all'." });
            }

            var records = await query.ToListAsync();
            int count = records.Count;

            if (count == 0)
                return Json(new { success = false, message = "Tidak ada data pending yang bisa dihapus.", deleted = 0 });

            foreach (var r in records)
            {
                r.Remark = "Deleted";
            }
            await _context.SaveChangesAsync();

            // Hitung sisa pending setelah hapus
            var remaining = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");

            string modeLabel = req.Mode == "today" ? "hari ini" : "semua";
            return Json(new
            {
                success   = true,
                message   = $"{count} record pending ({modeLabel}) berhasil dihapus.",
                deleted   = count,
                remaining = remaining
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteSelectedPendingPreparations([FromBody] DeleteSelectedPendingRequest req)
        {
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return Json(new { success = false, message = "Tidak ada item yang dipilih." });

            var records = await _context.PreparationRecords
                .Where(p => req.Ids.Contains(p.PreparationId) && p.ScheduleId == null && p.Remark != "Deleted")
                .ToListAsync();

            if (records.Count == 0)
                return Json(new { success = false, message = "Item terpilih tidak ditemukan atau sudah terikat ke jadwal.", deleted = 0 });

            foreach (var r in records)
            {
                r.Remark = "Deleted";
            }
            await _context.SaveChangesAsync();

            var remaining = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");
            return Json(new
            {
                success   = true,
                message   = $"{records.Count} item terpilih berhasil dihapus.",
                deleted   = records.Count,
                remaining = remaining
            });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SyncSelectedPending([FromBody] SyncSelectedPendingRequest req)
        {
            if (req == null || req.PreparationIds == null || req.PreparationIds.Count == 0 || string.IsNullOrWhiteSpace(req.ManifestNumber))
                return Json(new { success = false, message = "Data tidak lengkap. Pilih item dan ketik Manifest." });

            var manifestStr = req.ManifestNumber.Trim();

            var records = await _context.PreparationRecords
                .Where(p => req.PreparationIds.Contains(p.PreparationId) && p.ScheduleId == null && p.Remark != "Deleted")
                .ToListAsync();

            if (records.Count == 0)
                return Json(new { success = false, message = "Tidak ada record valid ditemukan." });

            // Update ManifestNumber
            foreach (var rec in records)
            {
                rec.ManifestNumber = manifestStr;
            }
            
            await _context.SaveChangesAsync();
            
            // Try sync right away in case the schedule already exists
            int synced = await _syncService.SyncAllPendingAsync();

            if (synced > 0)
            {
                await _deliveryHubContext.Clients.All.SendAsync("ReceiveDeliveryUpdate");
                _stockCache.Invalidate();
                await _stockHubContext.Clients.All.SendAsync("updateStock");
            }

            if (synced == 0)
            {
                return Json(new { success = false, synced = 0, message = $"Manifest berhasil disimpan ke {records.Count} item, tapi gagal sinkronisasi ke jadwal (Part No tidak cocok / Qty penuh di manifest {manifestStr})." });
            }
            else if (synced < records.Count)
            {
                return Json(new { success = true, synced = synced, message = $"Hanya tersinkron {synced} dari {records.Count} item ke manifest {manifestStr}. Sisanya tidak cocok / jadwal penuh." });
            }

            return Json(new { success = true, synced = synced, message = $"Berhasil sinkronisasi {synced} item ke manifest {manifestStr}!" });
        }

        [HttpGet]
        public async Task<IActionResult> GetSchedulesJson(DateTime? filterDate, string? vin)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            var isAdmin = roleStr == "Admin" || roleStr == "Super Admin";
            var allowedDockCodes = await GetAllowedDockCodesAsync();

            var today = DateTime.Today;
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Terapkan filter dock HANYA jika user punya dock assignment
            if (!isAdmin && allowedDockCodes.Any())
            {
                var allSchedJson = await query.ToListAsync();
                var matchJsonIds = allSchedJson
                    .Where(s =>
                        allowedDockCodes.Contains((s.Area ?? "").Trim().ToUpper()) ||
                        (s.Customer != null && (
                            allowedDockCodes.Contains((s.Customer.Docking ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedDockCodes.Contains((s.Customer.Area ?? "").Trim().ToUpper()))))
                    .Select(s => s.ScheduleId).ToHashSet();
                query = query.Where(s => matchJsonIds.Contains(s.ScheduleId));
            }

            // 1. Date Filter (Default: Today)
            if (filterDate.HasValue)
            {
                query = query.Where(s => s.ScheduledDate.Date == filterDate.Value.Date);
            }
            else
            {
                query = query.Where(s => s.ScheduledDate.Date == today);
            }

            // 2. VIN Filter (Partial Match)
            if (!string.IsNullOrEmpty(vin))
            {
                var vinLower = vin.ToLower();
                query = query.Where(s => s.DeliveryItems.Any(di => 
                    (di.Item != null && di.Item.VIN != null && di.Item.VIN.ToLower().Contains(vinLower)) ||
                    (di.Item != null && di.Item.CustomerPartNumber != null && di.Item.CustomerPartNumber.ToLower().Contains(vinLower))
                ));
            }

            var rawSchedules = await query.ToListAsync();

            // 3. Priority Sorting & Projection — PER ITEM (bukan per schedule)
            // Setiap DeliveryItem ditampilkan sebagai 1 baris terpisah
            var result = rawSchedules
                .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.Select(di => new { Schedule = s, Item = di })
                    : new[] { new { Schedule = s, Item = (DeliveryItem)null! } })
                .Select(row => {
                    var s = row.Schedule;
                    var di = row.Item;
                    
                    // Status per item
                    string itemStatus;
                    int priority;
                    if (di == null)
                    {
                        itemStatus = "Waiting";
                        priority = 1;
                    }
                    else
                    {
                        var actual = di.ActualQuantity ?? 0;
                        var target = di.Quantity;
                        if (actual > 0 && actual >= target) { itemStatus = "Completed"; priority = 2; }
                        else if (actual > 0) { itemStatus = "Preparing"; priority = 0; }
                        else { itemStatus = "Waiting"; priority = 1; }
                    }

                    // Calculate Kanban Counts for this item
                    double kanbanTarget = 0;
                    double kanbanActual = 0;
                    string vinDisplay = "-";
                    int qtyLot = 1;

                    if (di?.Item != null)
                    {
                        qtyLot = (di.Item.QtyLot != null && di.Item.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                        kanbanTarget = (int)Math.Ceiling((double)di.Quantity / qtyLot);
                        kanbanActual = (int)Math.Ceiling((double)(di.ActualQuantity ?? 0) / qtyLot);
                        vinDisplay = !string.IsNullOrEmpty(di.Item.VIN) ? di.Item.VIN : (di.Item.CustomerPartNumber ?? "-");
                    }

                    return new {
                        s.ScheduleId,
                        s.ScheduleNumber,
                        CustomerName = s.Customer?.CustomerCode ?? "-",
                        Dock = s.Customer?.CustomerName ?? "-",
                        VINs = vinDisplay,
                        PartNo = di?.Item?.CustomerPartNumber ?? "-",
                        QtyLot = qtyLot,
                        QtyPcs = di?.Quantity ?? 0,
                        Status = itemStatus,
                        Priority = priority,
                        TotalKanbanActual = kanbanActual, 
                        TotalKanbanTarget = kanbanTarget,
                        ScheduledDate = s.ScheduledDate,
                        PrepScanTime = di?.PrepScanTime != null ? di.PrepScanTime.Value.ToString("HH:mm:ss") : "-"
                    };
                })
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.TotalKanbanActual) // Yang baru di-scan naik ke atas
                .ThenBy(x => x.ScheduledDate)
                .ThenBy(x => x.ScheduleNumber);

            return Json(result);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LogNG([FromBody] PreparationNGLogRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Tag)) return Json(new { ok = false });
            var createdBy = HttpContext.Session.GetString("FullName") 
                         ?? HttpContext.Session.GetString("Username") 
                         ?? "Operator";
            var rec = new ScanNGLog
            {
                Module = "Preparation",
                Tag = req.Tag.Trim(),
                Label = req.Label?.Trim() ?? "",
                Kanban = req.Kanban?.Trim() ?? "",
                Reason = req.Reason?.Trim() ?? "",
                CreatedBy = createdBy,
                CreatedDate = DateTime.Now
            };
            _context.ScanNGLogs.Add(rec);
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        /// <summary>
        /// Hapus satu PreparationRecord by ID. Admin / Super Admin only.
        /// Setelah hapus, hitung ulang ActualQuantity di DeliveryItem terkait.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteScanRecord([FromBody] DeleteScanRecordRequest req)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            if (roleStr != "Admin" && roleStr != "Super Admin")
                return Json(new { success = false, message = "Akses ditolak." });

            var record = await _context.PreparationRecords.FindAsync(req.PreparationId);
            if (record == null)
                return Json(new { success = false, message = $"Record ID {req.PreparationId} tidak ditemukan." });

            var scheduleId = record.ScheduleId;
            var tag = record.Tag;
            _context.PreparationRecords.Remove(record);
            await _context.SaveChangesAsync();

            // Hitung ulang ActualQuantity jika record terikat ke jadwal
            if (scheduleId.HasValue)
            {
                var schedule = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId.Value);
                if (schedule != null)
                {
                    var relatedItem = await _context.Items.FirstOrDefaultAsync(i =>
                        i.VIN.Contains(tag) || i.ItemCode.Contains(tag));
                    var dItem = relatedItem != null
                        ? schedule.DeliveryItems.FirstOrDefault(di => di.ItemId == relatedItem.ItemId)
                        : null;
                    if (dItem != null)
                    {
                        int qpc = (relatedItem!.QtyLot != null && relatedItem.QtyLot > 0) ? relatedItem.QtyLot.Value : 1;
                        var tagCandidates = VinHelper.GetItemTags(relatedItem.VIN, relatedItem.ItemCode, dItem.ExternalPartNo ?? relatedItem.CustomerPartNumber);
                        var remainingRecords = await _context.PreparationRecords
                            .Where(p => p.ScheduleId == scheduleId &&
                                        tagCandidates.Contains(p.Tag) &&
                                        p.Remark == "Match")
                            .OrderBy(p => p.CreatedDate)
                            .ToListAsync();

                        var filteredRecords = remainingRecords
                            .Where(r => VinHelper.IsKanbanBelongsToManifest(r.Kanban, schedule.ScheduleNumber))
                            .ToList();

                        var uniqueRecords = new List<PreparationRecord>();
                        var seenBarcodes = new HashSet<string>();

                        foreach (var r in filteredRecords)
                        {
                            if (!string.IsNullOrEmpty(r.Kanban))
                            {
                                var kbnUpper = r.Kanban.Trim().ToUpper();
                                // DN kanbans tidak di-dedup: satu DN bisa punya beberapa fisik dengan barcode sama (qty > 1)
                                if (kbnUpper.StartsWith("KBN") && kbnUpper.Length >= 12)
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

                        int remainingCount = uniqueRecords.Count;
                        dItem.ActualQuantity = Math.Min((decimal)remainingCount * qpc, dItem.Quantity);
                        dItem.IsCompleted = dItem.ActualQuantity >= dItem.Quantity;
                        schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di =>
                            di.DeliveryItemId == dItem.DeliveryItemId
                                ? (dItem.ActualQuantity ?? 0)
                                : (di.ActualQuantity ?? 0));
                        if (schedule.PreparationStatus == "Prepared" &&
                            !schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                            schedule.PreparationStatus = "In Progress";
                        schedule.UpdatedDate = DateTime.Now;
                        await _context.SaveChangesAsync();
                    }
                }
            }

            return Json(new { success = true, message = $"Record {req.PreparationId} berhasil dihapus." });
        }

        /// <summary>
        /// POST /PreparationWorkflow/BackfillTargetDate
        /// Isi TargetDate untuk semua PreparationRecord lama yang TargetDate-nya NULL.
        /// TargetDate diisi dari CreatedDate.Date masing-masing record.
        /// Gunakan ini untuk memperbaiki data lama agar kolom Delivery Date tidak tampil "-".
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> BackfillTargetDate()
        {
            var records = await _context.PreparationRecords
                .Where(r => r.TargetDate == null)
                .ToListAsync();

            foreach (var r in records)
                r.TargetDate = r.CreatedDate.Date;

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"{records.Count} record diperbarui." });
        }

        /// <summary>
        /// POST /PreparationWorkflow/RecalculateActualQty?date=2026-05-25
        /// Recalculate ActualQuantity untuk semua DeliveryItems pada tanggal tertentu
        /// berdasarkan jumlah PreparationRecords yang sesungguhnya di database.
        /// Gunakan ini untuk memperbaiki data lama yang ActualQuantity-nya tidak sinkron
        /// akibat race condition pada scan bersamaan.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> RecalculateActualQty(DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.Today).Date;

            var schedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == targetDate && s.Status != "Cancelled")
                .ToListAsync();

            if (!schedules.Any())
                return Json(new { success = false, message = $"Tidak ada jadwal pada {targetDate:yyyy-MM-dd}." });

            int totalFixed = 0;
            var report = new List<object>();

            foreach (var schedule in schedules)
            {
                foreach (var dItem in schedule.DeliveryItems)
                {
                    var item = dItem.Item;
                    if (item == null) continue;

                    int qpc = (item.QtyLot != null && item.QtyLot > 0) ? item.QtyLot.Value : 1;
                    int targetKanban = (int)Math.Ceiling((double)dItem.Quantity / qpc);

                    // Hitung dari DB sesungguhnya — gunakan VIN yang sudah dinormalisasi
                    // agar cocok dengan cara tag disimpan oleh Save action (VinHelper.Normalize).
                    // Tanpa ini, item VIN berakhiran 'LB'/'X' selalu menghasilkan count=0.
                    var tagCandidates = VinHelper.GetItemTags(item.VIN, item.ItemCode, dItem.ExternalPartNo ?? item.CustomerPartNumber);
                    var records = await _context.PreparationRecords
                        .Where(p => p.ScheduleId == schedule.ScheduleId &&
                                    tagCandidates.Contains(p.Tag) &&
                                    p.Remark == "Match")
                        .OrderBy(p => p.CreatedDate)
                        .ToListAsync();

                    var filteredRecords = records
                        .Where(r => VinHelper.IsKanbanBelongsToManifest(r.Kanban, schedule.ScheduleNumber))
                        .ToList();

                    var uniqueRecords = new List<PreparationRecord>();
                    var seenBarcodes = new HashSet<string>();

                    foreach (var r in filteredRecords)
                    {
                        if (!string.IsNullOrEmpty(r.Kanban))
                        {
                            var kbnUpper = r.Kanban.Trim().ToUpper();
                            // DN kanbans tidak di-dedup: satu DN bisa punya beberapa fisik dengan barcode sama (qty > 1)
                            if (kbnUpper.StartsWith("KBN") && kbnUpper.Length >= 12)
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

                    int actualCount = uniqueRecords.Count;

                    int effectiveCount = Math.Min(actualCount, targetKanban);
                    decimal correctQty = Math.Min((decimal)effectiveCount * qpc, dItem.Quantity);

                    decimal oldQty = dItem.ActualQuantity ?? 0;
                    if (oldQty != correctQty)
                    {
                        report.Add(new
                        {
                            schedule = schedule.ScheduleNumber,
                            item = item.ItemCode,
                            vin = item.VIN,
                            oldKanban = (int)Math.Ceiling((double)oldQty / qpc),
                            newKanban = effectiveCount,
                            dbScanCount = actualCount,
                            targetKanban
                        });

                        dItem.ActualQuantity = correctQty;
                        dItem.IsCompleted = correctQty >= dItem.Quantity;
                        totalFixed++;
                    }
                }

                // Sinkronkan TotalActualQuantity dan status schedule
                schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);

                bool allDone = schedule.DeliveryItems.Any() &&
                               schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
                if (allDone && schedule.PreparationStatus != "Prepared")
                    schedule.PreparationStatus = "Prepared";
                else if (!allDone && schedule.PreparationStatus == "Prepared")
                    schedule.PreparationStatus = "In Progress";
            }

            await _context.SaveChangesAsync();

            // Broadcast update ke dashboard
            await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new { Action = "recalculate" });

            return Json(new
            {
                success = true,
                date = targetDate.ToString("yyyy-MM-dd"),
                totalItemsFixed = totalFixed,
                message = totalFixed > 0
                    ? $"{totalFixed} item ActualQuantity dikoreksi."
                    : "Semua ActualQuantity sudah akurat, tidak ada yang perlu dikoreksi.",
                details = report
            });
        }
    }

    /// <summary>Request body untuk SetSkipStockMode endpoint.</summary>
    public class SetSkipStockModeRequest
    {
        public bool Active { get; set; }
    }

    public class SetValidateProdIntLabelModeRequest
    {
        public bool Active { get; set; }
    }

    public class PreparationNGLogRequest
    {
        public string Module { get; set; } = "Preparation";
        public string? Tag { get; set; }
        public string? Label { get; set; }
        public string? Kanban { get; set; }
        public string? Reason { get; set; }
    }

    public class DeletePendingRequest
    {
        /// <summary>"today" = hapus hari ini saja, "all" = hapus semua pending.</summary>
        public string Mode { get; set; } = "today";
    }

    public class DeleteSelectedPendingRequest
    {
        public List<int> Ids { get; set; } = new List<int>();
    }

    public class DeleteScanRecordRequest
    {
        public int PreparationId { get; set; }
    }

    public class SyncSelectedPendingRequest
    {
        public List<int> PreparationIds { get; set; } = new List<int>();
        public string ManifestNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO untuk body POST /Save — menyertakan field dock mapping baru.
    /// Dikirim dari form scan preparation di browser.
    /// </summary>
    public class PreparationSaveRequest
    {
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Kanban { get; set; } = string.Empty;
        public string? ManifestNumber { get; set; }
        public bool SkipStockValidation { get; set; } = false;
        /// <summary>Nama dock yang dipilih operator (CustomerName dari Customer master).</summary>
        public string? DockName { get; set; }
        /// <summary>Cycle dari dock yang dipilih.</summary>
        public string? DockCycle { get; set; }
        /// <summary>CustomerId Customer yang dipilih.</summary>
        public int? DockCustomerId { get; set; }
        /// <summary>Tanggal jadwal target scan (null = hari ini). Format ISO: yyyy-MM-dd.</summary>
        public string? TargetDate { get; set; }
        /// <summary>Rak yang dipilih operator (huruf rak, mis. "A", "B").</summary>
        public string? Rack { get; set; }
        /// <summary>Nomor kolom rak yang dipilih operator.</summary>
        public int? Column { get; set; }
    }
}
