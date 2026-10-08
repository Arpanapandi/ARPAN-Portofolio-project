using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Helpers;
using Microsoft.AspNetCore.Authorization;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin", "Pulling", "User", "STO")]
    public class PullingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> _hubContext;
        private readonly DeliveryControl.Services.ActivityLogService _activityLogService;
        private readonly DeliveryControl.Services.StockCacheService _stockCache;

        public PullingController(ApplicationDbContext context, 
            Microsoft.AspNetCore.SignalR.IHubContext<DeliveryControl.Hubs.StockHub> hubContext,
            DeliveryControl.Services.ActivityLogService activityLogService,
            DeliveryControl.Services.StockCacheService stockCache)
        {
            _context = context;
            _hubContext = hubContext;
            _activityLogService = activityLogService;
            _stockCache = stockCache;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugNA3030()
        {
            var items = await _context.Items.Where(i => i.VIN == "NA3030").Select(i => new { i.ItemId, i.VIN, i.Rack, i.IsActive }).ToListAsync();
            var itemIds = items.Select(i => i.ItemId).ToList();
            var pulls = await _context.PullingRecords.Where(p => p.ItemId != null && itemIds.Contains(p.ItemId.Value))
                .Select(p => new { p.PullingId, p.Rack, p.Column, p.AdjustNote, p.CreatedDate }).ToListAsync();
            var preps = await _context.PreparationRecords.Where(r => r.Tag != null && r.Tag.Contains("NA3030"))
                .Select(r => new { r.PreparationId, r.CreatedDate }).ToListAsync();
            return Json(new { items, pulls, preps });
        }

        public async Task<IActionResult> Index()
        {
            var role = HttpContext.Session.GetString("Role");
            var isSuperAdmin = role == "Super Admin";
            var isAdmin = role == "Admin" || role == "Super Admin";
            var isUser = role == "User";

            var skipLabelSetting = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "PullingSkipLabelValidation");
            bool isSkipLabelActive = skipLabelSetting?.Value == "1" || skipLabelSetting?.Value?.ToLower() == "true";

            ViewData["IsSuperAdmin"] = isSuperAdmin;
            ViewData["IsAdmin"] = isAdmin;
            ViewData["IsUser"]  = isUser;
            ViewData["SkipLabelValidation"] = isSkipLabelActive;
            return View();
        }

        /// <summary>
        /// Super Admin / Admin: Aktifkan/nonaktifkan Skip Label Validation Pulling secara global (via DB SystemSettings).
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin", "Admin")]
        public async Task<IActionResult> SetSkipLabelMode([FromBody] SetSkipLabelModeRequest req)
        {
            const string key = "PullingSkipLabelValidation";
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSetting { Key = key, Description = "Skip validasi duplikasi dan format label pada menu Pulling (diaktifkan Super Admin)" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.Active ? "1" : "0";
            await _context.SaveChangesAsync();

            // Broadcast ke semua client yang sedang buka halaman Pulling via SignalR
            await _hubContext.Clients.All.SendAsync("pullingSkipLabelToggled", req.Active);

            return Json(new { success = true, active = req.Active });
        }

        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return Json(new { success = false });

            tag = tag.Trim();

            var normalizedTag = VinHelper.Normalize(tag);
            var itemCandidates = await _context.Items
                .Include(i => i.RackLocations)
                .Where(i => i.VIN.Contains(normalizedTag))
                .ToListAsync();

            var matchedItems = itemCandidates.Where(i => VinHelper.IsMatch(i.VIN, tag)).ToList();

            var validMatchedItems = matchedItems.Where(i => !string.Equals(i.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase)).ToList();
            if (!validMatchedItems.Any() && matchedItems.Any())
            {
                return Json(new { success = false, message = "item tidak ada order" });
            }
            if (validMatchedItems.Any())
            {
                matchedItems = validMatchedItems;
            }

            if (!matchedItems.Any())
            {
                return Json(new { success = false, message = "VIN tidak ditemukan di Master Data" });
            }

            // Prioritas: customer NON-EKS didahulukan (misal HMMI sebelum HMMI EKS)
            matchedItems = ItemPriorityHelper.PrioritizeNonEks(matchedItems);

            var item = matchedItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Rack) && i.IsActive) 
                    ?? matchedItems.First(); // Ambil record utama (sebaiknya yg punya rak & aktif)

            var itemIds = matchedItems.Select(i => i.ItemId).ToList();

            // ── Rack locations + stock: dari FG Dashboard (FIFO-adjusted PullingRecords) ──
            // Prioritas: actual stock per rak — lebih akurat daripada master item (sering tidak lengkap)
            var tagForStock = VinHelper.Normalize(tag);
            var pullingForVinAll = await _context.PullingRecords
                .Where(p => 
                    ((p.ItemId.HasValue && itemIds.Contains(p.ItemId.Value)) || (tag != "" && p.Tag != null && p.Tag.ToUpper().Contains(tag)))
                    && p.Remark != "Mismatch"
                )
                .OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId)
                .ToListAsync();
            
            // Only consider non-zero stock items for active stock / FIFO matching
            var pullingForVin = pullingForVinAll.Where(p => (p.AdjustNote ?? "") != "Zero Stock" && (p.AdjustNote ?? "") != "Opname Reduce").ToList();
            
            var prepForVin = await _context.PreparationRecords
                .Where(r => r.Tag == tagForStock && r.Remark != "Mismatch")
                .OrderBy(r => r.CreatedDate).ThenBy(r => r.PreparationId)
                .ToListAsync();

            // Pre-compute normalized VIN as local string (EF Core cannot translate VinHelper.Normalize() to SQL)
            var normalizedTagForVin = VinHelper.Normalize(tagForStock);
            var activeItemCountForVin = await _context.Items
                .CountAsync(i => i.IsActive && !i.IsDeleted && i.VIN != null &&
                                (i.VIN.ToUpper() == tagForStock || i.VIN.ToUpper() == normalizedTagForVin));
            bool isUniqueVin = activeItemCountForVin == 1;

            var consumedPullingIds = new HashSet<int>();
            foreach (var prep in prepForVin)
            {
                var prepLbl = (prep.Label ?? "").Trim().ToUpper();
                var normPrepLbl = VinHelper.Normalize(prepLbl);

                var fifoMatch = pullingForVin.FirstOrDefault(p =>
                    !consumedPullingIds.Contains(p.PullingId) &&
                    (p.Label ?? "").Trim().ToUpper() == prepLbl &&
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                bool labelCompatibleMatch = !string.IsNullOrEmpty(prepLbl) && pullingForVin.Any(p =>
                    !consumedPullingIds.Contains(p.PullingId) &&
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                    (VinHelper.Normalize(p.Label) == normPrepLbl ||
                     VinHelper.Normalize(p.Label).StartsWith(normPrepLbl) ||
                     normPrepLbl.StartsWith(VinHelper.Normalize(p.Label))));

                // Prioritas 2 & Fallback: Jika tidak ada exact label match, pasangkan ke pulling tertua yang tersedia
                if (fifoMatch == null)
                {
                    if (labelCompatibleMatch)
                    {
                        fifoMatch = pullingForVin.FirstOrDefault(p =>
                            !consumedPullingIds.Contains(p.PullingId) &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                            (VinHelper.Normalize(p.Label) == normPrepLbl ||
                             VinHelper.Normalize(p.Label).StartsWith(normPrepLbl) ||
                             normPrepLbl.StartsWith(VinHelper.Normalize(p.Label))));
                    }
                    if (fifoMatch == null)
                    {
                        fifoMatch = pullingForVin.FirstOrDefault(p =>
                            !consumedPullingIds.Contains(p.PullingId) &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                    }
                }
                if (fifoMatch != null) consumedPullingIds.Add(fifoMatch.PullingId);
            }

            var inStockPieces = pullingForVin.Where(p => !consumedPullingIds.Contains(p.PullingId)).ToList();
            var inStockCount  = inStockPieces.Count;

            // ── Rack locations: EXACTLY match Dashboard FG logic ──
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
                AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", p.ItemId ?? item.ItemId);
            }

            // 2. Zero Stock markers (selalu muncul)
            foreach(var p in pullingForVinAll.Where(x => (x.AdjustNote ?? "").Contains("Zero Stock") && !(x.AdjustNote ?? "").Contains("Rack Hidden")))
            {
                AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", item.ItemId);
            }

            // 3. Historical racks DIHAPUS SESUAI PERMINTAAN USER (TIDAK DITAMPILKAN LAGI WALAUPUN ADA STOK AKTIF)
            // if (inStockCount > 0)
            // {
            //     foreach(var p in pullingForVinAll.Where(x => !(x.AdjustNote ?? "").Contains("Rack Hidden")))
            //     {
            //         AddRack((p.Rack ?? "-").Trim().ToUpper(), p.Column, p.Plant ?? "-", item.ItemId);
            //     }
            // }


            var rackLocations = rackDict.Values
                .OrderBy(r => ((dynamic)r).rack)
                .ThenBy(r => ((dynamic)r).noRack)
                .ToList();

            string status = "Normal";
            if (inStockCount < (item.RackMin ?? 5)) status = "Shortage";
            else if (inStockCount > (item.RackMax ?? 20)) status = "Over";

            return Json(new {
                success         = true,
                itemId          = item.ItemId,
                itemCode        = item.ItemCode,
                itemName        = item.ItemName,
                description     = item.Description ?? "-",
                unit            = item.Unit ?? "-",
                plant           = item.Plant ?? "-",
                rack            = item.Rack ?? "-",
                noRack          = item.NoRack,
                customer        = item.Customer ?? "-",
                category        = item.Category ?? "-",
                vin             = item.VIN ?? "-",
                qtyLot          = item.QtyLot ?? 0,
                rackMin         = item.RackMin ?? 0,
                rop             = item.ROP ?? 0,
                rackMax         = item.RackMax ?? 0,
                status          = status,
                currentStock    = inStockCount,
                // Multi-rack info
                multiRack       = rackLocations.Count > 1,
                rackLocations   = rackLocations
            });
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin", "Pulling")]
        public async Task<IActionResult> Save([FromBody] PullingRecord record)
        {
            if (ModelState.IsValid)
            {
                // Trim strings and remove visual X
                record.Tag = VinHelper.Normalize(record.Tag);
                record.Label = (record.Label ?? "").Trim();

                if (string.IsNullOrEmpty(record.Tag))
                {
                    return Json(new { success = false, message = "Input TAG / VIN kosong!" });
                }

                if (string.IsNullOrEmpty(record.Label))
                {
                    return Json(new { success = false, message = "Label tidak boleh kosong!" });
                }

                // 1. Validasi kesesuaian Tag/Rak dan Label (WAJIB selalu berlaku: Label harus mengandung unsur VIN/Rak)
                if (!VinHelper.IsLabelContainsVin(record.Label, record.Tag))
                {
                    return Json(new { success = false, message = $"Label tidak valid! Label '{record.Label}' tidak mengandung unsur kode VIN/Rak '{record.Tag}'." });
                }

                // 2. Cek status global mode Skip Validasi Label (Hanya men-skip duplikasi label di database)
                var skipLabelSetting = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == "PullingSkipLabelValidation");
                bool isSkipLabelActive = skipLabelSetting?.Value == "1" || skipLabelSetting?.Value?.ToLower() == "true";

                if (!isSkipLabelActive)
                {
                    // Cek duplikasi Label di database (Label yang sama tidak boleh discan 2 kali)
                    var existingPulling = await _context.PullingRecords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Label == record.Label && p.Remark != "Mismatch");

                    if (existingPulling != null)
                    {
                        var createdTime = existingPulling.CreatedDate.ToString("dd/MM/yyyy HH:mm");
                        var createdUser = string.IsNullOrEmpty(existingPulling.CreatedBy) ? "Operator" : existingPulling.CreatedBy;
                        return Json(new { 
                            success = false, 
                            message = $"Label '{record.Label}' sudah pernah discan pada {createdTime} oleh {createdUser}!" 
                        });
                    }
                }

                // 1. Identification & Item Lookup

                // 2. Lookup Item Master - Using VIN as Tag
                var normalizedSaveTag = VinHelper.Normalize(record.Tag);
                var normalizedLabel = VinHelper.Normalize(record.Label);

                bool labelMismatch = !VinHelper.IsLabelContainsVin(record.Label, record.Tag);

                var itemSaveCandidates = await _context.Items
                    .Where(i => i.VIN.Contains(normalizedSaveTag))
                    .ToListAsync();

                var validSaveCandidates = itemSaveCandidates.Where(i => !string.Equals(i.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase)).ToList();
                if (validSaveCandidates.Any())
                {
                    itemSaveCandidates = validSaveCandidates;
                }

                // Prioritaskan customer NON-EKS (HMMI sebelum HMMI EKS) agar scan memilih item yang benar
                itemSaveCandidates = ItemPriorityHelper.PrioritizeNonEks(itemSaveCandidates);

                // If client provided a specific ItemId (multi-rack selection), prefer it
                Item? item = null;
                if (record.ItemId.HasValue && record.ItemId.Value > 0)
                {
                    item = await _context.Items.FindAsync(record.ItemId.Value);
                    // Validasi: VIN harus cocok
                    if (item != null && !VinHelper.IsMatch(item.VIN, record.Tag))
                    {
                        item = null; // VIN mismatch — fall back to normal lookup
                    }
                }

                if (item == null)
                {
                    // Fallback 1: find by VIN + Rack + Column match in Items table
                    string clientRack = (record.Rack ?? "").Trim().ToUpper();
                    int clientCol = record.Column;
                    if (!string.IsNullOrEmpty(clientRack) && clientRack != "-" && clientCol > 0)
                    {
                        item = itemSaveCandidates.FirstOrDefault(i =>
                            VinHelper.IsMatch(i.VIN, record.Tag) &&
                            i.Rack == clientRack &&
                            i.NoRack == clientCol);
                    }
                    // Fallback 2: first VIN match (sudah diurutkan non-EKS dulu)
                    if (item == null)
                        item = itemSaveCandidates.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, record.Tag));
                }

                if (item != null)
                {
                    if (string.Equals(item.StatusItem?.Trim(), "No Order", StringComparison.OrdinalIgnoreCase))
                    {
                        return Json(new { success = false, message = "item tidak ada order" });
                    }

                    // Pulling menambah stock: tidak ada validasi duplikat label/VIN
                    record.ItemId = item.ItemId;
                    record.Plant  = item.Plant ?? "Unknown";
                    // Preserve client-provided rack for multi-rack selections; only fallback to item's primary rack when empty
                    if (string.IsNullOrEmpty(record.Rack) || record.Rack == "-")
                        record.Rack = item.Rack ?? "-";
                    if (record.Column == 0)
                        record.Column = item.NoRack ?? 0;
                }
                else
                {
                    return Json(new { success = false, message = "VIN tidak ditemukan di Master Data" });
                }

                record.CreatedDate = DateTime.Now;
                record.CreatedBy = HttpContext.Session.GetString("FullName") ?? "Operator";
                record.Remark = labelMismatch ? "Mismatch" : "Match";
                
                _context.PullingRecords.Add(record);
                await _context.SaveChangesAsync();

                // Log Activity per Plant as requested
                await _activityLogService.LogActivity(
                    module: "Pulling",
                    action: "Create",
                    entityName: item.ItemName + " (" + item.VIN + ")",
                    entityId: record.PullingId,
                    description: $"Plant: {record.Plant}, Rack: {record.Rack}, Label: {record.Label}" + (isSkipLabelActive ? " [SKIP VALIDASI LABEL]" : (labelMismatch ? " [MISMATCH]" : "")),
                    performedBy: record.CreatedBy
                );

                // Notify all clients via SignalR
                _stockCache.Invalidate();
                await _hubContext.Clients.All.SendAsync("updateStock");
                
                // Get updated stock for feedback (only count Match records)
                var updatedStockCount = await _context.PullingRecords
                    .CountAsync(r => r.ItemId == item.ItemId && r.Remark == "Match" && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label && p.Remark == "Match"));

                var mismatchMsg = labelMismatch ? " ⚠️ MISMATCH - Transaksi tercatat tapi TIDAK mempengaruhi stock." : "";
                return Json(new { success = true, message = $"Data {item.ItemName} berhasil disimpan!{mismatchMsg}", newStock = updatedStockCount, remark = record.Remark });
            }
            return Json(new { success = false, message = "Data tidak valid." });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllItemsForAdjust()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "Admin" && role != "Super Admin" && role != "STO")
                return Json(new { success = false });

            var rawItems = await _context.Items
                .AsNoTracking()
                .Where(i => !i.IsDeleted)
                .Select(i => new {
                    i.ItemId,
                    i.ItemCode,
                    i.ItemName,
                    i.VIN,
                    i.Plant,
                    i.Category,
                    i.Rack,
                    i.NoRack
                })
                .ToListAsync();

            var items = rawItems
                .GroupBy(i => !string.IsNullOrWhiteSpace(i.VIN) ? i.VIN.Trim().ToUpper() : (i.ItemCode ?? "").Trim().ToUpper())
                .Select(g => {
                    var primary = g.OrderBy(i => (i.ItemCode ?? "").Contains('_') ? 1 : 0)
                                   .ThenBy(i => i.ItemId)
                                   .First();
                    return primary;
                })
                .OrderBy(i => i.ItemName)
                .ToList();

            return Json(new { success = true, items });
        }

        [HttpPost]
        public async Task<IActionResult> ManualAdjust([FromBody] ManualAdjustRequest request)
        {
            try {
                // Admin only
                var role = HttpContext.Session.GetString("Role");
                if (role != "Admin" && role != "Super Admin" && role != "STO")
                    return Json(new { success = false, message = "Hanya Admin, Super Admin, atau STO yang dapat melakukan Manual Adjust." });

            // Validate inputs
            if (request.ItemId <= 0)
                return Json(new { success = false, message = "Item tidak valid." });

            // Default note jika kosong
            if (string.IsNullOrWhiteSpace(request.Note))
                request.Note = "Manual Stock Adjustment";

            var item = await _context.Items.FindAsync(request.ItemId);
            if (item == null)
                return Json(new { success = false, message = "Item tidak ditemukan." });


            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var now = DateTime.Now;

            // ABSOLUTE QUANTITY LOGIC: 
            // Jika TargetQty disertakan, kita "reset" stok di rak tersebut menjadi angka yang diinginkan.
            if (request.TargetQty.HasValue)
            {
                var rk = (request.Rack ?? request.SourceRack ?? "").Trim().ToUpper();
                var cn = request.NoRack ?? request.SourceNoRack ?? 0;

                // OPTIMIZATION: Filter hanya untuk item yang relevan agar tidak berat
                var itemTag = (item.VIN ?? item.ItemCode ?? "N/A").Trim().ToUpper();

                var allPullingGlobal = await _context.PullingRecords.AsNoTracking()
                    .Where(p => p.Remark != "Mismatch"
                             && (p.AdjustNote ?? "") != "Opname Reduce"   // FIX: match dashboard — exclude rack-specific reduce markers
                             && (p.ItemId == item.ItemId || (p.Tag ?? "").Trim().ToUpper() == itemTag))
                    .Select(p => new { p.PullingId, p.ItemId, Tag = p.Tag, Label = p.Label, p.CreatedDate, Rack = p.Rack, Column = p.Column, p.AdjustNote })
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PullingId)
                    .ToListAsync();

                var preparationQuery = _context.PreparationRecords.AsNoTracking()
                    .Where(p => p.Remark != "Mismatch" && (p.Tag ?? "").Trim().ToUpper() == itemTag);
                
                // FIX: Align EXACTLY with dashboard plant filtering (GetStockViewModel)
                // Dashboard includes: Plant==plant, Plant=="MADJUST", Plant==null, Plant=="", Plant=="-"
                if (!string.IsNullOrEmpty(request.Plant) && request.Plant != "Overall")
                {
                    preparationQuery = preparationQuery.Where(p =>
                        p.Plant == request.Plant || p.Plant == "MADJUST" ||
                        p.Plant == null || p.Plant == "" || p.Plant == "-");
                }

                var allPrepGlobal = await preparationQuery
                    .Select(p => new { p.PreparationId, Tag = p.Tag, Label = p.Label, p.Plant, p.CreatedDate })
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PreparationId)
                    .ToListAsync();

                // 2. Jalankan Logika FIFO Identik Dashboard (Optimized with Lookup)
                var pullingLookup = allPullingGlobal.ToLookup(p => (p.Tag ?? "").Trim().ToUpper());
                var consumedIds = new HashSet<int>();
                var matchedPrepIds = new HashSet<int>();

                // Cek apakah VIN ini unik di master data (untuk relaxed fallback)
                // Pre-compute normalized VIN as local string (EF Core cannot translate VinHelper.Normalize() to SQL)
                var normalizedItemTag2 = VinHelper.Normalize(itemTag);
                var activeItemCountForVin2 = await _context.Items
                    .CountAsync(i => i.IsActive && !i.IsDeleted && i.VIN != null &&
                                    (i.VIN.ToUpper() == itemTag || i.VIN.ToUpper() == normalizedItemTag2));
                bool isUniqueVin2 = activeItemCountForVin2 == 1;

                foreach (var prep in allPrepGlobal)
                {
                    var pTag   = (prep.Tag   ?? "").Trim().ToUpper();
                    var pLabel = (prep.Label ?? "").Trim().ToUpper();

                    var candidates = pullingLookup[pTag];

                    // Match logic identical to dashboard (Priority 1: Tag + Label + 10s)
                    var match = candidates.FirstOrDefault(p => !consumedIds.Contains(p.PullingId) && 
                        (p.Label ?? "").Trim().ToUpper() == pLabel &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                    if (match == null)
                    {
                        var normPLabel = VinHelper.Normalize(pLabel);
                        bool labelCompatibleMatch = !string.IsNullOrEmpty(pLabel) && candidates.Any(p =>
                            !consumedIds.Contains(p.PullingId) &&
                            (p.AdjustNote ?? "") != "Zero Stock" &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                            (VinHelper.Normalize(p.Label) == normPLabel ||
                             VinHelper.Normalize(p.Label).StartsWith(normPLabel) ||
                             normPLabel.StartsWith(VinHelper.Normalize(p.Label))));

                        if (labelCompatibleMatch)
                        {
                            match = candidates.FirstOrDefault(p =>
                                !consumedIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                                (VinHelper.Normalize(p.Label) == normPLabel ||
                                 VinHelper.Normalize(p.Label).StartsWith(normPLabel) ||
                                 normPLabel.StartsWith(VinHelper.Normalize(p.Label))));
                        }
                        if (match == null)
                        {
                            match = candidates.FirstOrDefault(p => !consumedIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                        }
                    }

                    if (match != null)
                    {
                        consumedIds.Add(match.PullingId);
                        matchedPrepIds.Add(prep.PreparationId);
                    }
                }

                // Note: Ghost Prep Forgiveness removed from this sync adjust as it can be too broad.
                // The new stock is already protected by the p.CreatedDate <= prep.CreatedDate + 10s rule,
                // meaning boxes added 'now' won't match preps from the past.


                // 3. Ekstrak data yang BENAR-BENAR aktif HANYA di item & rak ini
                var activeIds = allPullingGlobal
                    .Where(p => !consumedIds.Contains(p.PullingId) && (p.ItemId == item.ItemId || (p.Tag ?? "").Trim().ToUpper() == itemTag))
                    .Where(p => {
                        var pRack = (p.Rack ?? "").Trim().ToUpper();
                        var matchRack = pRack == rk || (string.IsNullOrWhiteSpace(pRack) && rk == "-");
                        return matchRack && p.Column == cn;
                    })
                    .Select(p => p.PullingId)
                    .ToList();

                // 4. MATIKAN (Mismatch) stok lama HANYA di rak target agar tidak terhitung lagi
                // Termasuk Zero Stock marker lama di rak target — akan digantikan oleh record baru
                if (activeIds.Any())
                {
                    foreach (var chunk in activeIds.Chunk(1000))
                    {
                        var activeInTargetRack = await _context.PullingRecords
                            .Where(p => chunk.Contains(p.PullingId))
                            .ToListAsync();

                        foreach (var pr in activeInTargetRack)
                        {
                            pr.Remark = "Mismatch"; 
                            var discardText = " | Discarded by Sync Adjust (" + rk + "." + cn + ")";
                            var baseNote = pr.AdjustNote ?? "";
                            if (baseNote.Length + discardText.Length > 100)
                            {
                                baseNote = baseNote.Substring(0, 100 - discardText.Length);
                            }
                            pr.AdjustNote = baseNote + discardText;
                        }
                    }
                }
                // Bersihkan Zero Stock marker lama di rak TARGET
                var targetZeroMarkers = await _context.PullingRecords
                    .Where(p => (p.ItemId == item.ItemId || (p.Tag != null && p.Tag == item.VIN)) && (p.Rack ?? "").Trim().ToUpper() == rk
                             && p.Column == cn && p.Remark != "Mismatch" && p.AdjustNote == "Zero Stock")
                    .ToListAsync();
                foreach (var pr in targetZeroMarkers) pr.Remark = "Mismatch";
                if (activeIds.Any() || targetZeroMarkers.Any())
                    await _context.SaveChangesAsync();

                // 4b. Jika user mengganti lokasi rak (K.14 → K.15), hapus stok aktif di rak LAMA juga.
                // SourceRack/SourceNoRack dikirim dari JS ketika dropdown rak diganti sebelum simpan.
                var srcRk = (request.SourceRack ?? "").Trim().ToUpper();
                var srcCn = request.SourceNoRack ?? -1;
                if (!string.IsNullOrEmpty(srcRk) && srcCn >= 0 && (srcRk != rk || srcCn != cn))
                {
                    var sourceActiveIds = allPullingGlobal
                        .Where(p => !consumedIds.Contains(p.PullingId) && (p.ItemId == item.ItemId || (p.Tag ?? "").Trim().ToUpper() == itemTag))
                        .Where(p => {
                            var pRack = (p.Rack ?? "").Trim().ToUpper();
                            return (pRack == srcRk || (string.IsNullOrWhiteSpace(pRack) && srcRk == "-")) && p.Column == srcCn;
                        })
                        .Select(p => p.PullingId)
                        .ToList();

                    if (sourceActiveIds.Any())
                    {
                        foreach (var chunk in sourceActiveIds.Chunk(1000))
                        {
                            var sourceRecords = await _context.PullingRecords
                                .Where(p => chunk.Contains(p.PullingId))
                                .ToListAsync();
                            foreach (var pr in sourceRecords)
                            {
                                pr.Remark = "Mismatch";
                                var note2 = " | Moved to " + rk + "." + cn;
                                var base2 = pr.AdjustNote ?? "";
                                if (base2.Length + note2.Length > 100) base2 = base2.Substring(0, 100 - note2.Length);
                                pr.AdjustNote = base2 + note2;
                            }
                        }
                    }
                    // Bersihkan Zero Stock marker lama di rak LAMA (K.14)
                    var sourceZeroMarkers = await _context.PullingRecords
                        .Where(p => (p.ItemId == item.ItemId || (p.Tag != null && p.Tag == item.VIN)) && (p.Rack ?? "").Trim().ToUpper() == srcRk
                                 && p.Column == srcCn && p.Remark != "Mismatch" && p.AdjustNote == "Zero Stock")
                        .ToListAsync();
                    foreach (var pr in sourceZeroMarkers) pr.Remark = "Mismatch";
                    if (sourceActiveIds.Any() || sourceZeroMarkers.Any())
                        await _context.SaveChangesAsync();
                }

                request.Note = (request.Note ?? "") + $" (Sync Adjust to {request.TargetQty})";

                // [FIX] Immediate return to isolate this rack adjustment and prevent cross-rack consumption
                if (request.TargetQty.Value == 0)
                {
                    // Add Zero Stock marker to keep rack visible with 0 count
                    var zeroTag = item.VIN ?? item.ItemCode ?? "N/A";
                    var zeroLabel = "ZEROSTK-" + rk + cn + "-" + Guid.NewGuid().ToString().Substring(0, 4);
                    var zeroMarker = new PullingRecord {
                        ItemId = item.ItemId,
                        Plant = string.IsNullOrEmpty(item.Plant) ? "Unknown" : (item.Plant.Length > 20 ? item.Plant.Substring(0, 20) : item.Plant),
                        Rack = rk, Column = cn, Tag = zeroTag, Label = zeroLabel, Quantity = 0,
                        IsManualAdjust = true, AdjustNote = "Zero Stock", CreatedBy = createdBy, CreatedDate = DateTime.Now
                    };
                    _context.PullingRecords.Add(zeroMarker);
                }
                else
                {
                    // Add NEW physical records for the Target Quantity
                    var tag = item.VIN ?? item.ItemCode ?? "N/A";
                    if (tag.Length > 100) tag = tag.Substring(0, 100);

                    // Unique label to avoid old ghost preps
                    var batchLabel = tag + "LB" + rk + cn.ToString() + "U" + now.Ticks.ToString().Substring(10);
                    if (batchLabel.Length > 100) batchLabel = batchLabel.Substring(0, 100);

                    for (int i = 0; i < request.TargetQty.Value; i++)
                    {
                        var pulling = new PullingRecord
                        {
                            ItemId       = item.ItemId,
                            Plant        = string.IsNullOrEmpty(item.Plant) ? "Unknown" : (item.Plant.Length > 20 ? item.Plant.Substring(0, 20) : item.Plant),
                            Rack         = rk,
                            Column       = cn,
                            Tag          = tag,
                            Label        = batchLabel,
                            IsManualAdjust = true,
                            AdjustNote   = (request.Note ?? "Sync Adjust").Trim().Length > 500 ? (request.Note ?? "Sync Adjust").Trim().Substring(0, 500) : (request.Note ?? "Sync Adjust").Trim(),
                            CreatedBy    = createdBy,
                            CreatedDate  = now
                        };
                        _context.PullingRecords.Add(pulling);
                    }
                }

                await _context.SaveChangesAsync();
                _stockCache.Invalidate();
                await _hubContext.Clients.All.SendAsync("updateStock");
                return Json(new { success = true, message = $"Stock {rk}.{cn} berhasil diupdate ke {request.TargetQty}.", newStock = request.TargetQty });
            }

            // Legacy STO Logic (Only for Delta Adjustments)

            // Legacy STO Logic (Only for Delta Adjustments)
            if (request.Qty.GetValueOrDefault() == 0)
            {
                // Normalisasi lokasi RAK dari request
                var stoRack   = !string.IsNullOrWhiteSpace(request.Rack) ? request.Rack.Trim().ToUpper() : "";
                var stoNoRack = request.NoRack ?? 0;
                var hasSpecificRack = !string.IsNullOrEmpty(stoRack);

                // Query available (unconsumed) pulling records untuk item ini
                var itemTag = (item.VIN ?? item.ItemCode ?? "N/A").Trim().ToUpper();
                var availableQuery = _context.PullingRecords
                    .Where(r => (r.ItemId == item.ItemId || (itemTag != "N/A" && r.Tag != null && r.Tag.Trim().ToUpper() == itemTag))
                             && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

                // Jika lokasi RAK spesifik disediakan, hanya zero-kan di lokasi itu saja
                if (hasSpecificRack)
                {
                    availableQuery = availableQuery.Where(r => r.Rack == stoRack && r.Column == stoNoRack);
                }

                var availableRecords = await availableQuery.ToListAsync();

                int consumedCount = 0;
                foreach (var pulling in availableRecords)
                {
                    var stoText = " (STO - Stock To Zero)";
                    var baseKanban = (request.Note?.Trim() ?? "STO");
                    if (baseKanban.Length + stoText.Length > 100) baseKanban = baseKanban.Substring(0, 100 - stoText.Length);

                    var prep = new PreparationRecord
                    {
                        Tag = string.IsNullOrEmpty(pulling.Tag) ? "N/A" : (pulling.Tag.Length > 100 ? pulling.Tag.Substring(0, 100) : pulling.Tag),
                        Label = string.IsNullOrEmpty(pulling.Label) ? "N/A" : (pulling.Label.Length > 100 ? pulling.Label.Substring(0, 100) : pulling.Label),
                        Plant = "MADJUST",
                        Kanban = baseKanban + stoText,
                        ScheduleId = null,
                        CreatedBy = createdBy,
                        CreatedDate = now
                    };
                    _context.PreparationRecords.Add(prep);
                    consumedCount++;
                }

                // Buat Zero Stock marker pulling record agar lokasi rak tetap tampil di dashboard
                var tag = item.VIN ?? item.ItemCode ?? "N/A";
                if (tag.Length > 100) tag = tag.Substring(0, 100);

                var effectiveRack   = hasSpecificRack ? stoRack : (item.Rack ?? "-");
                if (effectiveRack.Length > 5) effectiveRack = effectiveRack.Substring(0, 5);

                var effectiveNoRack = hasSpecificRack ? stoNoRack : (item.NoRack ?? 0);
                var zeroLabel = (item.VIN ?? "VIN") + "LB" + effectiveRack + effectiveNoRack.ToString();
                if (zeroLabel.Length > 100) zeroLabel = zeroLabel.Substring(0, 100);

                var zeroMarker = new PullingRecord
                {
                    ItemId         = item.ItemId,
                    Plant          = string.IsNullOrEmpty(item.Plant) ? "Unknown" : (item.Plant.Length > 20 ? item.Plant.Substring(0, 20) : item.Plant),
                    Rack           = effectiveRack,
                    Column         = effectiveNoRack,
                    Tag            = tag,
                    Label          = zeroLabel,
                    Quantity       = 0, // Set Qty ke 0 agar di log transaksi tidak muncul sebagai 1 Box
                    IsManualAdjust = true,
                    AdjustNote     = "Zero Stock",
                    CreatedBy      = createdBy,
                    CreatedDate    = now
                };
                _context.PullingRecords.Add(zeroMarker);

                await _context.SaveChangesAsync();

                var locationDesc = hasSpecificRack ? $" di lokasi {stoRack}.{stoNoRack}" : "";
                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "STO",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"STO (Stock To Zero){locationDesc} - Consumed {consumedCount} pcs. Catatan: {request.Note}",
                    performedBy: createdBy
                );

                // Broadcast update
                _stockCache.Invalidate();
                await _hubContext.Clients.All.SendAsync("updateStock");

                return Json(new { 
                    success = true, 
                    message = $"STO berhasil! Stock {item.ItemName} di-nol-kan{locationDesc}.",
                    newStock = 0,
                    consumedCount = consumedCount
                });
            }

            if (request.Qty.GetValueOrDefault() > 0)
            {
                // Gunakan lokasi dari REQUEST (bukan master item) agar setiap lokasi
                // fisik yang berbeda (Rack+NoRack berbeda) membentuk baris terpisah di dashboard FG.
                var effectiveRack   = !string.IsNullOrWhiteSpace(request.Rack) 
                                      ? request.Rack.Trim().ToUpper() 
                                      : (item.Rack ?? "-");
                if (effectiveRack.Length > 5) effectiveRack = effectiveRack.Substring(0, 5);

                var effectiveNoRack = request.NoRack ?? item.NoRack ?? 0;

                var tag = item.VIN ?? item.ItemCode ?? "N/A";
                if (tag.Length > 100) tag = tag.Substring(0, 100);

                // batchLabel dibuat UNIK TOTAL per transaksi (VIN + LB + Rack + NoRack + Ticks)
                // agar benar-benar terisolasi dari "Ghost Prep" (PreparationRecord lama tanpa Pulling).
                var batchLabel = tag + "LB" + effectiveRack + effectiveNoRack.ToString() + "U" + now.Ticks.ToString().Substring(10);
                if (batchLabel.Length > 100) batchLabel = batchLabel.Substring(0, 100);

                for (int i = 0; i < request.Qty.GetValueOrDefault(); i++)
                {
                    var pulling = new PullingRecord
                    {
                        ItemId       = item.ItemId,
                        Plant        = string.IsNullOrEmpty(item.Plant) ? "Unknown" : (item.Plant.Length > 20 ? item.Plant.Substring(0, 20) : item.Plant),
                        Rack         = effectiveRack,
                        Column       = effectiveNoRack,
                        Tag          = tag,
                        Label        = batchLabel,
                        IsManualAdjust = true,
                        AdjustNote   = (request.Note ?? "").Trim().Length > 500 ? (request.Note ?? "").Trim().Substring(0, 500) : (request.Note ?? "").Trim(),
                        CreatedBy    = createdBy,
                        CreatedDate  = now
                    };
                    _context.PullingRecords.Add(pulling);
                }

                await _context.SaveChangesAsync();

                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "Adjust",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"TAMBAH +{request.Qty.GetValueOrDefault()} pcs. Catatan: {request.Note}",
                    performedBy: createdBy
                );
            }
            else
            {
                // REDUCE: consume existing PullingRecords via PreparationRecords
                int reduceQty = Math.Abs(request.Qty.GetValueOrDefault());

                // Normalisasi lokasi sumber (jika disertakan dari edit baris)
                var srcRack   = (request.SourceRack ?? "").Trim().ToUpper();
                var srcNoRack = request.SourceNoRack;

                // Query base
                var itemTag = (item.VIN ?? item.ItemCode ?? "N/A").Trim().ToUpper();
                var availableQuery = _context.PullingRecords
                    .Where(r => (r.ItemId == item.ItemId || (itemTag != "N/A" && r.Tag != null && r.Tag.Trim().ToUpper() == itemTag))
                             && !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

                // Jika ada lokasi sumber spesifik, filter ke lokasi itu saja
                if (!string.IsNullOrWhiteSpace(srcRack) && srcNoRack.HasValue)
                {
                    availableQuery = availableQuery
                        .Where(r => r.Rack == srcRack && r.Column == srcNoRack.Value);
                }

                var available = await availableQuery
                    .OrderBy(r => r.CreatedDate)
                    .Take(reduceQty)
                    .ToListAsync();

                if (available.Count < reduceQty)
                    return Json(new { success = false, message = $"Stok tidak cukup di lokasi ini. Tersedia: {available.Count} pcs." });

                foreach (var pr in available)
                {
                    var prep = new PreparationRecord
                    {
                        Plant = "MADJUST",
                        Tag = pr.Tag,
                        Label = pr.Label,
                        Kanban = (request.Note ?? "").Trim().Length > 100 ? (request.Note ?? "").Trim().Substring(0, 100) : (request.Note ?? "").Trim(),
                        ScheduleId = null,
                        CreatedBy = createdBy,
                        CreatedDate = now
                    };
                    _context.PreparationRecords.Add(prep);
                }

                await _context.SaveChangesAsync();

                await _activityLogService.LogActivity(
                    module: "ManualAdjust",
                    action: "Adjust",
                    entityName: $"{item.ItemName} ({item.VIN})",
                    entityId: item.ItemId,
                    description: $"KURANGI -{reduceQty} pcs" + (!string.IsNullOrWhiteSpace(srcRack) ? $" dari lokasi {srcRack}.{srcNoRack}" : "") + $". Catatan: {request.Note}",
                    performedBy: createdBy
                );
            }

            // Broadcast SignalR
            _stockCache.Invalidate();
            await _hubContext.Clients.All.SendAsync("updateStock");

            // Return updated stock
            var itemTag2 = (item.VIN ?? item.ItemCode ?? "N/A").Trim().ToUpper();
            var newStock = await _context.PullingRecords
                .CountAsync(r => (r.ItemId == item.ItemId || (itemTag2 != "N/A" && r.Tag != null && r.Tag.Trim().ToUpper() == itemTag2)) &&
                                 !_context.PreparationRecords.Any(p => p.Tag == r.Tag && p.Label == r.Label));

            string direction = request.Qty.GetValueOrDefault() > 0 ? $"+{request.Qty.GetValueOrDefault()}" : $"{request.Qty.GetValueOrDefault()}";
            return Json(new { success = true, message = $"Manual Adjust berhasil: {direction} pcs untuk {item.ItemName}.", newStock });
            }
            catch (Exception ex)
            {
                var errMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return Json(new { success = false, message = "System Error: " + errMsg });
            }
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LogNG([FromBody] PullingNGLogRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Tag)) return Json(new { ok = false });
            var createdBy = HttpContext.Session.GetString("FullName") 
                         ?? HttpContext.Session.GetString("Username") 
                         ?? "Operator";
            var rec = new DeliveryControl.Models.ScanNGLog
            {
                Module = "Pulling",
                Tag = req.Tag.Trim(),
                Label = req.Label?.Trim() ?? "",
                Kanban = "",
                Reason = req.Reason?.Trim() ?? "",
                CreatedBy = createdBy,
                CreatedDate = DateTime.Now
            };
            _context.ScanNGLogs.Add(rec);
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        /// <summary>
        /// Relokasi stok dari satu lokasi (Rack+NoRack) ke lokasi lain.
        /// Jika lokasi tujuan sudah ada item yang sama (VIN+Rack+NoRack), otomatis merge di dashboard.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RelocateStock([FromBody] RelocateStockRequest request)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "Admin" && role != "Super Admin" && role != "STO")
                return Json(new { success = false, message = "Hanya Admin, Super Admin, atau STO yang dapat merelokasi stok." });

            if (request.ItemId <= 0)
                return Json(new { success = false, message = "Item tidak valid." });

            var item = await _context.Items.FindAsync(request.ItemId);
            if (item == null)
                return Json(new { success = false, message = "Item tidak ditemukan." });

            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";

            // Normalisasi source location
            var srcRack   = (request.SourceRack ?? "").Trim().ToUpper();
            var srcNoRack = request.SourceNoRack ?? 0;

            // Normalisasi target location
            var tgtRack   = (request.TargetRack ?? "").Trim().ToUpper();
            var tgtNoRack = request.TargetNoRack ?? 0;

            // Label baru untuk lokasi tujuan (agar grouping dashboard berubah)
            var oldLabel = (item.VIN ?? "") + "LB" + srcRack + srcNoRack.ToString();
            var newLabel = (item.VIN ?? "") + "LB" + tgtRack + tgtNoRack.ToString();

            // 1. Ambil SEMUA data historis item ini untuk re-kalkulasi FIFO (Identik dashboard)
            var itemTag = (item.VIN ?? item.ItemCode ?? "N/A").Trim().ToUpper();
            var allPulling = await _context.PullingRecords.AsNoTracking()
                .Where(p => p.Remark != "Mismatch" && (p.ItemId == request.ItemId || (p.Tag ?? "").Trim().ToUpper() == itemTag))
                .OrderBy(p => p.CreatedDate).ThenBy(p => p.PullingId)
                .ToListAsync();

            var preparationQuery = _context.PreparationRecords.AsNoTracking()
                .Where(p => p.Remark != "Mismatch" && (p.Tag ?? "").Trim().ToUpper() == itemTag);

            // [FIX] Align with dashboard plant filtering
            if (!string.IsNullOrEmpty(request.Plant) && request.Plant != "Overall")
            {
                preparationQuery = preparationQuery.Where(p => p.Plant == request.Plant || p.Plant == "MADJUST");
            }

            var allPrep = await preparationQuery
                .OrderBy(p => p.CreatedDate).ThenBy(p => p.PreparationId)
                .ToListAsync();

            // 2. Jalankan matching FIFO (Identik 100% dengan dashboard & ManualAdjust)
            var pullingLookup = allPulling.ToLookup(p => (p.Tag ?? "").Trim().ToUpper());
            var consumedIds = new HashSet<int>();

            // Cek apakah VIN ini unik di master data (untuk relaxed fallback)
            // Pre-compute normalized VIN as local string (EF Core cannot translate VinHelper.Normalize() to SQL)
            var normalizedItemTag3 = VinHelper.Normalize(itemTag);
            var activeItemCountForVin3 = await _context.Items
                .CountAsync(i => i.IsActive && !i.IsDeleted && i.VIN != null &&
                                (i.VIN.ToUpper() == itemTag || i.VIN.ToUpper() == normalizedItemTag3));
            bool isUniqueVin3 = activeItemCountForVin3 == 1;

            foreach (var prep in allPrep)
            {
                var pTag   = (prep.Tag   ?? "").Trim().ToUpper();
                var pLabel = (prep.Label ?? "").Trim().ToUpper();

                var candidates = pullingLookup[pTag];

                // Prioritas 1: Tag + Label + 10s
                var match = candidates.FirstOrDefault(p => 
                    !consumedIds.Contains(p.PullingId) && 
                    (p.Label ?? "").Trim().ToUpper() == pLabel && 
                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                // Prioritas 2: Fallback label compatible (menggunakan VinHelper.Normalize)
                if (match == null)
                {
                    var normPLabel = VinHelper.Normalize(pLabel);
                    bool labelCompatibleMatch = !string.IsNullOrEmpty(pLabel) && candidates.Any(p =>
                        !consumedIds.Contains(p.PullingId) &&
                        (p.AdjustNote ?? "") != "Zero Stock" &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                        (VinHelper.Normalize(p.Label) == normPLabel ||
                         VinHelper.Normalize(p.Label).StartsWith(normPLabel) ||
                         normPLabel.StartsWith(VinHelper.Normalize(p.Label))));

                    if (labelCompatibleMatch)
                    {
                        match = candidates.FirstOrDefault(p =>
                            !consumedIds.Contains(p.PullingId) &&
                            (p.AdjustNote ?? "") != "Zero Stock" &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                            (VinHelper.Normalize(p.Label) == normPLabel ||
                             VinHelper.Normalize(p.Label).StartsWith(normPLabel) ||
                             normPLabel.StartsWith(VinHelper.Normalize(p.Label))));
                    }
                    if (match == null)
                    {
                        match = candidates.FirstOrDefault(p =>
                            !consumedIds.Contains(p.PullingId) &&
                            (p.AdjustNote ?? "") != "Zero Stock" &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                    }
                }

                if (match != null) consumedIds.Add(match.PullingId);
            }

            // 3. Filter PullingRecords yang "Aktif" (Unconsumed) di lokasi sumber
            var recordsToMoveIds = allPulling
                .Where(r => !consumedIds.Contains(r.PullingId) 
                         && r.ItemId == request.ItemId
                         && (r.Rack ?? "").Trim().ToUpper() == srcRack
                         && r.Column == srcNoRack)
                .Select(r => r.PullingId)
                .ToList();

            if (!recordsToMoveIds.Any())
                return Json(new { success = false, message = $"Tidak ada stok AKTIF di lokasi {srcRack}.{srcNoRack} untuk item ini." });

            // 4. Batasi sesuai Qty request jika ada
            if (request.Qty.HasValue && request.Qty.Value > 0)
            {
                if (request.Qty.Value > recordsToMoveIds.Count)
                    return Json(new { success = false, message = $"Qty relokasi ({request.Qty.Value}) melebihi stok aktif ({recordsToMoveIds.Count})." });
                recordsToMoveIds = recordsToMoveIds.Take(request.Qty.Value).ToList();
            }

            // Ambil data asli untuk di-update
            var recordsToMove = await _context.PullingRecords
                .Where(p => recordsToMoveIds.Contains(p.PullingId))
                .ToListAsync();

            // Jika Qty dispesifikasikan, ambil hanya sejumlah tersebut
            if (request.Qty.HasValue && request.Qty.Value > 0)
            {
                if (request.Qty.Value > recordsToMove.Count)
                {
                    return Json(new { success = false, message = $"Qty relokasi ({request.Qty.Value}) melebihi stok yang ada ({recordsToMove.Count})." });
                }
                recordsToMove = recordsToMove.OrderBy(r => r.CreatedDate).Take(request.Qty.Value).ToList();
            }

            int movedCount = recordsToMove.Count;

            foreach (var pr in recordsToMove)
            {
                var prOldLabel = pr.Label;
                pr.Rack   = tgtRack;
                pr.Column = tgtNoRack;
                pr.Label  = newLabel;  // Update label agar grouping dashboard ikut berubah/merge
                pr.AdjustNote = (request.Note?.Trim() ?? "Relokasi Stok") + $" ({srcRack}.{srcNoRack} → {tgtRack}.{tgtNoRack})";
                
                // Update pula PreparationRecords yang menggunakan label lama dan tag yang sama
                // agar FIFO matching tidak rusak
                var prepToUpdate = await _context.PreparationRecords
                    .Where(p => p.Label == prOldLabel && p.Tag == pr.Tag)
                    .ToListAsync();
                foreach (var p in prepToUpdate)
                {
                    p.Label = newLabel;
                }
            }

            await _context.SaveChangesAsync();

            await _activityLogService.LogActivity(
                module: "ManualAdjust",
                action: "Relocate",
                entityName: $"{item.ItemName} ({item.VIN})",
                entityId: item.ItemId,
                description: $"RELOKASI {movedCount} pcs dari {srcRack}.{srcNoRack} ke {tgtRack}.{tgtNoRack}. Catatan: {request.Note}",
                performedBy: createdBy
            );

            _stockCache.Invalidate();
            await _hubContext.Clients.All.SendAsync("updateStock");

            return Json(new { success = true, message = $"Berhasil merelokasi {movedCount} pcs dari {srcRack}.{srcNoRack} ke {tgtRack}.{tgtNoRack}.", movedCount });
        }

        [HttpPost]
        public async Task<IActionResult> HideRack(int itemId, string rack, int noRack)
        {
            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var now = DateTime.Now;

            var item = await _context.Items.FindAsync(itemId);
            var tag = (item?.VIN ?? "").Trim().ToUpper();

            // Ambil semua pulling di rak ini yang belum di-Mismatch
            var records = await _context.PullingRecords
                .Where(r => (r.ItemId == itemId || (tag != "" && r.Tag != null && r.Tag.Trim().ToUpper() == tag)) && r.Rack == rack && r.Column == noRack && r.Remark != "Mismatch")
                .ToListAsync();

            if (!records.Any())
                return Json(new { success = false, message = "Tidak ada riwayat aktif untuk rak ini." });

            // Cek apakah record sudah dikonsumsi oleh PreparationRecord.
            // Record yang sudah di-match ke prep (label sama) adalah bagian dari riwayat transaksi
            // dan TIDAK boleh dihapus, tapi ditandai agar tidak muncul di breakdown lokasi
            var rawConsumedLabels = await _context.PreparationRecords
                .Where(p => p.Remark != "Mismatch")
                .Select(p => p.Label)
                .Distinct()
                .ToListAsync();

            var consumedLabelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in rawConsumedLabels)
                if (!string.IsNullOrEmpty(l)) consumedLabelSet.Add(l);

            var toDelete = new List<PullingRecord>();
            int deletedCount = 0;
            int hiddenConsumedCount = 0;
            foreach (var r in records)
            {
                // Jangan hapus record yang label-nya sudah dikonsumsi oleh Preparation
                if (!string.IsNullOrEmpty(r.Label) && consumedLabelSet.Contains(r.Label))
                {
                    if (!(r.AdjustNote ?? "").Contains("Rack Hidden"))
                    {
                        r.AdjustNote = (r.AdjustNote ?? "") + " | Rack Hidden";
                        hiddenConsumedCount++;
                    }
                    continue;
                }

                toDelete.Add(r);
                deletedCount++;
            }

            if (deletedCount == 0 && hiddenConsumedCount == 0)
                return Json(new { success = false, message = "Rak ini sudah disembunyikan atau dihapus sebelumnya." });

            if (toDelete.Any())
            {
                foreach (var r in toDelete)
                {
                    r.Remark = "Mismatch";
                    r.AdjustNote = (r.AdjustNote ?? "") + " | Rack Deleted";
                }
            }
                
            await _context.SaveChangesAsync();

            await _activityLogService.LogActivity(
                module: "Stock",
                action: "DeleteRack",
                entityName: $"Item {itemId} Rack {rack}.{noRack}",
                entityId: itemId,
                description: $"Menghapus permanen {deletedCount} record dan menyembunyikan {hiddenConsumedCount} riwayat di rak {rack}.{noRack} untuk item {itemId}",
                performedBy: createdBy
            );

            _stockCache.Invalidate();
            if (_hubContext != null) 
            {
                await _hubContext.Clients.All.SendAsync("updateStock");
            }

            return Json(new { success = true, message = "Baris item di lokasi tersebut berhasil dihapus permanen." });
        }

        /// <summary>
        /// Perbaiki data label yang kosong akibat HideRack lama yang salah me-Mismatch record
        /// yang sudah dikonsumsi oleh PreparationRecord. Record tersebut dikembalikan ke kondisi semula.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RepairHideRackLabels()
        {
            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";

            // Cari semua pulling yang di-Mismatch oleh HideRack (tandanya ada "Rack Removed" di AdjustNote)
            var wronglyMismatched = await _context.PullingRecords
                .Where(r => r.Remark == "Mismatch"
                         && r.AdjustNote != null && r.AdjustNote.Contains("Rack Removed"))
                .ToListAsync();

            if (!wronglyMismatched.Any())
                return Json(new { success = true, repaired = 0, message = "Tidak ada data yang perlu diperbaiki." });

            // Kumpulkan label yang ada di PreparationRecord (sudah dikonsumsi)
            var consumedLabels = await _context.PreparationRecords
                .Where(p => p.Label != null && p.Label != "")
                .Select(p => p.Label)
                .Distinct()
                .ToListAsync();

            var consumedSet = consumedLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);

            int repaired = 0;
            foreach (var r in wronglyMismatched)
            {
                // Hanya perbaiki jika label-nya ada di Preparation (artinya record ini seharusnya
                // tidak di-Mismatch oleh HideRack — ia adalah bagian dari riwayat transaksi)
                if (!string.IsNullOrEmpty(r.Label) && consumedSet.Contains(r.Label))
                {
                    r.Remark = "Match";
                    // Hapus suffix " | Rack Removed at ..." dari AdjustNote
                    if (r.AdjustNote != null)
                    {
                        var idx = r.AdjustNote.IndexOf(" | Rack Removed at ", StringComparison.Ordinal);
                        if (idx >= 0)
                            r.AdjustNote = r.AdjustNote[..idx].TrimEnd();
                        if (string.IsNullOrWhiteSpace(r.AdjustNote))
                            r.AdjustNote = null;
                    }
                    repaired++;
                }
            }

            if (repaired == 0)
                return Json(new { success = true, repaired = 0, message = "Tidak ada label yang perlu dipulihkan (label tidak ditemukan di Preparation)." });

            await _context.SaveChangesAsync();

            await _activityLogService.LogActivity(
                module: "Stock",
                action: "RepairHideRackLabels",
                entityName: "Bulk Repair",
                entityId: 0,
                description: $"Memulihkan {repaired} record pulling yang salah di-Mismatch oleh HideRack",
                performedBy: createdBy
            );

            return Json(new { success = true, repaired, message = $"{repaired} label berhasil dipulihkan." });
        }
    }
}

public class ManualAdjustRequest
{
    public int ItemId { get; set; }
    public int? Qty { get; set; }
    public int? TargetQty { get; set; }
    public string? Note { get; set; }
    public string? Rack { get; set; }
    public int? NoRack { get; set; }
    /// <summary>Lokasi asal (untuk deduct dari lokasi spesifik saat qty negatif)</summary>
    public string? SourceRack { get; set; }
    public int? SourceNoRack { get; set; }
    public string? Plant { get; set; }
}

public class RelocateStockRequest
{
    public int ItemId { get; set; }
    public string? SourceRack { get; set; }
    public int? SourceNoRack { get; set; }
    public string? TargetRack { get; set; }
    public int? TargetNoRack { get; set; }
    public int? Qty { get; set; }
    public string? Note { get; set; }
    public string? Plant { get; set; }
}

public class PullingNGLogRequest
{
    public string Module { get; set; } = "Pulling";
    public string? Tag { get; set; }
    public string? Label { get; set; }
    public string? Kanban { get; set; }
    public string? Reason { get; set; }
}

// ─── Helper: Urutkan item agar customer NON-EKS diprioritaskan ───────────────
// Aturan: item yang Customer-nya TIDAK mengandung "EKS" diletakkan di depan.
// Ini memastikan scan Pulling/Prepare selalu mengambil item HMMI bukan HMMI EKS
// ketika kedua item ada dan VIN-nya sama.
public static class ItemPriorityHelper
{
    public static List<Item> PrioritizeNonEks(IEnumerable<Item> items)
    {
        return items
            .OrderBy(i => IsEksCustomer(i.Customer) ? 1 : 0) // non-EKS = 0 (duluan)
            .ThenByDescending(i => i.IsActive ? 1 : 0)        // aktif lebih prioritas
            .ThenBy(i => i.ItemId)                             // tiebreak: ID terkecil
            .ToList();
    }

    private static bool IsEksCustomer(string? customer)
    {
        if (string.IsNullOrWhiteSpace(customer)) return false;
        return customer.Trim().EndsWith(" EKS", StringComparison.OrdinalIgnoreCase)
            || customer.Trim().Contains(" EKS ", StringComparison.OrdinalIgnoreCase)
            || customer.Trim().Equals("EKS", StringComparison.OrdinalIgnoreCase);
    }
}

public class SetSkipLabelModeRequest
{
    public bool Active { get; set; }
}
