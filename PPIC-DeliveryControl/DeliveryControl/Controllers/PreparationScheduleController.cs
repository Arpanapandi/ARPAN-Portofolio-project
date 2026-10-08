using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels; // Assumed namespace for view models
using ClosedXML.Excel; // Required for Excel
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Hubs;
using System.IO;
using DeliveryControl.Helpers;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin")]
    public class PreparationScheduleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly PreparationSyncService _syncService;
        private readonly SmartImportService _smartImportService;
        private readonly ILogger<PreparationScheduleController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public PreparationScheduleController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, PreparationSyncService syncService, SmartImportService smartImportService, ILogger<PreparationScheduleController> logger, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _hubContext = hubContext;
            _syncService = syncService;
            _smartImportService = smartImportService;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        // GET: PreparationSchedule
        public async Task<IActionResult> Index(DateTime? filterDate, int? filterCustomerId, string? q, int pageNumber = 1)
        {
            try {
                var dateToFilter = filterDate ?? DateTime.Today;
                ViewBag.FilterDate = dateToFilter.ToString("yyyy-MM-dd");
                ViewBag.FilterCustomerId = filterCustomerId ?? 0;
                ViewBag.SearchKeyword = q;

                var query = _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems)
                        .ThenInclude(di => di.Item)
                    .AsNoTracking();

                if (!string.IsNullOrEmpty(q))
                {
                    var keyword = q.Trim().ToLower();
                    query = query.Where(s =>
                        (s.ScheduleNumber != null && s.ScheduleNumber.ToLower().Contains(keyword)) ||
                        (s.Customer != null && s.Customer.CustomerName.ToLower().Contains(keyword)) ||
                        (s.Customer != null && s.Customer.CustomerCode.ToLower().Contains(keyword)) ||
                        (s.Route != null && s.Route.ToLower().Contains(keyword)) ||
                        (s.Cycle != null && s.Cycle.ToLower().Contains(keyword)) ||
                        (s.DeliveryItems.Any(di =>
                            (di.Item != null && (
                                (di.Item.VIN != null && di.Item.VIN.ToLower().Contains(keyword)) ||
                                (di.Item.CustomerPartNumber != null && di.Item.CustomerPartNumber.ToLower().Contains(keyword)) ||
                                (di.Item.ItemCode != null && di.Item.ItemCode.ToLower().Contains(keyword))
                            ))
                        ))
                    );
                    query = query.OrderByDescending(s => s.ScheduledDate).ThenBy(s => s.ScheduleNumber);
                }
                else
                {
                    var tomorrow = dateToFilter.Date.AddDays(1);
                    query = query.Where(s =>
                        s.ScheduledDate.Date == dateToFilter.Date ||
                        // Load besok juga; H-1 filtering dilakukan in-memory setelah ToListAsync
                        s.ScheduledDate.Date == tomorrow.Date);
                    if (filterCustomerId.HasValue && filterCustomerId.Value > 0)
                    {
                        query = query.Where(s => s.CustomerId == filterCustomerId.Value);
                    }
                    // OrderBy dilakukan in-memory (string.Equals+StringComparison tidak bisa ditranslasi SQL Server)
                }

                var allSchedules = await query.ToListAsync();

                // H-1 in-memory filter + sort in-memory
                if (string.IsNullOrEmpty(q))
                {
                    allSchedules = allSchedules
                        .Where(s => s.ScheduledDate.Date == dateToFilter.Date || IsPreparationH1Schedule(s, dateToFilter))
                        .OrderBy(s => (s.Status ?? "").Trim().Equals("Completed", StringComparison.OrdinalIgnoreCase) ? 2 :
                                      (s.Status ?? "").Trim().Equals("In Progress", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(s => s.ScheduleNumber)
                        .ToList();
                }

                // Flat item rows
                var flatRows = allSchedules
                    .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                        ? s.DeliveryItems.Select(di => (Schedule: s, Item: (DeliveryItem?)di))
                        : new[] { (Schedule: s, Item: (DeliveryItem?)null) })
                    .AsEnumerable();

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var keyword = q.Trim().ToLower();
                    flatRows = flatRows.Where(row => {
                        var s = row.Schedule;
                        var item = row.Item?.Item;
                        bool scheduleMatch = (s.ScheduleNumber ?? "").ToLower().Contains(keyword) ||
                                           (s.Customer?.CustomerName ?? "").ToLower().Contains(keyword) ||
                                           (s.Customer?.CustomerCode ?? "").ToLower().Contains(keyword) ||
                                           (s.Route ?? "").ToLower().Contains(keyword) ||
                                           (s.Cycle ?? "").ToLower().Contains(keyword);
                        bool itemMatch = item != null && (
                            (item.VIN ?? "").ToLower().Contains(keyword) ||
                            (item.CustomerPartNumber ?? "").ToLower().Contains(keyword) ||
                            (item.ItemCode ?? "").ToLower().Contains(keyword) ||                            (item.ItemName ?? "").ToLower().Contains(keyword)
                        );
                        return scheduleMatch || itemMatch;
                    });
                }

                var finalRows = flatRows
                    .OrderBy(r => r.Schedule.Cycle ?? "")
                    .ThenBy(r => {
                        var di = r.Item;
                        if (di == null) return 1; // Waiting
                        var act = di.ActualQuantity ?? 0;
                        var qty = di.Quantity;
                        if (act > 0 && act < qty) return 0; // Preparing
                        if (act >= qty) return 2;           // Completed
                        return 1;                           // Waiting
                    })
                    .ThenBy(r => r.Schedule.ScheduleNumber)
                    .ToList();
                int pageSize = 20;
                int totalItems = finalRows.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
                if (pageNumber < 1) pageNumber = 1;
    
                var pagedRows = finalRows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
    
                // Reconstruct Model for View
                var model = pagedRows
                    .GroupBy(r => r.Schedule.ScheduleId)
                    .Select(g => {
                        var s = g.First().Schedule;
                        s.DeliveryItems = g.Where(r => r.Item != null).Select(r => r.Item!).ToList();
                        return s;
                    })
                    .ToList();
    
                ViewBag.CurrentPage = pageNumber;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalItems = totalItems;
                ViewBag.RouteData = new Dictionary<string, string> { { "filterDate", dateToFilter.ToString("yyyy-MM-dd") } };
                if (!string.IsNullOrEmpty(q)) ViewBag.RouteData["q"] = q;
    
                ViewBag.ActiveDockCount = await _context.Customers.CountAsync(c => c.IsActive);
                var customerItems = await _context.Customers
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.CustomerCode)
                    .Select(c => new {
                        c.CustomerId,
                        DisplayName = $"[{c.CustomerName}] {c.CustomerCode} | Rute: {c.Route ?? "-"} | Cycle: {c.Cycle ?? "-"}"
                    })
                    .ToListAsync();
                ViewBag.DockFilterList = customerItems.Select(c => new SelectListItem { Value = c.CustomerId.ToString(), Text = c.DisplayName }).ToList();
                ViewBag.Customers = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(customerItems, "CustomerId", "DisplayName", filterCustomerId);
    
                ViewBag.PendingPrepCount = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");
    
                return View(model);
            } catch (Exception ex) {
                return Content(ex.ToString());
            }
        }

        private static bool IsScheduleCompleted(DeliverySchedule s)
        {
            if (s.ActualEndTime.HasValue) return true;
            if (string.Equals((s.Status ?? string.Empty).Trim(), "Completed", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.DeliveryItems != null && s.DeliveryItems.Any())
            {
                return s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
            }
            return false;
        }

        private static bool IsPreparationH1Schedule(DeliverySchedule s, DateTime currentDate)
        {
            if (s.ScheduledDate.Date != currentDate.AddDays(1)) return false;
            if (IsScheduleCompleted(s)) return false;

            // 1. Jika Pickup pagi (sebelum 12:00 siang), selalu tampilkan di H-1
            if (s.PickupTime.HasValue && s.PickupTime.Value.Hour < 12) return true;

            // 2. Jika Pickup siang/sore (>= 12:00), cek waktu mulai persiapan (StartPrepareTime)
            // Jika jadwal diset untuk mulai persiapan dini hari besok (< 07:00 pagi atau < 420 menit),
            // berarti itu adalah bagian dari pekerjaan Shift 3 hari ini. Tampilkan!
            var startPrepMins = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
            if (startPrepMins > 0 && startPrepMins < 420) return true;

            return false;
        }

        /// <summary>
        /// Manual trigger untuk sinkronisasi pending preparations ke jadwal yang sudah ada.
        /// Mengembalikan JSON (dipanggil via fetch dari JavaScript).
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SyncPendingNow()
        {
            try
            {
                int synced = await _syncService.SyncAllPendingAsync();

                // Hitung sisa pending setelah sinkronisasi (PreparationRecord tanpa ScheduleId = pending)
                var remaining = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");

                string message = synced > 0
                    ? $"{synced} record preparation PENDING berhasil disinkronisasi ke jadwal."
                    : "Tidak ada pending preparation yang bisa disinkronisasi saat ini.";

                return Json(new { success = true, message, synced, remaining });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}", synced = 0, remaining = 0 });
            }
        }

        /// <summary>
        /// Mengembalikan jumlah PreparationRecord yang masih pending (ScheduleId == null).
        /// Dipanggil via fetch dari JavaScript di beberapa halaman (PreparationSchedule/Index,
        /// PreparationWorkflow/Index, PreparationDashboard).
        /// Response: { total: N, count: N, canSync: N }
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPendingCount()
        {
            var total = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null && p.Remark != "Deleted");
            return Json(new { total, count = total, canSync = total > 0 ? 1 : 0 });
        }

        /// <summary>
        /// Server-side search lintas halaman — query DB berdasarkan keyword.
        /// Mencari: Manifest (ScheduleNumber), Customer Code, Customer Name (Dock),
        ///          Route, Cycle, VIN, Part No, CustomerPartNumber.
        /// Parameter: q=keyword, filterDate=yyyy-MM-dd (opsional, null = semua tanggal), page, pageSize
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Search(string? q, string? filterDate, int page = 1, int pageSize = 20)
        {
            try
            {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Json(new { total = 0, totalPages = 0, page = 1, items = Array.Empty<object>() });

            var keyword = q.Trim().ToLower();

            // Query base — join ke Customer dan Items
            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Filter keyword — pindahkan pencarian ke level database (EF Core) untuk performa
            // Kita cari di: ScheduleNumber, CustomerCode, CustomerName, Route, Cycle, VIN, PartNo
            query = query.Where(s => 
                (s.ScheduleNumber != null && s.ScheduleNumber.ToLower().Contains(keyword)) ||
                (s.Customer != null && (
                    (s.Customer.CustomerCode != null && s.Customer.CustomerCode.ToLower().Contains(keyword)) ||
                    (s.Customer.CustomerName != null && s.Customer.CustomerName.ToLower().Contains(keyword))
                )) ||
                (s.Route != null && s.Route.ToLower().Contains(keyword)) ||
                (s.Cycle != null && s.Cycle.ToLower().Contains(keyword)) ||
                (s.DeliveryItems.Any(di => 
                    (di.Item != null && (
                        (di.Item.VIN != null && di.Item.VIN.ToLower().Contains(keyword)) ||
                        (di.Item.CustomerPartNumber != null && di.Item.CustomerPartNumber.ToLower().Contains(keyword)) ||
                        (di.Item.ItemCode != null && di.Item.ItemCode.ToLower().Contains(keyword)) ||
                        (di.Item.ItemName != null && di.Item.ItemName.ToLower().Contains(keyword))
                    ))
                ))
            );

            // Sorting: Tanggal terbaru/terdekat dulu
            query = query.OrderByDescending(s => s.ScheduledDate)
                         .ThenBy(s => s.ScheduleNumber);

            var totalSchedules = await query.CountAsync();
            
            // Karena satu schedule bisa punya banyak items (1 baris di tabel = 1 item), 
            // kita harus menarik data yang benar-benar cocok.
            var schedules = await query.ToListAsync();

            // Flat item rows — filter manual di memory untuk akurasi baris per item
            var matched = schedules
                .SelectMany(s => s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.Select(di => (Schedule: s, Item: (DeliveryItem?)di))
                    : new[] { (Schedule: s, Item: (DeliveryItem?)null) })
                .Where(row =>
                {
                    var s = row.Schedule;
                    var di = row.Item;
                    var item = di?.Item;

                    // 1. Cek apakah Schedule-level cocok (Manifest, Customer, Route, Cycle)
                    bool scheduleLevelMatch = 
                        (s.ScheduleNumber ?? "").ToLower().Contains(keyword) ||
                        (s.Customer?.CustomerName ?? "").ToLower().Contains(keyword) ||
                        (s.Customer?.CustomerCode ?? "").ToLower().Contains(keyword) ||
                        (s.Route ?? "").ToLower().Contains(keyword) ||
                        (s.Cycle ?? "").ToLower().Contains(keyword);

                    // 2. Cek apakah Item-level cocok (VIN, Part No, Item Code, Name)
                    bool itemLevelMatch = item != null && (
                        (item.VIN ?? "").ToLower().Contains(keyword) ||
                        (item.CustomerPartNumber ?? "").ToLower().Contains(keyword) ||
                        (item.ItemCode ?? "").ToLower().Contains(keyword) ||
                        (item.ItemName ?? "").ToLower().Contains(keyword)
                    );

                    // Jika user mencari Manifest, dia mau lihat SEMUA item di manifest itu (scheduleLevelMatch == true)
                    // Jika user mencari VIN/Part No, dia HANYA mau lihat item itu (itemLevelMatch == true)
                    // Jadi: tampilkan row jika Schedule cocok ATAU Item tersebut cocok.
                    return scheduleLevelMatch || itemLevelMatch;
                })
                .OrderBy(r => r.Schedule.Cycle ?? "")
                .ThenBy(r => {
                    var di = r.Item;
                    if (di == null) return 1; // Waiting
                    var act = di.ActualQuantity ?? 0;
                    var qty = di.Quantity;
                    if (act > 0 && act < qty) return 0; // Preparing
                    if (act >= qty) return 2;           // Completed
                    return 1;                           // Waiting
                })
                .ThenBy(r => r.Schedule.ScheduleNumber)
                .ToList();

            var total = matched.Count;
            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (page < 1) page = 1;
            
            var paged = matched.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Buat DTO plain — hindari circular reference saat serialisasi JSON
            var items = paged.Select((row, idx) => new {
                rowNo             = (page - 1) * pageSize + idx + 1,
                itemId            = row.Item?.DeliveryItemId ?? 0,
                scheduleId        = row.Schedule.ScheduleId,
                scheduleNumber    = row.Schedule.ScheduleNumber ?? "",
                scheduledDate     = row.Schedule.ScheduledDate.ToString("dd/MM/yyyy"),
                createdDate       = row.Schedule.CreatedDate.ToString("dd/MM HH:mm"),
                status            = row.Schedule.Status ?? "",
                preparationStatus = row.Schedule.PreparationStatus ?? "",
                dock              = row.Schedule.Customer?.CustomerName ?? "-",
                customerCode      = row.Schedule.Customer?.CustomerCode ?? "-",
                route             = row.Schedule.Route ?? "-",
                cycle             = row.Schedule.Cycle ?? "-",
                vin               = row.Item?.Item?.VIN ?? "",
                partNo            = row.Item?.Item?.CustomerPartNumber ?? row.Item?.Item?.ItemCode ?? "",
                externalPartNo    = row.Item?.ExternalPartNo ?? "",
                itemName          = row.Item?.Item?.ItemName ?? "",
                qty               = (int)(row.Item?.Quantity ?? 0),
                actualQty         = (int)(row.Item?.ActualQuantity ?? 0),
                qtyLot            = (int)(row.Item?.Item?.QtyLot ?? 0),
                kanbanActual      = row.Item?.CalculatedKanbanActual ?? 0,
                kanbanTarget      = row.Item?.CalculatedKanbanTarget ?? 0
            }).ToList();

            return Json(new { total, totalPages, page, pageSize, items });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Search] Error saat pencarian keyword={Keyword}", q);
                return Json(new { total = 0, totalPages = 0, page = 1, pageSize, items = Array.Empty<object>(), error = ex.GetType().Name + ": " + ex.Message });
            }
        }

        public async Task<IActionResult> Process(int id)
        {
             var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

             if(schedule == null) return NotFound();

             return View(schedule);
        }

        // GET: PreparationSchedule/Create
        public async Task<IActionResult> Create()
        {
            ViewData["CustomerId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Customers, "CustomerId", "CustomerName");
            
            var itemList = _context.Items
                .OrderBy(i => i.ItemName)
                .Select(i => new {
                    ItemId = i.ItemId,
                    DisplayName = $"{i.ItemName} | Rack: {i.Rack}-{i.NoRack} | VIN: {i.VIN}"
                })
                .ToList();

            ViewData["Items"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(itemList, "ItemId", "DisplayName");

            // Pre-populate ScheduledDate and ScheduleNumber
            var today = DateTime.Today;
            var seq = await GetNextSequenceInternal(today);
            var schedule = new DeliverySchedule
            {
                ScheduledDate = today,
                ScheduleNumber = $"SCH-{today:yyyyMMdd}{seq:D3}",
                Status = "Scheduled"
            };

            return View(schedule);
        }

        // POST: PreparationSchedule/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(List<int> itemIds, List<decimal> itemQtys, [Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,PickupTime,ETD,Range,SKID,Area,TotalTargetQuantity,Notes,Status")] DeliverySchedule deliverySchedule)
        {
            if (ModelState.IsValid)
            {
                if (itemIds == null || !itemIds.Any())
                {
                    ModelState.AddModelError("", "Setidaknya harus ada satu item dalam jadwal.");
                }
                else
                {
                    // Auto-generate schedule number ONLY if empty
                    if (string.IsNullOrEmpty(deliverySchedule.ScheduleNumber))
                    {
                        int sequence = await GetNextSequenceInternal(deliverySchedule.ScheduledDate);
                        deliverySchedule.ScheduleNumber = $"SCH-{deliverySchedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }
                    
                    deliverySchedule.CreatedDate = DateTime.Now;
                    deliverySchedule.CreatedBy = User.Identity?.Name ?? "PreparationPortal";

                    _context.Add(deliverySchedule);
                    
                    // Create DeliveryItems from lists
                    for (int i = 0; i < itemIds.Count; i++)
                    {
                        if (itemIds[i] > 0 && itemQtys.Count > i && itemQtys[i] > 0)
                        {
                            var dItem = new DeliveryItem
                            {
                                DeliverySchedule = deliverySchedule,
                                ItemId = itemIds[i],
                                Quantity = itemQtys[i],
                                ActualQuantity = 0,
                                CreatedDate = DateTime.Now
                            };
                            deliverySchedule.DeliveryItems.Add(dItem);
                            _context.DeliveryItems.Add(dItem);
                        }
                    }

                    // Update total target qty from sum of items
                    deliverySchedule.TotalTargetQuantity = (int)deliverySchedule.DeliveryItems.Sum(di => di.Quantity);

                    await _context.SaveChangesAsync();

                    // Auto-sync pending preparations ke jadwal yang baru dibuat
                    int createSynced = await _syncService.SyncAllPendingAsync();
                    if (createSynced > 0)
                    {
                        TempData["InfoMessage"] = $"{createSynced} record preparation PENDING telah tersinkronisasi ke jadwal baru.";
                    }

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        action = "create",
                        message = $"New schedule {deliverySchedule.ScheduleNumber} created manually.",
                        timestamp = DateTime.Now
                    });

                    return RedirectToAction(nameof(Index));
                }
            }
            
            // On failure, reload view data
            ViewData["CustomerId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Customers, "CustomerId", "CustomerName", deliverySchedule.CustomerId);
            var itemList = await _context.Items.OrderBy(i => i.ItemName)
                .Select(i => new {
                    ItemId = i.ItemId,
                    DisplayName = $"{i.ItemName} | Rack: {i.Rack}-{i.NoRack} | VIN: {i.VIN}"
                }).ToListAsync();
            ViewData["Items"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(itemList, "ItemId", "DisplayName");

            return View(deliverySchedule);
        }

        // POST: PreparationSchedule/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id, int pageNumber = 1, DateTime? filterDate = null)
        {
            var deliverySchedule = await _context.DeliverySchedules.FindAsync(id);
            if (deliverySchedule != null)
            {
                _context.DeliverySchedules.Remove(deliverySchedule);
                await _context.SaveChangesAsync();

                // Notify Dashboard via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "delete",
                    message = $"Preparation schedule {deliverySchedule.ScheduleNumber} deleted.",
                    timestamp = DateTime.Now
                });
            }
            return RedirectToAction(nameof(Index), new { pageNumber = pageNumber, filterDate = filterDate });
        }

        // POST: PreparationSchedule/DeleteDeliveryItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDeliveryItem(int id, int pageNumber = 1, DateTime? filterDate = null)
        {
            var deliveryItem = await _context.DeliveryItems.FindAsync(id);
            if (deliveryItem != null)
            {
                var scheduleId = deliveryItem.ScheduleId;
                _context.DeliveryItems.Remove(deliveryItem);
                await _context.SaveChangesAsync();

                // Check if the schedule still has items; if not, remove the schedule too
                var anyItemsLeft = await _context.DeliveryItems.AnyAsync(di => di.ScheduleId == scheduleId);
                if (!anyItemsLeft)
                {
                    var parentSchedule = await _context.DeliverySchedules.FindAsync(scheduleId);
                    if (parentSchedule != null)
                    {
                        _context.DeliverySchedules.Remove(parentSchedule);
                        await _context.SaveChangesAsync();
                    }
                }

                // Notify via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "deleteitem",
                    message = $"Delivery item ID {id} deleted.",
                    timestamp = DateTime.Now
                });
            }
            return RedirectToAction(nameof(Index), new { pageNumber = pageNumber, filterDate = filterDate });
        }

        // POST: PreparationSchedule/BulkDeleteSelected
        // Diterima dari JS bulk-delete. Payload: { ids: [1,2,3] }
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> BulkDeleteSelected([FromBody] BulkDeleteRequest? request)
        {
            if (request == null || request.ids == null || request.ids.Length == 0)
                return Json(new { success = false, message = "Tidak ada item yang dipilih." });

            try
            {
                var ids = request.ids.Distinct().ToArray();
                var items = await _context.DeliveryItems.Where(d => ids.Contains(d.DeliveryItemId)).ToListAsync();
                if (items == null || items.Count == 0)
                    return Json(new { success = false, message = "Tidak ditemukan item untuk dihapus." });

                // Track schedules touched so we can remove orphan schedules if needed
                var scheduleIds = items.Select(i => i.ScheduleId).Distinct().ToList();

                _context.DeliveryItems.RemoveRange(items);
                await _context.SaveChangesAsync();

                // Optionally remove schedules that now have zero items
                var orphans = await _context.DeliverySchedules
                    .Where(s => scheduleIds.Contains(s.ScheduleId) && !s.DeliveryItems.Any())
                    .ToListAsync();
                if (orphans.Any())
                {
                    _context.DeliverySchedules.RemoveRange(orphans);
                    await _context.SaveChangesAsync();
                }

                // Notify via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "deleteitem",
                    message = $"{items.Count} item(s) deleted via bulk.",
                    timestamp = DateTime.Now
                });

                return Json(new { success = true, message = $"{items.Count} item berhasil dihapus." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BulkDeleteSelected error");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Admin only: Hapus semua Ghost Schedules (schedule tanpa DeliveryItems sama sekali).
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> CleanGhostSchedules()
        {
            var roleStr = HttpContext.Session.GetString("Role");
            if (roleStr != "Admin" && roleStr != "Super Admin")
                return Json(new { success = false, message = "Hanya Admin yang bisa menjalankan cleanup." });

            try
            {
                var ghosts = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => !s.DeliveryItems.Any())
                    .ToListAsync();

                int count = ghosts.Count;
                if (count == 0)
                    return Json(new { success = true, message = "Tidak ada Ghost Schedule ditemukan." });

                _context.DeliverySchedules.RemoveRange(ghosts);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"✅ Berhasil menghapus {count} Ghost Schedule." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Admin only: Bulk delete ghost schedules berdasarkan ScheduleId (dipanggil dari bulk-select UI).
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> BulkDeleteGhostSchedules([FromBody] BulkDeleteRequest? request)
        {
            if (request == null || request.ids == null || request.ids.Length == 0)
                return Json(new { success = false, message = "Tidak ada schedule yang dipilih." });

            var roleStr = HttpContext.Session.GetString("Role");
            if (roleStr != "Admin" && roleStr != "Super Admin")
                return Json(new { success = false, message = "Hanya Admin yang bisa menghapus." });

            try
            {
                var ids = request.ids.Distinct().ToArray();
                // Hanya hapus schedule yang benar-benar tidak punya DeliveryItems (safety guard)
                var ghosts = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => ids.Contains(s.ScheduleId) && !s.DeliveryItems.Any())
                    .ToListAsync();

                if (!ghosts.Any())
                    return Json(new { success = true, message = "Tidak ada Ghost Schedule valid untuk dihapus." });

                _context.DeliverySchedules.RemoveRange(ghosts);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"{ghosts.Count} Ghost Schedule berhasil dihapus." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Admin only: Recalculate ActualQuantity dari scratch berdasarkan jumlah PreparationRecords yang LINKED.
        /// Digunakan untuk memperbaiki data lama yang over-scan (ActualQty > Quantity) akibat bug batch-save SyncService.
        /// Logic: untuk setiap DeliveryItem, hitung ulang ActualQty dari jumlah scan linked × qpc.
        ///        Jika hasil hitungan melebihi Quantity, cap ke Quantity.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RecalculateActualQty([FromBody] RecalcQtyRequest? request)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            if (roleStr != "Admin" && roleStr != "Super Admin")
                return Json(new { success = false, message = "Hanya Admin yang bisa menjalankan recalculate." });

            try
            {
                // STEP 0: Link semua pending records ke jadwal terlebih dahulu.
                // Gunakan filterDate agar hanya memproses scan relevan (bukan semua 13k records).
                // Tanpa filter ini, SyncAllPendingAsync memproses semua pending dan sangat lambat.
                DateTime? targetDate = null;
                if (!string.IsNullOrEmpty(request?.ScheduleDate) && DateTime.TryParse(request.ScheduleDate, out var pd))
                    targetDate = pd.Date;

                await _syncService.SyncAllPendingAsync(filterDate: targetDate);

                // Load DeliveryItems dengan relasi Item (untuk qpc) dan Schedule
                var itemsQuery = _context.DeliveryItems
                    .Include(di => di.Item)
                    .Include(di => di.DeliverySchedule)
                    .AsQueryable();

                if (targetDate.HasValue)
                {
                    // Rentang diperlebar ke H-3 s/d H+1 agar mencakup:
                    //   - Jadwal CARRY OVER (ScheduledDate hari-hari sebelumnya yang belum selesai)
                    //   - Jadwal H-1 (scan kemarin untuk jadwal hari ini)
                    //   - Jadwal hari ini dan besok (H+1)
                    var rangeMin = targetDate.Value.AddDays(-3);
                    var rangeMax = targetDate.Value.AddDays(1);
                    itemsQuery = itemsQuery.Where(di =>
                        di.DeliverySchedule.ScheduledDate.Date >= rangeMin &&
                        di.DeliverySchedule.ScheduledDate.Date <= rangeMax);
                }

                var deliveryItems = await itemsQuery.ToListAsync();

                // Load semua linked PreparationRecords (ScheduleId != null, Remark = "Match")
                var linkedScans = await _context.PreparationRecords
                    .Where(p => p.ScheduleId != null && p.Remark == "Match")
                    .Select(p => new { p.ScheduleId, p.Tag, p.Kanban, p.ManifestNumber, IsPending = false })
                    .ToListAsync();

                // Load juga pending records (ScheduleId = null, Remark = "Match") yang sudah punya ManifestNumber.
                // Key unik: ManifestNumber + DockCycle + DockName + Kanban
                // Jika record punya DockCycle/DockName, harus cocok dengan schedule. Jika kosong → lolos (backward compat).
                var pendingScans = await _context.PreparationRecords
                    .Where(p => p.ScheduleId == null && p.Remark == "Match" && p.ManifestNumber != null && p.ManifestNumber != "")
                    .Select(p => new { p.Tag, p.Kanban, p.ManifestNumber, p.DockCycle, p.DockName })
                    .ToListAsync();

                // Load ScheduleNumber + Cycle + Area per ScheduleId untuk validasi pending match
                var scheduleNumbers = await _context.DeliverySchedules
                    .Select(s => new { s.ScheduleId, s.ScheduleNumber, s.Cycle, s.Area })
                    .ToDictionaryAsync(s => s.ScheduleId, s => new { s.ScheduleNumber, s.Cycle, s.Area });

                // Load semua master items untuk qpc lookup
                var allItems = await _context.Items.ToListAsync();

                int fixedCount = 0;
                foreach (var di in deliveryItems)
                {
                    if (di.DeliverySchedule == null) continue;
                    int qpc = (di.Item?.QtyLot != null && di.Item.QtyLot > 0) ? di.Item.QtyLot.Value : 1;

                    // Hitung jumlah scan yang LINKED ke schedule ini untuk item ini.
                    // PENTING: kondisi ScheduleId HARUS membungkus SELURUH pengecekan tag.
                    var itemVin = VinHelper.Normalize(di.Item?.VIN ?? "");
                    var itemCode = di.Item?.ItemCode ?? "";

                    // Dapatkan ScheduleNumber+Cycle+Area untuk schedule ini (untuk match pending records)
                    scheduleNumbers.TryGetValue(di.ScheduleId, out var thisSched);
                    var thisScheduleNumUpper = (thisSched?.ScheduleNumber ?? "").Trim().ToUpper();
                    var thisCycleUpper       = (thisSched?.Cycle ?? "").Trim().ToUpper();
                    var thisAreaUpper        = (thisSched?.Area  ?? "").Trim().ToUpper();

                    // Gabungkan: linked records (ScheduleId match) + pending records (ManifestNumber match)
                    var tagMatches = (string s_tag) =>
                        (!string.IsNullOrEmpty(itemVin) && (VinHelper.Normalize(s_tag) == itemVin || VinHelper.IsMatch(s_tag, di.Item?.VIN ?? ""))) ||
                        (!string.IsNullOrEmpty(itemCode) && VinHelper.Normalize(s_tag) == VinHelper.Normalize(itemCode));

                    var matchedLinked = linkedScans
                        .Where(s => s.ScheduleId == di.ScheduleId && tagMatches(s.Tag))
                        .Select(s => new { s.Kanban })
                        .ToList();

                    // Match pending records menggunakan kunci unik: manifest + cycle + dock.
                    // Jika pending record punya DockCycle → harus cocok dengan schedule.Cycle.
                    // Jika pending record punya DockName  → harus cocok dengan schedule.Area.
                    // Field kosong/"-" → lolos (backward compatible dengan scan lama tanpa cycle/dock).
                    var matchedPending = (string.IsNullOrEmpty(thisScheduleNumUpper)
                        ? new List<string?>()
                        : pendingScans
                            .Where(s =>
                                !string.IsNullOrEmpty(s.ManifestNumber) &&
                                s.ManifestNumber.Trim().ToUpper() == thisScheduleNumUpper &&
                                tagMatches(s.Tag) &&
                                // Cycle filter: jika ada DockCycle dan tidak kosong/"-", harus cocok
                                (string.IsNullOrEmpty(s.DockCycle) || s.DockCycle == "-" ||
                                 string.IsNullOrEmpty(thisCycleUpper) ||
                                 s.DockCycle.Trim().ToUpper() == thisCycleUpper) &&
                                // Dock filter: jika ada DockName dan tidak kosong/"-", harus cocok dengan Area
                                (string.IsNullOrEmpty(s.DockName) || s.DockName == "-" ||
                                 string.IsNullOrEmpty(thisAreaUpper) ||
                                 s.DockName.Trim().ToUpper() == thisAreaUpper ||
                                 thisAreaUpper.Contains(s.DockName.Trim().ToUpper()) ||
                                 s.DockName.Trim().ToUpper().Contains(thisAreaUpper)))
                            .Select(s => s.Kanban)
                            .ToList());

                    var allMatchedKanbans = matchedLinked.Select(s => s.Kanban)
                        .Concat(matchedPending)
                        .ToList();

                    // Filter DN kanban: untuk schedule berformat DN, buang scan yang kanban-nya
                    // diawali DN BERBEDA dari schedule ini. Ini membersihkan data lama di mana kanban
                    // dari manifest lain (C1) terlanjur ter-link ke schedule ini (C2).
                    if (!string.IsNullOrEmpty(thisScheduleNumUpper) && thisScheduleNumUpper.StartsWith("DN"))
                    {
                        allMatchedKanbans = allMatchedKanbans
                            .Where(k => string.IsNullOrEmpty(k) ||
                                        !k.Trim().ToUpper().StartsWith("DN") ||
                                        k.Trim().ToUpper().StartsWith(thisScheduleNumUpper))
                            .ToList();
                    }

                    // Deduplikasi hanya untuk DN kanbans (unique per unit fisik).
                    // Non-DN kanbans (barcode = nomor part): tiap scan = 1 kanban → hitung total, bukan unique.
                    bool isScheduleDn = !string.IsNullOrEmpty(thisScheduleNumUpper) && thisScheduleNumUpper.StartsWith("DN");
                    int scanCount;
                    if (isScheduleDn)
                    {
                        // DN: unique barcode per kanban — dedup by barcode
                        var uniqueKanbans = allMatchedKanbans
                            .Where(k => string.IsNullOrEmpty(k) || k == "TWO-POINT-CHECK")
                            .Count();
                        uniqueKanbans += allMatchedKanbans
                            .Where(k => !string.IsNullOrEmpty(k) && k != "TWO-POINT-CHECK")
                            .Select(k => k!.Trim().ToUpper())
                            .Distinct()
                            .Count();
                        scanCount = uniqueKanbans;
                    }
                    else
                    {
                        // Non-DN: tiap scan = 1 kanban fisik, hitung total records
                        scanCount = allMatchedKanbans.Count;
                    }

                    decimal correctActualQty = scanCount * qpc;
                    // Cap ke Quantity agar tidak over-target
                    if (correctActualQty > di.Quantity) correctActualQty = di.Quantity;
                    // Jika scanCount = 0 maka correctActualQty = 0 — ini yang membersihkan data terkontaminasi
                    // (item yang ActualQty-nya di-set ke target padahal tidak ada scan sama sekali)

                    bool changed = false;
                    if (di.ActualQuantity != correctActualQty)
                    {
                        di.ActualQuantity = correctActualQty;
                        changed = true;
                    }
                    bool shouldBeCompleted = correctActualQty >= di.Quantity;
                    if (di.IsCompleted != shouldBeCompleted)
                    {
                        di.IsCompleted = shouldBeCompleted;
                        changed = true;
                    }
                    if (changed) fixedCount++;
                }

                if (fixedCount > 0)
                {
                    // Recalculate TotalActualQuantity di level schedule tanpa mengubah workflow/status.
                    // Fix Actual Qty harus hanya memperbaiki jumlah, bukan memaksa status ke Prepared / In Progress.
                    var affectedScheduleIds = deliveryItems.Select(di => di.ScheduleId).Distinct().ToList();
                    var schedules = await _context.DeliverySchedules
                        .Include(s => s.DeliveryItems)
                        .Where(s => affectedScheduleIds.Contains(s.ScheduleId))
                        .ToListAsync();
                    foreach (var sched in schedules)
                    {
                        sched.TotalActualQuantity = sched.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);
                    }

                    await _context.SaveChangesAsync();
                }

                return Json(new { success = true, message = $"Recalculate selesai. {fixedCount} DeliveryItem dikoreksi." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Admin only: Re-apply ItemMappings ke DeliveryItems yang sudah ada.
        /// Berguna saat mapping baru ditambahkan setelah schedule di-upload, agar QPC/kanban ikut terkoreksi.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> RemapItems([FromBody] RemapItemsRequest? request)
        {
            var roleStr = HttpContext.Session.GetString("Role");
            if (roleStr != "Admin" && roleStr != "Super Admin")
                return Json(new { success = false, message = "Hanya Admin yang bisa menjalankan remap." });

            try
            {
                // Load semua ItemMappings (PartNo eksternal → VIN internal)
                var allMappings = await _context.ItemMappings
                    .Where(m => !string.IsNullOrEmpty(m.CustomerPartNumber))
                    .ToListAsync();

                // Load semua master Items
                var allItems = await _context.Items.ToListAsync();

                // Normalisasi: hapus semua karakter non-alphanumeric, uppercase
                static string NormCode(string? s) =>
                    string.IsNullOrWhiteSpace(s) ? "" :
                    System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[^A-Z0-9]", "");

                // Helper: cari Item master terbaik berdasarkan kode eksternal
                Item? ResolveItem(string? code)
                {
                    if (string.IsNullOrWhiteSpace(code)) return null;
                    string normCode = NormCode(code);

                    // P1: ItemMappings.CustomerPartNumber → resolve ke master Item via VIN
                    var map = allMappings.FirstOrDefault(m =>
                        NormCode(m.CustomerPartNumber) == normCode ||
                        VinHelper.IsMatch(m.CustomerPartNumber, code));
                    if (map != null && !string.IsNullOrWhiteSpace(map.VIN))
                    {
                        var fromMap = allItems.FirstOrDefault(i =>
                            VinHelper.IsMatch(i.VIN, map.VIN) ||
                            (!string.IsNullOrWhiteSpace(i.ItemCode) && VinHelper.IsMatch(i.ItemCode, map.VIN)));
                        if (fromMap != null) return fromMap;
                    }

                    // P2: master Items.CustomerPartNumber exact/normalized match
                    var byPartNo = allItems.FirstOrDefault(i =>
                        !string.IsNullOrWhiteSpace(i.CustomerPartNumber) &&
                        (NormCode(i.CustomerPartNumber) == normCode ||
                         VinHelper.IsMatch(i.CustomerPartNumber, code)));
                    if (byPartNo != null) return byPartNo;

                    // P3: master Items.VIN / ItemCode flexible match
                    return allItems.FirstOrDefault(i =>
                        VinHelper.IsMatch(i.VIN, code) ||
                        (!string.IsNullOrWhiteSpace(i.ItemCode) && VinHelper.IsMatch(i.ItemCode, code)));
                }

                // Query DeliveryItems, filter by date jika diberikan
                IQueryable<DeliveryItem> query = _context.DeliveryItems
                    .Include(di => di.Item)
                    .Include(di => di.DeliverySchedule);

                if (!string.IsNullOrWhiteSpace(request?.ScheduleDate) &&
                    DateTime.TryParse(request.ScheduleDate, out var filterDate))
                {
                    var filterDateOnly = filterDate.Date;
                    query = query.Where(di => di.DeliverySchedule != null &&
                                              di.DeliverySchedule.ScheduledDate.Date == filterDateOnly);
                }

                var deliveryItems = await query.ToListAsync();

                int remapped = 0, skipped = 0;

                foreach (var di in deliveryItems)
                {
                    if (di.Item == null) continue;

                    // Kumpulkan semua kode kandidat dari item yang tersimpan saat ini
                    var codesToTry = new List<string?> {
                        di.Item.CustomerPartNumber,
                        di.Item.VIN,
                        di.Item.ItemCode
                    }.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();

                    Item? resolved = null;
                    foreach (var code in codesToTry)
                    {
                        var candidate = ResolveItem(code);
                        // Hanya update jika item berbeda DAN item baru punya QtyLot (QPC) yang valid
                        if (candidate != null && candidate.ItemId != di.ItemId && (candidate.QtyLot ?? 0) > 0)
                        {
                            resolved = candidate;
                            break;
                        }
                    }

                    if (resolved != null)
                    {
                        // Simpan Part No asli sebelum remap agar tetap tampil di jadwal
                        if (string.IsNullOrWhiteSpace(di.ExternalPartNo))
                        {
                            di.ExternalPartNo = codesToTry.FirstOrDefault(c => c?.Contains('-') == true)
                                ?? codesToTry.FirstOrDefault();
                        }
                        _logger.LogInformation(
                            "RemapItems: DeliveryItemId={Id} PartNo='{Code}' oldItemId={Old} → newItemId={New} VIN='{VIN}' QtyLot={QPC}",
                            di.DeliveryItemId, codesToTry.FirstOrDefault(), di.ItemId, resolved.ItemId, resolved.VIN, resolved.QtyLot);
                        di.ItemId = resolved.ItemId;
                        di.UpdatedDate = DateTime.Now;
                        remapped++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                if (remapped > 0)
                    await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    remapped,
                    skipped,
                    message = remapped > 0
                        ? $"✅ {remapped} item berhasil di-remap ke master yang benar. {skipped} item sudah cocok."
                        : $"Semua {skipped} item sudah cocok dengan master, tidak ada yang perlu diubah."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemapItems error");
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: PreparationSchedule/BulkCreate
        public async Task<IActionResult> BulkCreate(DateTime? selectedDate)
        {
            var scheduledDate = selectedDate ?? DateTime.Today;
            
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode)
                .ToListAsync();
            
            var customerItems = customers.Select(c => new CustomerScheduleItem
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Route = c.Route,
                Cycle = c.Cycle,
                Docking = c.Docking,
                Pickup = c.Pickup,
                ETD = c.ETD,
                IsSelected = false,
                IsMatchingDay = ShouldScheduleCustomerOnDate(c, scheduledDate)
            }).ToList();
            
            var viewModel = new BulkScheduleViewModel
            {
                ScheduledDate = scheduledDate,
                AvailableCustomers = customerItems,
                IsAutoGenerate = true
            };
            
            return View(viewModel);
        }

        // POST: PreparationSchedule/BulkCreate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkCreate(BulkScheduleViewModel model)
        {
            try
            {
                var schedules = new List<DeliverySchedule>();
                List<Customer> customers;

                if (model.IsAutoGenerate)
                {
                    if (model.ScheduledDate.DayOfWeek == DayOfWeek.Saturday || model.ScheduledDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        TempData["ErrorMessage"] = "Mode otomatis hanya berlaku untuk hari Seninâ€“Jumat.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers.Where(c => c.IsActive).ToListAsync();
                    customers = customers.Where(c => ShouldScheduleCustomerOnDate(c, model.ScheduledDate)).ToList();

                    if (!customers.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang memenuhi kriteria Cycle.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }
                }
                else
                {
                    if (model.SelectedCustomerIds == null || !model.SelectedCustomerIds.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang dipilih!";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .Where(c => model.SelectedCustomerIds.Contains(c.CustomerId))
                        .ToListAsync();
                }
                
                int sequenceNumber = await GetNextSequenceInternal(model.ScheduledDate);
                
                foreach (var customer in customers)
                {
                    var prefix = $"SCH-{model.ScheduledDate:yyyyMMdd}";
                    var scheduleNumber = $"{prefix}{sequenceNumber:D3}";
                    sequenceNumber++;

                    // --- Sequential Forward Bumping Logic ---
                    // Menggunakan StartPrepareTime sebagai basis waktu pertama (H).
                    // Semua milestone berikutnya (EndPrep, DockIn, Pickup, ETD) akan di-shift ke H+1 
                    // jika jamnya lebih kecil dari milestone sebelumnya.
                    var pickupTime = ParseTimeToDateTime(customer.Pickup, model.ScheduledDate);
                    var enterDockTime = ParseTimeToDateTime(customer.Docking, model.ScheduledDate);
                    var etdTime = ParseTimeToDateTime(customer.ETD, model.ScheduledDate);

                    var baseDate = model.ScheduledDate.Date;
                    var startPrepTime = baseDate.AddMinutes(customer.StartPrepareTime);
                    var stdPMinutes = customer.StdPrepareTime > 0 ? customer.StdPrepareTime : 0;
                    var stdEndTime = baseDate.AddMinutes(stdPMinutes);
                    
                    // --- Enhanced Sequential Forward Bumping ---
                    // 1. End Prep must be >= Start Prep
                    if (stdPMinutes < customer.StartPrepareTime) stdEndTime = stdEndTime.AddDays(1);

                    // 2. Dock In: bump sekali jika sebelum startPrepTime (overnight case).
                    //    JANGAN pakai stdEndTime sebagai anchor agar tidak double-bump ketika
                    //    DockIn (04:30) dan EndPrep (04:45) hanya berselisih menit pada hari yang sama.
                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                        enterDockTime = enterDockTime.Value.AddDays(1);

                    // 3. Pickup must be >= Dock In (or End Prep if Dock In is null)
                    if (pickupTime.HasValue)
                    {
                        var anchor = enterDockTime ?? stdEndTime;
                        if (pickupTime.Value < anchor) pickupTime = pickupTime.Value.AddDays(1);
                    }

                    // 4. ETD must be >= Pickup (or previous anchor)
                    if (etdTime.HasValue)
                    {
                        var anchor = pickupTime ?? enterDockTime ?? stdEndTime;
                        if (etdTime.Value < anchor) etdTime = etdTime.Value.AddDays(1);
                    }
                    
                    var schedule = new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = customer.CustomerId,
                        ScheduledDate = model.ScheduledDate,
                        Route = customer.Route,
                        Cycle = customer.Cycle,
                        EnterDockTime = enterDockTime,
                        PickupTime = pickupTime,
                        ETD = etdTime,
                        Range = customer.Range,
                        SKID = ParseSKID(customer.SKID),
                        Area = customer.Area,
                        Status = "Scheduled", // Default status
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "PreparationPortal"
                    };
                    
                    schedules.Add(schedule);
                }
                
                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // Auto-sync pending preparations ke jadwal yang baru dibuat
                        int synced = await _syncService.SyncAllPendingAsync();
                        if (synced > 0)
                        {
                            TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal baru.";
                        }

                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "bulk_create", message = $"Created {schedules.Count} schedules", timestamp = DateTime.Now });

                        TempData["SuccessMessage"] = $"Berhasil membuat {schedules.Count} schedule!";
                        return RedirectToAction(nameof(Index));
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error: {ex.Message}";
            }
            
            return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
        }

        // Download Excel Template (Multi-Sheet per Customer)
        public async Task<IActionResult> DownloadTemplate()
        {
            // Custom sort order: ADM, TMMIN, HINO, AHM, HYUNDAI first, then rest alphabetically
            var priorityOrder = new[] { "ADM", "TMMIN", "HINO", "AHM", "HYUNDAI" };
            var allCustomers = await _context.Customers
                .Where(c => c.IsActive)
                .ToListAsync();
            
            var customers = allCustomers
                .OrderBy(c => {
                    var code = (c.CustomerCode ?? "").ToUpper();
                    for (int i = 0; i < priorityOrder.Length; i++)
                        if (code.Contains(priorityOrder[i])) return i;
                    return priorityOrder.Length; // others come last
                })
                .ThenBy(c => c.CustomerCode)
                .ThenBy(c => c.CustomerName)
                .ToList();

            using (var workbook = new XLWorkbook())
            {
                // ─── PANDUAN sheet (first) ───────────────────────────────────────
                var guideSheet = workbook.Worksheets.Add("PANDUAN");
                guideSheet.Cell(1, 1).Value = "PANDUAN TEMPLATE JADWAL PREPARATION";
                guideSheet.Cell(1, 1).Style.Font.Bold = true;
                guideSheet.Cell(1, 1).Style.Font.FontSize = 14;
                guideSheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#f59e0b");
                guideSheet.Cell(3, 1).Value = "1. Template ini berisi satu sheet untuk setiap Customer yang aktif.";
                guideSheet.Cell(4, 1).Value = "2. Isi data pada sheet Customer yang ingin di-import (boleh lebih dari satu sheet).";
                guideSheet.Cell(5, 1).Value = "3. Kolom: MANIFESTING | PART NO / VIN | QTY (PCS) — jangan ubah nama kolom header.";
                guideSheet.Cell(6, 1).Value = "4. Baris 1 (abu-abu) adalah info sistem — JANGAN dihapus atau diubah.";
                guideSheet.Cell(7, 1).Value = "5. Saat upload, pilihan Customer di dropdown bersifat opsional (override semua sheet).";
                guideSheet.Column(1).Width = 75;

                // ─── One sheet per customer ──────────────────────────────────────
                var headers = new[] { "MANIFESTING", "PART NO / VIN", "QTY (PCS)" };
                var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var c in customers)
                {
                    // Sheet name = Dock (CustomerName) + Cycle, max 31 chars (Excel limit)
                    string rawName = $"{c.CustomerName}-{c.Cycle}";
                    // Replace invalid chars: \ / ? * [ ] :
                    rawName = System.Text.RegularExpressions.Regex.Replace(rawName, @"[\\/?*\[\]:]", "-").Trim();
                    if (rawName.Length > 28) rawName = rawName.Substring(0, 28); // reserve 3 chars for dedup suffix

                    // Deduplicate: append (2), (3), ... if name already used
                    string sheetName = rawName;
                    int dupCounter = 2;
                    while (usedSheetNames.Contains(sheetName))
                    {
                        string suffix = $"({dupCounter++})";
                        sheetName = rawName.Substring(0, Math.Min(rawName.Length, 31 - suffix.Length)) + suffix;
                    }
                    usedSheetNames.Add(sheetName);

                    var ws = workbook.Worksheets.Add(sheetName);

                    // Row 1: system info (CUSTOMER_ID) — gray, small font
                    ws.Cell(1, 1).Value = "CUSTOMER_ID";
                    ws.Cell(1, 2).Value = c.CustomerId.ToString();
                    ws.Cell(1, 3).Value = c.CustomerCode ?? "";
                    var infoRange = ws.Range(1, 1, 1, 3);
                    infoRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#94a3b8");
                    infoRange.Style.Font.FontColor = XLColor.FromHtml("#1e293b");
                    infoRange.Style.Font.FontSize = 8;
                    infoRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                    // Row 2: headers
                    for (int i = 0; i < headers.Length; i++)
                        ws.Cell(2, i + 1).Value = headers[i];
                    var headerRange = ws.Range(2, 1, 2, headers.Length);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                    headerRange.Style.Font.FontColor = XLColor.White;
                    headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // Row 3-4: brief instructions (no sample data - sheet stays empty if user doesn't fill)
                    ws.Cell(3, 1).Value = "Keterangan:";
                    ws.Cell(3, 1).Style.Font.Bold = true;
                    ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
                    ws.Cell(4, 1).Value = $"Customer: {c.CustomerCode} | Dock: {c.CustomerName} | Route: {c.Route} | Cycle: {c.Cycle}";
                    ws.Cell(4, 1).Style.Font.FontColor = XLColor.Gray;
                    ws.Cell(5, 1).Value = "Hapus baris Keterangan ini, lalu isi data mulai baris 3.";
                    ws.Cell(5, 1).Style.Font.FontColor = XLColor.Gray;

                    ws.Columns().AdjustToContents();
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Jadwal_Preparation_AllCustomer.xlsx");
                }
            }
        }
        [HttpGet]
        public IActionResult ImportExcel()
        {
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> ImportExcel([FromForm] IFormFile file, [FromForm] int? customerId)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid! Silakan pilih file Excel terlebih dahulu.";
                return RedirectToAction(nameof(Index));
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToAction(nameof(Index));
            }

            int successCount = 0;
            int itemCount = 0;
            int errorCount = 0;
            var errorSamples = new List<string>();

            try
            {
                // 1. Load Customers for Lookup 
                var allCustomers = await _context.Customers.AsNoTracking().ToListAsync();
                Customer? chosenCustomer = customerId.HasValue ? allCustomers.FirstOrDefault(c => c.CustomerId == customerId.Value) : null;
                
                // Index customers for fallback Excel matching
                var customersByContext = allCustomers
                    .GroupBy(c => $"{NormalizeHeader(c.CustomerName)}|{NormalizeHeader(c.Route)}|{NormalizeHeader(c.Cycle)}")
                    .ToDictionary(g => g.Key, g => g.First());

                var customersByName = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode))
                    .GroupBy(c => NormalizeHeader(c.CustomerCode))
                    .ToDictionary(g => g.Key, g => g.First());

                                // 2. Load Items & Mappings for "Translation" 
                // v12.0: SEPARATION OF CONCERNS
                // - Mappings: Part No -> VIN
                // - Items: VIN -> Stock/Loc/QPC
                var allItems = await _context.Items.Where(i => i.IsActive).ToListAsync();
                var allItemsInactive = await _context.Items.Where(i => !i.IsActive).AsNoTracking().ToListAsync();
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                var importInactiveWarnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        // ─── PHASE 1: Collect All Valid Data Rows (multi-sheet loop) ───
                        var validRowsData = new List<ExcelRowData>();

                        foreach (var worksheet in workbook.Worksheets)
                        {
                        // Skip guide/info sheets
                        string wsName = worksheet.Name.Trim().ToUpper();
                        if (wsName == "PANDUAN" || wsName == "INFO" || wsName == "GUIDE") continue;

                        // --- Detect CUSTOMER_ID info row (multi-sheet template) ---
                        Customer? sheetCustomer = null;
                        int headerStartRow = 1;
                        if (worksheet.Cell(1, 1).GetString().Trim().ToUpper() == "CUSTOMER_ID")
                        {
                            if (int.TryParse(worksheet.Cell(1, 2).GetString().Trim(), out int sheetCustId))
                                sheetCustomer = allCustomers.FirstOrDefault(c => c.CustomerId == sheetCustId);
                            headerStartRow = 2;
                        }
                        
                        // --- Try to extract customer from sheet name (e.g., "ADM EXP-TUE" -> ADM) ---
                        if (sheetCustomer == null)
                        {
                            // Try matching sheet name prefix with customer code, name, or dock
                            string sheetNameClean = NormalizeHeader(worksheet.Name);
                            var sheetNameParts = worksheet.Name.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            foreach (var part in sheetNameParts)
                            {
                                if (string.IsNullOrWhiteSpace(part) || part.Length < 2) continue;
                                var partNorm = NormalizeHeader(part);
                                
                                // Try match by customer code (exact or prefix)
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.CustomerCode ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase) ||
                                    NormalizeHeader(c.CustomerCode ?? "").StartsWith(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by customer name (prefix)
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.CustomerName ?? "").StartsWith(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by dock/docking
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.Docking ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                                
                                // Try match by area
                                sheetCustomer = allCustomers.FirstOrDefault(c => 
                                    NormalizeHeader(c.Area ?? "").Equals(partNorm, StringComparison.OrdinalIgnoreCase));
                                if (sheetCustomer != null) break;
                            }
                        }
                        
                        // Form dropdown override takes priority over sheet CUSTOMER_ID
                        Customer? sheetEffectiveCustomer = chosenCustomer ?? sheetCustomer;

                        // --- Robust Header Detection ---
                        var headerRow = worksheet.Row(headerStartRow);
                        var targetKeywords = new[] { 
                            "MANIFESTING", "DOCK", "KODE", "CUSTOMER", "ROUTE", 
                            "CYCLE", "ITEM", "PART", "QTY", "KANBAN" 
                        };

                        for (int r = headerStartRow; r <= headerStartRow + 9; r++) // Scan up to 10 rows from header start
                        {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 20; c++)
                            {
                                var val = NormalizeHeader(GetSafeString(testRow.Cell(c)));
                                if (val.Contains("MANIFESTING") || val.Contains("DOCK") || val.Contains("CUSTOMER") || val.Contains("PART")) 
                                    currentScore++;
                            }
                            if (currentScore >= 3) { headerRow = testRow; break; }
                        }

                        // Map Headers accurately
                        var cleanedHeaders = new Dictionary<string, int>();
                        for (int col = 1; col <= worksheet.LastColumnUsed().ColumnNumber(); col++)
                        {
                            var cleaned = NormalizeHeader(GetSafeString(headerRow.Cell(col)));
                            if (!string.IsNullOrEmpty(cleaned) && !cleanedHeaders.ContainsKey(cleaned)) cleanedHeaders.Add(cleaned, col);
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords) {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanedHeaders.ContainsKey(cleanKw)) return cleanedHeaders[cleanKw];
                                var match = cleanedHeaders.Keys.FirstOrDefault(k => k.Contains(cleanKw));
                                if (match != null) return cleanedHeaders[match];
                            }
                            return -1;
                        }

                        string SafeNormalize(string? input) => NormalizeHeader(input ?? "");

                        var colMap = new {
                            Manifesting = FindCol("MANIFESTING", "MANIFEST"),
                            Dock = FindCol("DOCK"),
                            CustName = FindCol("NAMA CUSTOMER", "NAMA", "CUSTOMER"),
                            Route = FindCol("ROUTE", "RUTE"),
                            Cycle = FindCol("CYCLE", "SIKLUS"),
                            PartNo = FindCol("PART NO", "CUSTOMER PART", "PART"),
                            Vin = FindCol("VIN", "INTERNAL CODE", "INTERNAL"),
                            ItemPartNo = FindCol("PART NO", "PART NO / VIN", "ITEM", "PART", "VIN"), // Legacy/Combined
                            QtyPcs = FindCol("QTY (PCS)", "QTY PCS", "QTY"),
                            Pickup = FindCol("PICKUP", "PENJEMPUTAN"),
                            Etd = FindCol("ETD")
                        };

                        if (colMap.CustName == -1 && sheetEffectiveCustomer == null) {
                            errorCount++;
                            // Extract first word from sheet name for error message
                            var firstWord = worksheet.Name.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? worksheet.Name;
                            if (errorSamples.Count < 5) errorSamples.Add($"Sheet '{worksheet.Name}': Customer '{firstWord}' tidak ditemukan di master. Tambahkan customer atau pilih Customer di dropdown sebelum import.");
                            continue; // skip this sheet, continue to next
                        }

                        // --- Collect rows for this sheet (fill-down vars reset per sheet) ---
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()).ToList();
                        
                        // Skip sheet if no data rows
                        if (!rows.Any()) continue;
                        
                        // Skip sheet if no real data (check Part No/VIN column for non-empty values)
                        int realDataCol = colMap.ItemPartNo != -1 ? colMap.ItemPartNo : (colMap.PartNo != -1 ? colMap.PartNo : colMap.Vin);
                        bool hasRealData = rows.Any(r => {
                            if (r.IsEmpty()) return false;
                            if (realDataCol == -1) return false;
                            string val = GetSafeString(r.Cell(realDataCol)).Trim();
                            // Skip rows that look like instructions (start with "Keterangan", "Hapus", "Customer:", etc.)
                            if (string.IsNullOrEmpty(val)) return false;
                            if (val.StartsWith("Keterangan", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Hapus", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Customer:", StringComparison.OrdinalIgnoreCase)) return false;
                            if (val.StartsWith("Isi data", StringComparison.OrdinalIgnoreCase)) return false;
                            return true;
                        });
                        if (!hasRealData) continue; // skip this sheet - no user data
                        
                        string lastCustName = "";
                        string lastManifest = "";
                        string lastDock = "";
                        string lastRoute = "";
                        string lastCycle = "";

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            try
                            {
                                string custNameCell = colMap.CustName != -1 ? GetSafeString(row.Cell(colMap.CustName)).Trim() : "";
                                string manifest = colMap.Manifesting != -1 ? GetSafeString(row.Cell(colMap.Manifesting)).Trim().ToUpper() : "";
                                
                                // v13.0: Separate Part No and VIN Support
                                string excelPartNo = colMap.PartNo != -1 ? GetSafeString(row.Cell(colMap.PartNo)).Trim().ToUpper() : "";
                                string excelVin = colMap.Vin != -1 ? GetSafeString(row.Cell(colMap.Vin)).Trim().ToUpper() : "";
                                string legacyItemCode = colMap.ItemPartNo != -1 ? GetSafeString(row.Cell(colMap.ItemPartNo)).Trim().ToUpper() : "";

                                string itemCode = !string.IsNullOrEmpty(excelPartNo) ? excelPartNo : (!string.IsNullOrEmpty(excelVin) ? excelVin : legacyItemCode);
                                
                                string dockLocation = colMap.Dock != -1 ? GetSafeString(row.Cell(colMap.Dock)).Trim() : "";
                                string route = colMap.Route != -1 ? GetSafeString(row.Cell(colMap.Route)).Trim() : "";
                                string cycle = colMap.Cycle != -1 ? GetSafeString(row.Cell(colMap.Cycle)).Trim() : "";
                                int qty = colMap.QtyPcs != -1 ? GetSafeInt(row.Cell(colMap.QtyPcs)) : 0;

                                // --- Fill Down Logic ---
                                if (string.IsNullOrEmpty(custNameCell)) custNameCell = lastCustName; else lastCustName = custNameCell;
                                if (string.IsNullOrEmpty(manifest)) manifest = lastManifest; else lastManifest = manifest;
                                if (string.IsNullOrEmpty(dockLocation)) dockLocation = lastDock; else lastDock = dockLocation;
                                if (string.IsNullOrEmpty(route)) route = lastRoute; else lastRoute = route;
                                if (string.IsNullOrEmpty(cycle)) cycle = lastCycle; else lastCycle = cycle;

                                if (string.IsNullOrEmpty(itemCode)) continue; 
                                
                                // --- MASTER CUSTOMER LOOKUP ---
                                Customer? customer = sheetEffectiveCustomer; // PRIORITAS 1: form override or sheet CUSTOMER_ID
                                
                                if (customer == null) // PRIORITAS 2: Excel Content
                                {
                                    string contextKey = $"{NormalizeHeader(dockLocation)}|{NormalizeHeader(route)}|{NormalizeHeader(cycle)}";
                                    if (customersByContext.TryGetValue(contextKey, out var cByContext)) {
                                        customer = cByContext;
                                    } else if (!string.IsNullOrEmpty(custNameCell) && customersByName.TryGetValue(NormalizeHeader(custNameCell), out var cByName)) {
                                        customer = cByName;
                                    }
                                }

                                if (customer == null) {
                                    errorCount++;
                                    if (errorSamples.Count < 5) errorSamples.Add($"Baris {row.RowNumber()}: Customer tidak ditemukan (Pilih customer di dropdown atau lengkapi kolom Dock/Route/Cycle di Excel).");
                                    continue;
                                }

                            // --- EXHAUSTIVE ITEM MATCHING ---
                            string normalizedInput = SafeNormalize(itemCode);
                            Item? matchedItem = null;
                            string? finalVin = null;
                            ItemMapping? itemMap = null;

                            // PRIORITAS 1: Cari di Master Items berdasarkan CustomerPartNumber (Flexible Match for VIN-like codes)
                            matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.CustomerPartNumber, itemCode));
                            if (matchedItem != null) finalVin = matchedItem.VIN;

                            // PRIORITAS 2: Cari di ItemMappings berdasarkan CustomerPartNumber
                            if (matchedItem == null)
                            {
                                itemMap = allMappings.FirstOrDefault(m => VinHelper.IsMatch(m.CustomerPartNumber, itemCode));
                                if (itemMap != null)
                                {
                                    finalVin = itemMap.VIN;
                                    matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, finalVin));
                                }
                            }

                            // PRIORITAS 3: Cari di Master Items berdasarkan VIN (Flexible Match)
                            if (matchedItem == null)
                            {
                                matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, itemCode));
                                if (matchedItem != null) finalVin = matchedItem.VIN;
                            }

                            // PRIORITAS 4: Cari di ItemMappings berdasarkan VIN
                            if (matchedItem == null)
                            {
                                var itemMapByVin = allMappings.FirstOrDefault(m => VinHelper.IsMatch(m.VIN, itemCode));
                                if (itemMapByVin != null)
                                {
                                    finalVin = itemMapByVin.VIN;
                                    matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.VIN, finalVin));
                                    itemMap = itemMapByVin;
                                }
                            }

                            // PRIORITAS 5: Legacy/ItemCode Match
                            if (matchedItem == null)
                            {
                                matchedItem = allItems.FirstOrDefault(i => VinHelper.IsMatch(i.ItemCode, itemCode));
                                if (matchedItem != null) finalVin = matchedItem.VIN;
                            }

                            // Set targetVin for Phase 2
                            string targetVin = finalVin ?? normalizedInput;

                            if (matchedItem == null)
                            {
                                // Cek apakah item ada tapi dinonaktifkan (sebelum auto-create)
                                var inactiveMatch = allItemsInactive.FirstOrDefault(i =>
                                    VinHelper.IsMatch(i.CustomerPartNumber, itemCode) ||
                                    VinHelper.IsMatch(i.VIN, itemCode) ||
                                    VinHelper.IsMatch(i.ItemCode, itemCode));
                                if (inactiveMatch != null)
                                {
                                    importInactiveWarnings.Add(itemCode);
                                    matchedItem = inactiveMatch; // tetap pakai item (tapi tandai sebagai warning)
                                    finalVin = inactiveMatch.VIN;
                                }
                            }

                            if (matchedItem == null)
                            {
                                // v11.0: AUTO-CREATE Master Item to ensure sync (Draft Mode)
                                matchedItem = new Item
                                {
                                    ItemCode = targetVin, // Use VIN as Code
                                    VIN = targetVin,
                                    CustomerPartNumber = !string.IsNullOrEmpty(excelPartNo) ? excelPartNo : ((itemMap != null) ? itemMap.CustomerPartNumber : (targetVin != itemCode ? itemCode : null)),
                                    Customer = customer.CustomerName,
                                    ItemName = "Imported (" + itemCode + ")",
                                    Description = "Auto-created from Schedule Import",
                                    IsActive = true,
                                    CreatedDate = DateTime.Now,
                                    QtyLot = 1 // Default QPC
                                };
                                _context.Items.Add(matchedItem);
                                allItems.Add(matchedItem); 
                            }
                            else 
                            {
                                // v14.0: Aggressive Enrichment - Use the most complete Part Number available
                                string? suggestedPartNo = (itemMap != null) ? itemMap.CustomerPartNumber : 
                                                         (targetVin != itemCode && !string.IsNullOrEmpty(itemCode) ? itemCode : null);

                                if (!string.IsNullOrEmpty(suggestedPartNo))
                                {
                                    // Update jika kosong ATAU jika Part No baru lebih panjang (lebih lengkap)
                                    if (string.IsNullOrEmpty(matchedItem.CustomerPartNumber) || 
                                        suggestedPartNo.Length > matchedItem.CustomerPartNumber.Length)
                                    {
                                        matchedItem.CustomerPartNumber = suggestedPartNo;
                                        _context.Update(matchedItem);
                                    }
                                }
                            }

                                // v10.0: QTY/LOT (QPC) CONSOLIDATION
                                if (matchedItem.ItemId > 0 && (matchedItem.QtyLot == null || matchedItem.QtyLot == 0) && !string.IsNullOrEmpty(matchedItem.VIN))
                                {
                                    var referenceItem = allItems.FirstOrDefault(i => 
                                        i.ItemId != matchedItem.ItemId &&
                                        i.VIN == matchedItem.VIN && 
                                        i.QtyLot != null && 
                                        i.QtyLot > 0);
                                    
                                    if (referenceItem != null)
                                    {
                                        matchedItem.QtyLot = referenceItem.QtyLot;
                                        matchedItem.UpdatedDate = DateTime.Now;
                                        _context.Update(matchedItem);
                                    }
                                }


                                validRowsData.Add(new ExcelRowData {
                                    RowNumber = row.RowNumber(),
                                    Customer = customer,
                                    ManifestNum = manifest,
                                    ItemCode = itemCode,
                                    DockLocation = dockLocation,
                                    Route = route,
                                    Cycle = cycle,
                                    MatchedItem = matchedItem,
                                    Quantity = qty,
                                    Row = row,
                                    PickupColIndex = colMap.Pickup,
                                    EtdColIndex = colMap.Etd
                                });
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) errorSamples.Add($"Sheet '{worksheet.Name}' Baris {row.RowNumber()}: {ex.Message}");
                            }
                        } // end foreach row

                        } // end foreach worksheet

                        // --- PHASE 2: Group by ManifestNum → 1 DeliverySchedule per manifest ---
                        var scheduledDate = DateTime.Today;
                        var importSession = DateTime.Now.ToString("HHmm");

                        // Group rows by ManifestNum (empty manifest treated as separate schedules)
                        var groupedByManifest = validRowsData
                            .GroupBy(r => string.IsNullOrEmpty(r.ManifestNum) ? Guid.NewGuid().ToString() : r.ManifestNum.Trim().ToUpper())
                            .ToList();

                        // Pre-load existing manifests to check for duplicates
                        var manifestNumbers = groupedByManifest
                            .Where(g => !string.IsNullOrEmpty(g.First().ManifestNum))
                            .Select(g => g.First().ManifestNum.Trim().ToUpper())
                            .ToList();
                        
                        var existingManifests = await _context.DeliverySchedules
                            .Where(s => s.ScheduledDate.Date == scheduledDate && 
                                        manifestNumbers.Contains(s.ScheduleNumber.ToUpper()))
                            .Select(s => s.ScheduleNumber.ToUpper())
                            .Distinct()
                            .ToListAsync();
                        
                        int skippedCount = 0;
                        var skippedManifests = new List<string>();

                        int sequentialCounter = 1;
                        foreach (var manifestGroup in groupedByManifest)
                        {
                            var firstRow = manifestGroup.First();
                            var customer = firstRow.Customer;
                            var manifestNum = firstRow.ManifestNum;

                            // Gunakan manifest number langsung tanpa suffix
                            var scheduleNumber = string.IsNullOrEmpty(manifestNum)
                                ? $"SCH-{scheduledDate:yyyyMMdd}-{sequentialCounter++}"
                                : manifestNum.Trim();

                            // Skip jika manifest sudah ada di database
                            if (!string.IsNullOrEmpty(manifestNum) && existingManifests.Contains(manifestNum.Trim().ToUpper()))
                            {
                                skippedCount++;
                                if (skippedManifests.Count < 5)
                                    skippedManifests.Add(manifestNum);
                                continue;
                            }

                            var schedule = new DeliverySchedule
                            {
                                ScheduleNumber = scheduleNumber,
                                CustomerId = customer.CustomerId,
                                ScheduledDate = scheduledDate,
                                Status = "Scheduled",
                                CreatedDate = DateTime.Now,
                                CreatedBy = User.Identity?.Name ?? "ImportExcel",
                                Route = !string.IsNullOrEmpty(customer.Route) ? customer.Route : firstRow.Route,
                                Cycle = !string.IsNullOrEmpty(customer.Cycle) ? customer.Cycle : firstRow.Cycle,
                                Area = !string.IsNullOrEmpty(customer.Area) ? customer.Area : firstRow.DockLocation,
                                StartPrepareTime = customer.StartPrepareTime,
                                StdPrepareTime = customer.StdPrepareTime,
                                Range = customer.Range,
                                SKID = customer.SKID
                            };

                            var enterDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                            var pickupTime = (firstRow.PickupColIndex != -1 ? GetSafeTime(firstRow.Row.Cell(firstRow.PickupColIndex), scheduledDate) : null)
                                            ?? ParseTimeToDateTime(customer.Pickup, scheduledDate);
                            var etdTime = (firstRow.EtdColIndex != -1 ? GetSafeTime(firstRow.Row.Cell(firstRow.EtdColIndex), scheduledDate) : null)
                                          ?? ParseTimeToDateTime(customer.ETD, scheduledDate);

                            var startPrepTime = scheduledDate.Date.AddMinutes(customer.StartPrepareTime);
                            var stdPMinutes = customer.StdPrepareTime;
                            var stdEndTime = scheduledDate.Date.AddMinutes(stdPMinutes);

                            // --- Enhanced Sequential Forward Bumping (Overnight Support) ---
                            // 1. Target Selesai (End Prep) harus >= Start Prep
                            if (stdPMinutes < customer.StartPrepareTime) stdEndTime = stdEndTime.AddDays(1);

                            // 2. Dock In: bump sekali jika sebelum startPrepTime (overnight case).
                            //    JANGAN pakai stdEndTime agar tidak double-bump ketika DockIn dan
                            //    EndPrep berselisih kecil pada hari yang sama (mis: 04:30 vs 04:45).
                            if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                                enterDockTime = enterDockTime.Value.AddDays(1);

                            // 3. Pickup harus >= Dock In (atau Target Selesai)
                            if (pickupTime.HasValue)
                            {
                                var anchor = enterDockTime ?? stdEndTime;
                                if (pickupTime.Value < anchor) pickupTime = pickupTime.Value.AddDays(1);
                            }

                            // 4. ETD harus >= Pickup (atau anchor sebelumnya)
                            if (etdTime.HasValue)
                            {
                                var anchor = pickupTime ?? enterDockTime ?? stdEndTime;
                                if (etdTime.Value < anchor) etdTime = etdTime.Value.AddDays(1);
                            }

                            schedule.EnterDockTime = enterDockTime;
                            schedule.PickupTime = pickupTime;
                            schedule.ETD = etdTime;

                            // Tambahkan semua item dari manifest yang sama
                            decimal totalQty = 0;
                            foreach (var rowData in manifestGroup)
                            {
                                var deliveryItem = new DeliveryItem
                                {
                                    Quantity = rowData.Quantity,
                                    ActualQuantity = 0,
                                    ExternalPartNo = rowData.ItemCode,  // simpan Part No asli dari manifest
                                    CreatedDate = DateTime.Now
                                };
                                if (rowData.MatchedItem.ItemId == 0)
                                    deliveryItem.Item = rowData.MatchedItem;
                                else
                                    deliveryItem.ItemId = rowData.MatchedItem.ItemId;

                                schedule.DeliveryItems.Add(deliveryItem);
                                totalQty += rowData.Quantity;
                                itemCount++;
                            }

                            schedule.TotalTargetQuantity = totalQty;
                            // Guard: jangan simpan schedule kosong (ghost schedule)
                            if (!schedule.DeliveryItems.Any())
                            {
                                skippedCount++;
                                errorSamples.Add($"Manifest {scheduleNumber}: di-skip karena tidak ada item valid.");
                                continue;
                            }
                            _context.DeliverySchedules.Add(schedule);
                            successCount++;
                        }

                        // SAVE EVERYTHING
                        await _context.SaveChangesAsync();

                        // Hitung manifest dengan multiple items (grouped)
                        int groupedManifests = groupedByManifest.Count(g => g.Count() > 1);
                        int groupedItems = groupedByManifest.Where(g => g.Count() > 1).Sum(g => g.Count());
                        
                        // Build success message dengan info grouping
                        var successMsg = $"Berhasil import {successCount} schedule ({itemCount} item baris) dari Excel!";
                        if (groupedManifests > 0)
                        {
                            successMsg += $" ({groupedManifests} manifest ter-group dengan {groupedItems} item)";
                        }
                        TempData["SuccessMessage"] = successMsg;

                        // Auto-sync pending preparations ke jadwal yang baru di-import
                        int synced = await _syncService.SyncAllPendingAsync();
                        if (synced > 0)
                        {
                            TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal yang baru di-import.";
                        }

                        // Notifikasi untuk manifest yang di-skip karena duplikat
                        if (skippedCount > 0)
                        {
                            var skippedList = string.Join(", ", skippedManifests.Take(5));
                            if (skippedCount > 5) skippedList += $" (+{skippedCount - 5} lagi)";
                            TempData["WarningMessage"] = $"{skippedCount} manifest di-skip karena sudah ada di database: {skippedList}";
                        }

                        // Notifikasi untuk item yang dinonaktifkan di Master Data
                        if (importInactiveWarnings.Any())
                        {
                            var inactiveSample = string.Join(", ", importInactiveWarnings.Take(5));
                            if (importInactiveWarnings.Count > 5) inactiveSample += $" (+{importInactiveWarnings.Count - 5} lagi)";
                            TempData["InactiveItemWarning"] = $"⚠️ {importInactiveWarnings.Count} item dinonaktifkan di Master Data: {inactiveSample}";
                        }

                        // Notify Dashboard via SignalR
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                        {
                            action = "import",
                            message = $"Berhasil import {successCount} schedule preparation dari Excel.",
                            timestamp = DateTime.Now
                        });
                    }
                }

                if (errorCount > 0)
                {
                     TempData["ErrorMessage"] = $"{errorCount} baris gagal. Contoh: {string.Join(", ", errorSamples)}";
                }
            }
            catch (Exception ex)
            {
                 TempData["ErrorMessage"] = $"Fatal Error: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // --- Helpers ---

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return System.Text.RegularExpressions.Regex.Replace(header, @"[^A-Z0-9]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ToUpper();
        }

        private string GetSafeString(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "";
            try { return cell.Value.ToString().Trim(); } catch { return ""; }
        }

        private DateTime? GetSafeTime(IXLCell cell, DateTime baseDate)
        {
             if (cell == null || cell.IsEmpty()) return null;
             string raw = cell.Value.ToString();
             
             // Try parsing Time (HH:mm)
             if (TimeSpan.TryParse(raw, out TimeSpan ts)) return baseDate.Date.Add(ts);
             if (DateTime.TryParse(raw, out DateTime dt)) return baseDate.Date.Add(dt.TimeOfDay);

             return null;
        }

        private int GetSafeInt(IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return 0;
            string raw = cell.Value.ToString();
             // Regex Extraction
            var match = System.Text.RegularExpressions.Regex.Match(raw, @"[0-9]+");
            if (match.Success && int.TryParse(match.Value, out int val)) return val;
            return 0;
        }

        // Keep existing Helpers
        private bool ShouldScheduleCustomerOnDate(Customer customer, DateTime date)
        {
             if (string.IsNullOrWhiteSpace(customer.Cycle)) return true;
             var day = date.ToString("dddd", new System.Globalization.CultureInfo("id-ID")); // Senin, etc
             return customer.Cycle.Contains(day, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<int> GetNextSequenceInternal(DateTime date)
        {
            var prefix = $"SCH-{date:yyyyMMdd}";
            var lastSchedule = await _context.DeliverySchedules
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();

            if (lastSchedule == null) return 1;

            var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
            if (int.TryParse(lastSequence, out int lastNum))
            {
                return lastNum + 1;
            }
            return 1;
        }

        private DateTime? ParseTimeToDateTime(string? timeString, DateTime baseDate)
        {
            if (string.IsNullOrWhiteSpace(timeString)) return null;
            if (TimeSpan.TryParse(timeString, out TimeSpan time)) return baseDate.Date.Add(time);
            return null;
        }

        private string? ParseSKID(string? skid) => string.IsNullOrWhiteSpace(skid) ? null : skid.Trim();

        [HttpGet]
        public async Task<IActionResult> GetNextScheduleNumber(DateTime date)
        {
            var seq = await GetNextSequenceInternal(date);
            var number = $"SCH-{date:yyyyMMdd}{seq:D3}";
            return Json(new { number = number });
        }

        #region === Smart Import Endpoints ===

        /// <summary>
        /// Step 1: Preview — upload file, auto-detect customer, extract items
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SmartImportPreview(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, errorMessage = "File tidak valid." });

            if (file.Length > 10 * 1024 * 1024) // 10MB limit
                return Json(new { success = false, errorMessage = "Ukuran file melebihi 10MB." });

            var result = await _smartImportService.ProcessFileAsync(file);

            // Get all customers for manual selection dropdown (grouped by dock)
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode).ThenBy(c => c.CustomerName)
                .Select(c => new { c.CustomerId, c.CustomerCode, c.CustomerName, c.Route, c.Cycle, c.Docking, c.Area })
                .ToListAsync();

            // VIN lookup: translate each extracted Part No → VIN via ItemMappings
            // Priority: ItemMappings(customer-filtered) → ItemMappings(all) → Items.CustomerPartNumber → Items.VIN/ItemCode
            var allMappingsPreview = await _context.ItemMappings.AsNoTracking().ToListAsync();
            var allItemsPreview    = await _context.Items.Where(i => i.IsActive).AsNoTracking()
                .Select(i => new { i.VIN, i.CustomerPartNumber, i.ItemCode, i.QtyLot }).ToListAsync();
            var allItemsInactivePreview = await _context.Items.Where(i => !i.IsActive).AsNoTracking()
                .Select(i => new { i.VIN, i.CustomerPartNumber, i.ItemCode, i.QtyLot }).ToListAsync();
            // Detected customer code used to prioritize customer-specific mappings
            var detectedCustCode = result.DetectedCustomerCode ?? "";

            // Normalize Part No for flexible matching: remove dashes/spaces, uppercase
            // e.g. "16261-0Y030-00" → "162610Y03000", "16261 0Y030 00" → "162610Y03000"
            static string NormalizePartNo(string? s) =>
                string.IsNullOrWhiteSpace(s) ? "" :
                System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");

            // Normalize VIN-like strings for comparison using VinHelper + remove dashes/spaces
            static string NormalizeVinForCompare(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                var cleaned = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");
                return DeliveryControl.Helpers.VinHelper.Normalize(cleaned);
            }

            string? ResolveVin(string partNo)
            {
                if (string.IsNullOrWhiteSpace(partNo)) return null;

                string pNorm = NormalizePartNo(partNo);   // e.g. "162610Y03000"
                string pUpper = partNo.Trim().ToUpper();  // e.g. "16261-0Y030-00"
                string pVinNorm = NormalizeVinForCompare(partNo);

                // P1: ItemMappings → CustomerPartNumber
                //   Try customer-specific mapping first, then fall back to any mapping
                bool MappingPartNoMatches(string? cpn) =>
                    !string.IsNullOrWhiteSpace(cpn) &&
                    (cpn.Trim().ToUpper() == pUpper || NormalizePartNo(cpn) == pNorm);
                var map = (!string.IsNullOrEmpty(detectedCustCode)
                    ? allMappingsPreview.FirstOrDefault(m =>
                        MappingPartNoMatches(m.CustomerPartNumber) &&
                        !string.IsNullOrWhiteSpace(m.Customer) &&
                        m.Customer.Contains(detectedCustCode, StringComparison.OrdinalIgnoreCase))
                    : null)
                    ?? allMappingsPreview.FirstOrDefault(m => MappingPartNoMatches(m.CustomerPartNumber));
                if (map != null && !string.IsNullOrWhiteSpace(map.VIN))
                {
                    // Prefer master Item VIN if exists (map.VIN might include LB suffix while Items store without)
                    var mappedItem = allItemsPreview.FirstOrDefault(i =>
                        !string.IsNullOrWhiteSpace(i.VIN) && NormalizeVinForCompare(i.VIN) == NormalizeVinForCompare(map.VIN));
                    return mappedItem != null && !string.IsNullOrWhiteSpace(mappedItem.VIN) ? mappedItem.VIN : map.VIN;
                }

                // P2: Items → CustomerPartNumber  (exact, then normalized)
                var item = allItemsPreview.FirstOrDefault(i =>
                    !string.IsNullOrWhiteSpace(i.CustomerPartNumber) && (
                        i.CustomerPartNumber.Trim().ToUpper() == pUpper ||
                        NormalizePartNo(i.CustomerPartNumber) == pNorm));
                if (item != null && !string.IsNullOrWhiteSpace(item.VIN))
                    return item.VIN;

                // P3: Items → VIN / ItemCode  (exact, then normalized or VIN-normalized)
                var item2 = allItemsPreview.FirstOrDefault(i =>
                    (!string.IsNullOrWhiteSpace(i.VIN) && (
                        i.VIN.Trim().ToUpper() == pUpper || NormalizePartNo(i.VIN) == pNorm || NormalizeVinForCompare(i.VIN) == pVinNorm)) ||
                    (!string.IsNullOrWhiteSpace(i.ItemCode) && (
                        i.ItemCode.Trim().ToUpper() == pUpper || NormalizePartNo(i.ItemCode) == pNorm)));
                if (item2 != null && !string.IsNullOrWhiteSpace(item2.VIN))
                    return item2.VIN;

                return null;
            }

            // Helper: cek apakah Part No atau resolved VIN hanya ada di item nonaktif (tidak ada yang aktif)
            bool IsInactiveVin(string? vin)
            {
                if (string.IsNullOrWhiteSpace(vin)) return false;
                string vNorm = NormalizeVinForCompare(vin);
                bool hasActive = allItemsPreview.Any(x => !string.IsNullOrWhiteSpace(x.VIN) && NormalizeVinForCompare(x.VIN) == vNorm);
                if (hasActive) return false;
                return allItemsInactivePreview.Any(x => !string.IsNullOrWhiteSpace(x.VIN) && NormalizeVinForCompare(x.VIN) == vNorm);
            }

            // Cek manifest yang sudah ada di DB untuk tanggal yang sama agar user bisa tahu sebelum confirm
            var previewManifestNums = result.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Manifesting))
                .Select(i => i.Manifesting!.Trim().ToUpper())
                .Distinct()
                .ToList();
            var itemDates = result.Items
                .Where(i => i.ScheduledDate.HasValue)
                .Select(i => i.ScheduledDate!.Value.Date)
                .Distinct()
                .ToList();
            if (!itemDates.Any() && result.DetectedScheduledDate.HasValue)
                itemDates.Add(result.DetectedScheduledDate.Value.Date);
            if (!itemDates.Any())
                itemDates.Add(DateTime.Today);

            var minDate = itemDates.Min();
            var maxDate = itemDates.Max().AddDays(1);

            var existingManifestNums = previewManifestNums.Count > 0
                ? await _context.DeliverySchedules
                    .Where(s => previewManifestNums.Contains(s.ScheduleNumber.ToUpper())
                                && s.ScheduledDate >= minDate
                                && s.ScheduledDate < maxDate)
                    .Select(s => s.ScheduleNumber.ToUpper() + "||" + (s.CustomerId ?? 0).ToString())
                    .ToListAsync()
                : new List<string>();

            return Json(new
            {
                success = result.Success,
                errorMessage = result.ErrorMessage,
                detectedCustomer = result.DetectedCustomerName,
                detectedCustomerCode = result.DetectedCustomerCode,
                customerId = result.CustomerId,
                detectedDock = result.DetectedDock,
                detectedManifest = result.DetectedManifest,
                detectedRoute = result.DetectedRoute,
                detectedCycle = result.DetectedCycle,
                detectedScheduledDate = result.DetectedScheduledDate.HasValue
                    ? result.DetectedScheduledDate.Value.ToString("yyyy-MM-dd")
                    : (string?)null,
                fileFormat = result.FileFormat,
                parserUsed = result.ParserUsed,
                isTmminMultiDock = result.IsTmminMultiDock,
                items = result.Items.Select(i => {
                    var resolvedVin = ResolveVin(i.PartNo);
                    
                    // Look up item in allItemsPreview/allItemsInactivePreview to get QtyLot
                    var pNorm = string.IsNullOrWhiteSpace(i.PartNo) ? "" : System.Text.RegularExpressions.Regex.Replace(i.PartNo.Trim().ToUpper(), @"[-\s]", "");
                    var pUpper = i.PartNo.Trim().ToUpper();
                    var dbItem = allItemsPreview.FirstOrDefault(x => 
                        (!string.IsNullOrEmpty(resolvedVin) && x.VIN == resolvedVin) ||
                        x.CustomerPartNumber == i.PartNo ||
                        (!string.IsNullOrEmpty(x.CustomerPartNumber) && System.Text.RegularExpressions.Regex.Replace(x.CustomerPartNumber.ToUpper(), @"[-\s]", "") == pNorm) ||
                        x.VIN == pUpper ||
                        x.ItemCode == pUpper
                    ) ?? allItemsInactivePreview.FirstOrDefault(x =>
                        (!string.IsNullOrEmpty(resolvedVin) && x.VIN == resolvedVin) ||
                        x.CustomerPartNumber == i.PartNo ||
                        (!string.IsNullOrEmpty(x.CustomerPartNumber) && System.Text.RegularExpressions.Regex.Replace(x.CustomerPartNumber.ToUpper(), @"[-\s]", "") == pNorm) ||
                        x.VIN == pUpper ||
                        x.ItemCode == pUpper
                    );

                    // TMMIN FIX: Use the perfectly calculated TrueSnp (QtyLot) from the parser if available,
                    // otherwise fallback to the current database QtyLot.
                    var qtyLot = i.QtyLot ?? dbItem?.QtyLot ?? 1;
                    if (qtyLot <= 0) qtyLot = 1;

                    return new
                    {
                        manifesting = i.Manifesting,
                        partNo = i.PartNo,
                        partName = i.PartName,
                        qty = i.Qty,
                        excelKanban = i.ExcelKanban,
                        qtyLot = qtyLot,
                        scheduledDate = i.ScheduledDate.HasValue
                            ? i.ScheduledDate.Value.ToString("yyyy-MM-dd")
                            : (string?)null,
                        vin = resolvedVin,
                        isInactive = IsInactiveVin(resolvedVin),
                        matchedCustomerId = i.MatchedCustomerId,
                        matchedDockName   = i.MatchedDockName,
                        matchedRoute      = i.MatchedRoute,
                        matchedCycle      = i.MatchedCycle
                    };
                }),
                warnings = result.Warnings,
                // All matching docks for detected customer
                matchedDocks = result.MatchedDocks.Select(d => new
                {
                    customerId = d.CustomerId,
                    customerCode = d.CustomerCode,
                    dockName = d.DockName,
                    route = d.Route,
                    cycle = d.Cycle,
                    docking = d.Docking,
                    area = d.Area
                }),
                // All customers for full manual selection
                customerList = customers,
                // Manifest yang sudah ada di DB (untuk warning di preview)
                existingManifests = existingManifestNums
            });
        }

        /// <summary>
        /// Preview upload file format baru ADM KEP: Date | DN | Part No | QTY Order (PCS)
        /// Digunakan oleh modal "ADM KEP Format Baru" — confirm step memakai SmartImportConfirm.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> PreviewAdmKepNew(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, errorMessage = "File tidak valid." });

            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".xlsx" && ext != ".xls")
                return Json(new { success = false, errorMessage = "Format file harus Excel (.xlsx atau .xls)." });

            if (file.Length > 10 * 1024 * 1024)
                return Json(new { success = false, errorMessage = "Ukuran file melebihi 10MB." });

            try
            {
                var result = await _smartImportService.ParseAdmKepExcelAsync(file);

                if (!result.Success)
                    return Json(new { success = false, errorMessage = result.ErrorMessage });

                // VIN lookup (same as SmartImportPreview)
                var allItems = await _context.Items.AsNoTracking().ToListAsync();
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                foreach (var item in result.Items)
                {
                    if (string.IsNullOrEmpty(item.PartNo)) continue;
                    var map = allMappings.FirstOrDefault(m =>
                        VinHelper.IsMatch(m.CustomerPartNumber, item.PartNo));
                    if (map != null) { item.PartNo = item.PartNo; /* keep original */ }
                    var masterItem = allItems.FirstOrDefault(i =>
                        VinHelper.IsMatch(i.CustomerPartNumber, item.PartNo) ||
                        VinHelper.IsMatch(i.VIN, item.PartNo));
                    // Store resolved VIN in PartName field for pass-through (frontend will read vin from separate resolve)
                }

                return Json(new
                {
                    success = true,
                    detectedCustomerName = result.DetectedCustomerName,
                    detectedManifest = result.DetectedManifest,
                    detectedScheduledDate = result.DetectedScheduledDate?.ToString("yyyy-MM-dd"),
                    parserUsed = result.ParserUsed,
                    items = result.Items.Select(i =>
                    {
                        var resolvedVin = "";
                        int qtyLot = 1;
                        if (!string.IsNullOrEmpty(i.PartNo))
                        {
                            var map2 = allMappings.FirstOrDefault(m => VinHelper.IsMatch(m.CustomerPartNumber, i.PartNo));
                            if (map2 != null) resolvedVin = map2.VIN ?? "";
                            var mi = allItems.FirstOrDefault(a =>
                                VinHelper.IsMatch(a.CustomerPartNumber, i.PartNo) ||
                                VinHelper.IsMatch(a.VIN, i.PartNo));
                            if (mi != null)
                            {
                                if (string.IsNullOrEmpty(resolvedVin)) resolvedVin = mi.VIN ?? "";
                                qtyLot = mi.QtyLot ?? 1;
                            }
                        }
                        if (qtyLot <= 0) qtyLot = 1;
                        return new
                        {
                            manifesting = i.Manifesting,
                            partNo = i.PartNo,
                            qty = i.Qty,
                            excelKanban = i.ExcelKanban,
                            qtyLot = qtyLot,
                            scheduledDate = i.ScheduledDate?.ToString("yyyy-MM-dd"),
                            vin = resolvedVin,
                            matchedCustomerId = i.MatchedCustomerId,
                            matchedDockName = i.MatchedDockName,
                            matchedRoute = i.MatchedRoute,
                            matchedCycle = i.MatchedCycle
                        };
                    }),
                    matchedDocks = result.MatchedDocks.Select(d => new
                    {
                        customerId = d.CustomerId,
                        customerCode = d.CustomerCode,
                        dockName = d.DockName,
                        route = d.Route,
                        cycle = d.Cycle
                    }),
                    isTmminMultiDock = result.IsTmminMultiDock,
                    warnings = result.Warnings
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PreviewAdmKepNew error");
                return Json(new { success = false, errorMessage = $"Error: {ex.Message}" });
            }
        }

        /// <summary>
        /// Step 2: Confirm — save extracted items as DeliverySchedule + DeliveryItems
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SmartImportConfirm([FromBody] SmartImportConfirmRequest request)
        {
            try {
                var jsonDebug = System.Text.Json.JsonSerializer.Serialize(request);
                System.IO.File.AppendAllText("SmartImportDebug.log", "\n\n[" + DateTime.Now + "]\n" + jsonDebug);
            } catch {}

            if (request == null || request.Items == null || !request.Items.Any())
                return Json(new { success = false, message = "Tidak ada item untuk di-import." });

            _logger.LogWarning("DEBUG_IMPORT_CONFIRM: PerItemDock={PerItemDock}, ItemsCount={Count}, FirstItemCustId={FirstCustId}",
                request.PerItemDock, request.Items.Count, request.Items.FirstOrDefault()?.ItemCustomerId);


            try
            {
                _logger.LogInformation("SmartImportConfirm: CustomerId={CustId}, Items={Count}, DetectedFileDate={FileDate}",
                    request.CustomerId, request.Items.Count, request.DetectedScheduledDate ?? "(none)");

                // 1. Load Customer by the SELECTED dock (customerId from grid)
                var customer = await _context.Customers.FirstOrDefaultAsync(c => c.CustomerId == request.CustomerId);
                if (customer == null)
                    return Json(new { success = false, message = "Customer tidak ditemukan di database." });

                _logger.LogInformation("SmartImportConfirm: Using Customer '{Code}' Dock='{Name}' (ID={Id})",
                    customer.CustomerCode, customer.CustomerName, customer.CustomerId);

                // 2. Load Items & Mappings for matching
                // allDbItems = master items from DB only (never mix with auto-created)
                var allDbItems = await _context.Items.Where(i => i.IsActive).ToListAsync();
                var allDbItemsInactive = await _context.Items.Where(i => !i.IsActive).AsNoTracking().ToListAsync();
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                // Customer code for customer-prioritized mapping lookup
                var confirmCustCode = customer.CustomerCode ?? "";
                // newItems = auto-created items this session (so we can reuse them across manifests)
                var newItems = new List<Item>();
                // Track item dinonaktifkan di master
                var inactiveItemWarnings = new List<string>();

                // ScheduledDate: gunakan tanggal dari file jika terdeteksi (tanggal delivery),
                // bukan selalu hari ini. Milestones (EnterDockTime, PickupTime, ETD) akan di-shift
                // relatif terhadap tanggal jadwal tsb via ApplyDayShift.
                // Fallback: DateTime.Today jika file tidak mengandung tanggal.
                DateTime? globalDetectedDate = null;
                if (!string.IsNullOrWhiteSpace(request.DetectedScheduledDate) &&
                    DateTime.TryParse(request.DetectedScheduledDate,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime parsedGlobal))
                {
                    globalDetectedDate = parsedGlobal.Date;
                }

                int successCount = 0;
                int itemCount = 0;
                int skippedCount = 0;

                // 3. Group items by manifest number + dock + delivery date (agar manifest dengan tanggal berbeda TIDAK PERNAH digabung)
                var groupedByManifestDock = request.Items
                    .GroupBy(i => {
                        var manifest = string.IsNullOrWhiteSpace(i.Manifesting) ? Guid.NewGuid().ToString() : i.Manifesting.Trim().ToUpper();
                        var dockKey = (request.PerItemDock && i.ItemCustomerId > 0) ? i.ItemCustomerId.ToString() : "0";
                        var dateKey = !string.IsNullOrWhiteSpace(i.ScheduledDate) ? i.ScheduledDate.Trim() : "";
                        return manifest + "||" + dockKey + "||" + dateKey;
                    })
                    .ToList();

                // Check for existing manifests (per manifest+dock+date combo)
                var manifestNumbers = groupedByManifestDock
                    .Where(g => !string.IsNullOrEmpty(g.First().Manifesting))
                    .Select(g => {
                        var m = g.First().Manifesting!.Trim().ToUpper();
                        var dockKey = (request.PerItemDock && g.First().ItemCustomerId > 0) ? g.First().ItemCustomerId.ToString() : "0";
                        var dateKey = !string.IsNullOrWhiteSpace(g.First().ScheduledDate) ? g.First().ScheduledDate.Trim() : "";
                        return m + "||" + dockKey + "||" + dateKey;
                    })
                    .Distinct()
                    .ToList();

                // Cek duplikat: manifest+customerId+scheduledDate sudah ada
                var manifestNums = manifestNumbers.Select(k => k.Split("||")[0]).Distinct().ToList();
                var existingSchedules = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                    .Where(s => manifestNums.Contains(s.ScheduleNumber.ToUpper()))
                    .ToListAsync();
                var existingManifests = existingSchedules
                    .Select(s => s.ScheduleNumber.ToUpper() + "||" + s.CustomerId.ToString() + "||" + s.ScheduledDate.ToString("yyyy-MM-dd"))
                    .ToHashSet();

                // Pre-load semua customer yang dipakai (untuk multi-dock: berbagai CustomerId)
                var allNeededCustomerIds = request.Items
                    .Where(i => i.ItemCustomerId > 0)
                    .Select(i => i.ItemCustomerId)
                    .Distinct()
                    .ToList();
                if (!allNeededCustomerIds.Contains(customer.CustomerId))
                    allNeededCustomerIds.Add(customer.CustomerId);
                var customerCache = await _context.Customers
                    .Where(c => allNeededCustomerIds.Contains(c.CustomerId))
                    .ToDictionaryAsync(c => c.CustomerId);

                // TMMIN KANBAN FIX: Pre-calculate the true SNP (QtyLot) for each PartNo + Dock!
                // This prevents fractional kanban rows (e.g. 80 qty, 2 kanban) from skewing the QtyLot downwards
                // We MUST group by Dock as well because TMMIN packs the same part with different SNPs for different docks (e.g. Dock 43 vs Dock 53).
                var trueSnps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var validItems = request.Items.Where(i => i.ExcelKanban > 0 && i.Qty > 0 && !string.IsNullOrWhiteSpace(i.PartNo)).ToList();
                var partGroups = validItems.GroupBy(i => i.PartNo!.Trim() + "|" + (i.ItemCustomerId > 0 ? i.ItemCustomerId.ToString() : "0"));
                foreach (var pg in partGroups)
                {
                    int maxSnp = pg.Max(i => (int)Math.Ceiling((double)i.Qty / i.ExcelKanban));
                    if (maxSnp > 0)
                    {
                        trueSnps[pg.Key] = maxSnp;
                    }
                }

                int seqCounter = 1;
                foreach (var manifestGroup in groupedByManifestDock)
                {
                    var first = manifestGroup.First();
                    var manifestNum = first.Manifesting ?? "";

                    // Tentukan customer untuk grup ini:
                    var groupCustomerId = customer.CustomerId;
                    if (request.PerItemDock)
                    {
                        if (first.ItemCustomerId > 0)
                            groupCustomerId = first.ItemCustomerId;
                        else
                            continue; // Skip unmatched items in multi-dock mode to prevent them from inheriting the seed dock!
                    }
                    var groupCustomer = customerCache.ContainsKey(groupCustomerId)
                        ? customerCache[groupCustomerId]
                        : customer;

                    // Tentukan scheduledDate per manifest:
                    // 1. Coba dari item pertama yang punya ScheduledDate (per-item dari file)
                    // 2. Fallback ke globalDetectedDate (DetectedScheduledDate dari request)
                    // 3. Fallback terakhir ke DateTime.Today
                    DateTime scheduledDate = DateTime.Today;
                    var firstItemWithDate = manifestGroup.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.ScheduledDate));
                    if (firstItemWithDate != null &&
                        DateTime.TryParse(firstItemWithDate.ScheduledDate,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime parsedItemDate))
                    {
                        scheduledDate = parsedItemDate.Date;
                    }
                    else if (globalDetectedDate.HasValue)
                    {
                        scheduledDate = globalDetectedDate.Value;
                    }

                    _logger.LogInformation("SmartImportConfirm: Manifest='{Manifest}' Dock='{Dock}' ScheduledDate={Date}",
                        manifestNum, groupCustomer.CustomerName, scheduledDate.ToString("yyyy-MM-dd"));

                    // Cek apakah manifest+dock combo sudah ada (append mode)
                    DeliverySchedule? existingSchedule = null;
                    if (!string.IsNullOrEmpty(manifestNum))
                    {
                        existingSchedule = existingSchedules.FirstOrDefault(s => 
                            s.ScheduleNumber.ToUpper() == manifestNum.Trim().ToUpper() && 
                            s.CustomerId == groupCustomer.CustomerId && 
                            s.ScheduledDate == scheduledDate);
                    }

                    var scheduleNumber = string.IsNullOrEmpty(manifestNum)
                        ? $"SCH-{scheduledDate:yyyyMMdd}-{seqCounter++}"
                        : manifestNum.Trim();

                    var schedule = existingSchedule ?? new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = groupCustomer.CustomerId,
                        ScheduledDate = scheduledDate,
                        Status = "Scheduled",
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "SmartImport",
                        Route = groupCustomer.Route,
                        Cycle = groupCustomer.Cycle,
                        Area = groupCustomer.Area,
                        StartPrepareTime = groupCustomer.StartPrepareTime,
                        StdPrepareTime = groupCustomer.StdPrepareTime,
                        Range = groupCustomer.Range,
                        SKID = groupCustomer.SKID
                    };

                    if (existingSchedule == null)
                    {

                    // --- Sequential Forward Bumping Logic ---
                    // Setiap milestone harus secara kronologis >= milestone sebelumnya.
                    var baseDate = scheduledDate.Date;
                    var startMinutes = groupCustomer.StartPrepareTime;
                    var stdPMinutes = groupCustomer.StdPrepareTime;
                    
                    var startPrepTime = baseDate.AddMinutes(startMinutes);
                    var stdEndTime = baseDate.AddMinutes(stdPMinutes);
                    if (stdPMinutes < startMinutes) stdEndTime = stdEndTime.AddDays(1);

                    var enterDockTime = ParseTimeToDateTime(groupCustomer.Docking, scheduledDate);
                    // Dock In: bump sekali jika sebelum startPrepTime (overnight case).
                    // JANGAN pakai stdEndTime agar tidak double-bump ketika DockIn dan EndPrep
                    // berselisih kecil pada hari yang sama (mis: 04:30 vs 04:45).
                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                        enterDockTime = enterDockTime.Value.AddDays(1);

                    var pickupTime = ParseTimeToDateTime(groupCustomer.Pickup, scheduledDate);
                    if (pickupTime.HasValue)
                    {
                        var anchor = enterDockTime ?? stdEndTime;
                        if (pickupTime.Value < anchor) pickupTime = pickupTime.Value.AddDays(1);
                    }

                    var etdTime = ParseTimeToDateTime(groupCustomer.ETD, scheduledDate);
                    if (etdTime.HasValue)
                    {
                        var anchor = pickupTime ?? enterDockTime ?? stdEndTime;
                        if (etdTime.Value < anchor) etdTime = etdTime.Value.AddDays(1);
                    }

                        schedule.EnterDockTime = enterDockTime;
                        schedule.PickupTime = pickupTime;
                        schedule.ETD = etdTime;
                    }

                    // Add items
                    decimal totalQty = 0;
                    int itemsAddedToExisting = 0;
                    foreach (var itemData in manifestGroup)
                    {
                        string itemCode = itemData.PartNo.Trim().ToUpper();
                        // Strip dashes/spaces for normalized comparison: "16261-0Y030-00" → "162610Y03000"
                        string itemCodeNorm = System.Text.RegularExpressions.Regex.Replace(itemCode, @"[-\s]", "");

                        // Cek apakah item sudah ada di schedule ini (append / update logic)
                        if (existingSchedule != null)
                        {
                            var existingDi = existingSchedule.DeliveryItems.FirstOrDefault(di => 
                            {
                                string NormalizeString(string? s) => s != null ? System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "") : "";
                                string normExt = NormalizeString(di.ExternalPartNo);
                                if (!string.IsNullOrEmpty(normExt) && normExt == itemCodeNorm) return true;
                                if (di.Item != null)
                                {
                                    if (NormalizeString(di.Item.CustomerPartNumber) == itemCodeNorm) return true;
                                    if (NormalizeString(di.Item.VIN) == itemCodeNorm) return true;
                                    if (NormalizeString(di.Item.ItemCode) == itemCodeNorm) return true;
                                }
                                return false;
                            });
                            
                            if (existingDi != null)
                            {
                                // Item sudah ada. Jika Qty di file lebih besar/berbeda, update Qty-nya!
                                decimal diff = itemData.Qty - existingDi.Quantity;
                                if (diff != 0)
                                {
                                    existingDi.Quantity = itemData.Qty;
                                    totalQty += diff;
                                    itemsAddedToExisting++; // Tandai ada perubahan agar schedule di-save
                                    _logger.LogInformation("SmartImportConfirm: Updated existing item '{PartNo}' Qty from {Old} to {New}", itemCode, existingDi.Quantity - diff, itemData.Qty);
                                }

                                // Update the override as well if the SNP changed
                                string existingSnpKey = itemCode + "|" + (itemData.ItemCustomerId > 0 ? itemData.ItemCustomerId.ToString() : "0");
                                if (trueSnps.TryGetValue(existingSnpKey, out int existingCalculatedSnp) && existingCalculatedSnp > 0)
                                {
                                    existingDi.QtyLotOverride = existingCalculatedSnp;
                                }

                                continue; // Skip pembuatan DeliveryItem baru
                            }
                        }

                        // -----------------------------------------------------------------------
                        // FlexMatch: normalize both sides
                        //   1. Strip dashes/spaces  (Part No format: "16261-0Y030-00" → "162610Y03000")
                        //   2. VinHelper.Normalize  (strip suffix LBX/LB/X, prefix LB: "TA1680LB" → "TA1680")
                        // -----------------------------------------------------------------------
                        string NormFull(string? s)
                        {
                            if (string.IsNullOrWhiteSpace(s)) return "";
                            var stripped = System.Text.RegularExpressions.Regex.Replace(s.Trim().ToUpper(), @"[-\s]", "");
                            return VinHelper.Normalize(stripped);
                        }
                        bool FlexMatch(string? a, string? b)
                        {
                            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
                            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                            var aN = NormFull(a); var bN = NormFull(b);
                            return !string.IsNullOrEmpty(aN) && !string.IsNullOrEmpty(bN) && aN == bN;
                        }

                        Item? matchedItem = null;
                        string? finalVin = null;

                        // Helper to search for item in DB list by multiple fields
                        // P0: Prioritaskan item yang Customer-nya sama dengan schedule ini
                        Item? FindInDb(string code) =>
                            allDbItems.FirstOrDefault(i => FlexMatch(i.CustomerPartNumber, code) && i.Customer == groupCustomer.CustomerName) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, code) && i.Customer == groupCustomer.CustomerName) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, code) && i.Customer == groupCustomer.CustomerName) ??
                            // Fallback: cari di semua customer (misal master data ada di dock lain)
                            allDbItems.FirstOrDefault(i => FlexMatch(i.CustomerPartNumber, code)) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, code)) ??
                            allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, code));

                        // P1: Gunakan VIN yang sudah di-resolve saat preview (shortcut — paling akurat)
                        // VIN ini berasal dari ResolveVin() di SmartImportPreview, tidak perlu matching ulang
                        if (!string.IsNullOrWhiteSpace(itemData.Vin))
                        {
                            matchedItem = FindInDb(itemData.Vin);
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // P1: Direct match in master Items (CustomerPartNumber, VIN, ItemCode)
                        if (matchedItem == null)
                        {
                            matchedItem = FindInDb(itemCode);
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // P2: Via ItemMappings.CustomerPartNumber → resolve VIN → find Item
                        //     Customer-specific mapping has priority over cross-customer matches
                        if (matchedItem == null)
                        {
                            var map = (!string.IsNullOrEmpty(confirmCustCode)
                                ? allMappings.FirstOrDefault(m =>
                                    FlexMatch(m.CustomerPartNumber, itemCode) &&
                                    !string.IsNullOrWhiteSpace(m.Customer) &&
                                    m.Customer.Contains(confirmCustCode, StringComparison.OrdinalIgnoreCase))
                                : null)
                                ?? allMappings.FirstOrDefault(m => FlexMatch(m.CustomerPartNumber, itemCode));
                            if (map != null && !string.IsNullOrWhiteSpace(map.VIN))
                            {
                                // map.VIN might be "TA1680LB" while master item has "TA1680" — FlexMatch handles this
                                matchedItem = allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, map.VIN))
                                           ?? allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, map.VIN));
                                finalVin = matchedItem?.VIN ?? map.VIN;
                            }
                        }

                        // P3: Via ItemMappings.VIN (part no IS the VIN in mapping table)
                        //     Customer-specific mapping has priority over cross-customer matches
                        if (matchedItem == null)
                        {
                            var mapByVin = (!string.IsNullOrEmpty(confirmCustCode)
                                ? allMappings.FirstOrDefault(m =>
                                    FlexMatch(m.VIN, itemCode) &&
                                    !string.IsNullOrWhiteSpace(m.Customer) &&
                                    m.Customer.Contains(confirmCustCode, StringComparison.OrdinalIgnoreCase))
                                : null)
                                ?? allMappings.FirstOrDefault(m => FlexMatch(m.VIN, itemCode));
                            if (mapByVin != null && !string.IsNullOrWhiteSpace(mapByVin.VIN))
                            {
                                matchedItem = allDbItems.FirstOrDefault(i => FlexMatch(i.VIN, mapByVin.VIN))
                                           ?? allDbItems.FirstOrDefault(i => FlexMatch(i.ItemCode, mapByVin.VIN));
                                finalVin = matchedItem?.VIN ?? mapByVin.VIN;
                            }
                        }

                        // P4: Reuse auto-created item from this session (same part no appeared in earlier manifest)
                        if (matchedItem == null)
                        {
                            matchedItem = newItems.FirstOrDefault(i => FlexMatch(i.CustomerPartNumber, itemCode)
                                                                    || FlexMatch(i.VIN, itemCode));
                            if (matchedItem != null) finalVin = matchedItem.VIN;
                        }

                        // Target VIN: dari preview resolve, matched item, atau normalized part no sebagai fallback terakhir
                        string targetVin = finalVin
                            ?? (!string.IsNullOrWhiteSpace(itemData.Vin) ? itemData.Vin : null)
                            ?? itemCodeNorm;

                        _logger.LogInformation(
                            "SmartImportConfirm: PartNo='{Part}' VinFromPreview='{PreviewVin}' → {Status}, VIN='{Vin}', ItemId={Id}",
                            itemCode,
                            itemData.Vin ?? "(none)",
                            matchedItem != null ? "MATCHED" : "AUTO-CREATE",
                            finalVin ?? "(none)",
                            matchedItem?.ItemId ?? 0);

                        // Cek apakah item ada tapi dinonaktifkan (sebelum auto-create)
                        // Cek via itemCode (PartNo dari manifest) ATAU itemData.Vin (VIN dari preview) ATAU finalVin (dari mapping P2/P3)
                        if (matchedItem == null)
                        {
                            var inactiveMatch = allDbItemsInactive.FirstOrDefault(i =>
                                FlexMatch(i.CustomerPartNumber, itemCode) ||
                                FlexMatch(i.VIN, itemCode) ||
                                FlexMatch(i.ItemCode, itemCode) ||
                                (!string.IsNullOrWhiteSpace(itemData.Vin) && (
                                    FlexMatch(i.VIN, itemData.Vin) ||
                                    FlexMatch(i.ItemCode, itemData.Vin))) ||
                                (!string.IsNullOrWhiteSpace(finalVin) && (
                                    FlexMatch(i.VIN, finalVin) ||
                                    FlexMatch(i.ItemCode, finalVin))));
                            if (inactiveMatch != null)
                            {
                                inactiveItemWarnings.Add(itemCode);
                                matchedItem = inactiveMatch; // tetap pakai item (tapi tandai sebagai warning)
                                finalVin = inactiveMatch.VIN;
                            }
                        }

                        // Auto-create only if not found anywhere (aktif maupun nonaktif)
                        if (matchedItem == null)
                        {
                            matchedItem = new Item
                            {
                                ItemCode = targetVin,
                                VIN = targetVin,
                                CustomerPartNumber = itemCode,  // keep original with dashes
                                Customer = customer.CustomerName,
                                ItemName = "SmartImport (" + itemCode + ")",
                                Description = "Auto-created from Smart Import",
                                IsActive = true,
                                CreatedDate = DateTime.Now,
                                QtyLot = 1
                            };
                            _context.Items.Add(matchedItem);
                            newItems.Add(matchedItem); // track separately from DB items
                        }
                        else if (matchedItem.ItemId > 0
                            && string.IsNullOrWhiteSpace(matchedItem.CustomerPartNumber)
                            && itemCode.Contains('-'))
                        {
                            // Enrich existing item with customer part number if missing
                            matchedItem.CustomerPartNumber = itemCode;
                        }

                        // TMMIN KANBAN FIX: Find the calculated SNP for this Part + Dock combo
                        string snpKey = itemCode + "|" + (itemData.ItemCustomerId > 0 ? itemData.ItemCustomerId.ToString() : "0");
                        int? exactSnp = null;
                        if (trueSnps.TryGetValue(snpKey, out int calculatedSnp))
                        {
                            exactSnp = calculatedSnp;
                            if (calculatedSnp > 0 && matchedItem.QtyLot != calculatedSnp)
                            {
                                _logger.LogInformation("SmartImportConfirm: Updating global fallback SNP for {Part} from {Old} to {New} based on Excel Kanban", itemCode, matchedItem.QtyLot, calculatedSnp);
                                matchedItem.QtyLot = calculatedSnp;
                            }
                        }

                        var deliveryItem = new DeliveryItem
                        {
                            Quantity = itemData.Qty,
                            ActualQuantity = 0,
                            ExternalPartNo = itemCode,  // simpan Part No asli dari manifest (dengan tanda hubung)
                            QtyLotOverride = exactSnp,  // Kunci SNP yang spesifik untuk Dock ini, abaikan Master Data jika berbeda
                            CreatedDate = DateTime.Now
                        };

                        if (matchedItem.ItemId == 0)
                            deliveryItem.Item = matchedItem;
                        else
                            deliveryItem.ItemId = matchedItem.ItemId;

                        schedule.DeliveryItems.Add(deliveryItem);
                        totalQty += itemData.Qty;
                        itemCount++;
                        itemsAddedToExisting++;
                    }

                    if (existingSchedule == null)
                    {
                        schedule.TotalTargetQuantity = totalQty;
                        
                        // Guard: jangan simpan schedule kosong (ghost schedule) — item harus > 0
                        if (!schedule.DeliveryItems.Any())
                        {
                            skippedCount++;
                            _logger.LogWarning("SmartImportConfirm: Skip manifest '{Manifest}' karena tidak ada item valid (semua PartNo kosong).", scheduleNumber);
                            continue;
                        }
                        
                        _context.DeliverySchedules.Add(schedule);
                        successCount++;
                    }
                    else
                    {
                        if (itemsAddedToExisting > 0)
                        {
                            schedule.TotalTargetQuantity += totalQty;
                            successCount++; // count as success if we actually appended items
                            _logger.LogInformation("SmartImportConfirm: Appended {Count} new items to existing manifest '{Manifest}'.", itemsAddedToExisting, scheduleNumber);
                        }
                        else
                        {
                            skippedCount++; // all items were already in the schedule
                        }
                    }
                }

                await _context.SaveChangesAsync();

                // SignalR notification — kirim sebelum sync supaya dashboard langsung update
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "smartImport",
                    message = $"Smart Import: {successCount} schedule ({itemCount} items) berhasil di-import.",
                    timestamp = DateTime.Now
                });

                // Build warning message untuk item yang dinonaktifkan
                string? inactiveWarningMsg = null;
                if (inactiveItemWarnings.Any())
                {
                    var sample = string.Join(", ", inactiveItemWarnings.Distinct().Take(5));
                    if (inactiveItemWarnings.Distinct().Count() > 5) sample += $" (+{inactiveItemWarnings.Distinct().Count() - 5} lagi)";
                    inactiveWarningMsg = $"⚠️ {inactiveItemWarnings.Distinct().Count()} item dinonaktifkan di Master Data: {sample}";
                }

                // Auto-sync pending preparations di background agar response tidak tertahan.
                // Buat scope baru karena DbContext/PreparationSyncService adalah Scoped service.
                DateTime syncDate = globalDetectedDate ?? DateTime.Today;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var bgSync = scope.ServiceProvider.GetRequiredService<PreparationSyncService>();
                        var bgHub  = scope.ServiceProvider.GetRequiredService<IHubContext<DeliveryHub>>();
                        int synced = await bgSync.SyncAllPendingAsync(filterDate: syncDate);
                        if (synced > 0)
                        {
                            await bgHub.Clients.All.SendAsync("deliveryUpdated", new
                            {
                                action = "syncComplete",
                                message = $"{synced} preparation otomatis tersinkronisasi.",
                                timestamp = DateTime.Now
                            });
                        }
                    }
                    catch (Exception bgEx)
                    {
                        _logger.LogWarning(bgEx, "SmartImport background sync error");
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"Berhasil import {successCount} schedule ({itemCount} items)." +
                              (skippedCount > 0 ? $" {skippedCount} manifest di-skip (sudah ada)." : ""),
                    warnings = inactiveWarningMsg
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ====================================================================
        // GetScheduleInfo: Lookup info jadwal berdasarkan ScheduleId atau ScheduleNumber
        // Digunakan oleh Repair Split modal untuk field "Schedule ID (Source)".
        // ====================================================================
        [HttpGet]
        public async Task<IActionResult> GetScheduleInfo(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(new { success = false });

            DeliverySchedule? sched = null;

            // Coba parse sebagai integer ID dulu
            if (int.TryParse(query.Trim(), out int schedId))
            {
                sched = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.ScheduleId == schedId);
            }

            // Jika tidak ketemu atau input bukan angka, cari by ScheduleNumber
            if (sched == null)
            {
                var qTrim = query.Trim();
                sched = await _context.DeliverySchedules
                    .Include(s => s.Customer)
                    .Include(s => s.DeliveryItems)
                    .AsNoTracking()
                    .Where(s => s.ScheduleNumber != null && s.ScheduleNumber == qTrim)
                    .OrderByDescending(s => s.ScheduledDate)
                    .FirstOrDefaultAsync();
            }

            if (sched == null)
                return Json(new { success = false, message = "Jadwal tidak ditemukan." });

            return Json(new
            {
                success = true,
                scheduleId = sched.ScheduleId,
                scheduleNumber = sched.ScheduleNumber,
                customerName = sched.Customer?.CustomerName,
                cycle = sched.Cycle,
                route = sched.Route,
                scheduledDate = sched.ScheduledDate.ToString("dd MMM yyyy"),
                totalItems = sched.DeliveryItems?.Count ?? 0,
                status = sched.Status
            });
        }

        // GetCustomerInfo: Lookup nama/kode customer berdasarkan CustomerId
        // Digunakan oleh Repair Split modal.
        // ====================================================================
        [HttpGet]
        public async Task<IActionResult> GetCustomerInfo(int customerId)
        {
            var cust = await _context.Customers.FindAsync(customerId);
            if (cust == null) return Json(new { success = false });
            return Json(new { success = true, customerName = cust.CustomerName, customerCode = cust.CustomerCode, cycle = cust.Cycle });
        }

        // ====================================================================
        // RepairSplitSchedule: Memisahkan item yang salah masuk ke jadwal C1
        // ke jadwal baru untuk customer target (C3).
        // Identifikasi berdasarkan ItemMapping.Customer dan Item.Customer.
        // ====================================================================
        [HttpPost]
        public async Task<IActionResult> RepairSplitSchedule([FromBody] RepairSplitRequest request)
        {
            if (request == null)
                return Json(new { success = false, message = "Request tidak valid." });

            try
            {
                // 1. Load source schedule
                var sourceSchedule = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                    .FirstOrDefaultAsync(s => s.ScheduleId == request.SourceScheduleId);
                if (sourceSchedule == null)
                    return Json(new { success = false, message = $"Schedule ID {request.SourceScheduleId} tidak ditemukan." });

                // 2. Load target customer (C3)
                var targetCustomer = await _context.Customers
                    .FirstOrDefaultAsync(c => c.CustomerId == request.TargetCustomerId);
                if (targetCustomer == null)
                    return Json(new { success = false, message = $"Customer ID {request.TargetCustomerId} tidak ditemukan." });

                // 3. Load ItemMappings for quick lookup
                var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                var targetCode = (targetCustomer.CustomerCode ?? "").Trim().ToUpper();
                var targetName = (targetCustomer.CustomerName ?? "").Trim().ToUpper();

                // Helper: does a string field indicate this item belongs to targetCustomer?
                bool MatchesTarget(string? field)
                {
                    if (string.IsNullOrWhiteSpace(field)) return false;
                    var f = field.Trim().ToUpper();
                    return (!string.IsNullOrEmpty(targetCode) && f.Contains(targetCode))
                        || (!string.IsNullOrEmpty(targetName) && f.Contains(targetName));
                }

                // 4. Classify each DeliveryItem
                var itemsToMove = new List<DeliveryItem>();
                var itemsToKeep = new List<DeliveryItem>();

                foreach (var di in sourceSchedule.DeliveryItems)
                {
                    bool belongsToTarget = false;

                    // Strategy A: Check ItemMapping.Customer for this PartNo
                    var extPart = (di.ExternalPartNo ?? "").Trim().ToUpper();
                    if (!string.IsNullOrEmpty(extPart))
                    {
                        string NormPart(string? s) => System.Text.RegularExpressions.Regex.Replace(
                            (s ?? "").Trim().ToUpper(), @"[-\s]", "");
                        var normExt = NormPart(extPart);

                        var mapping = allMappings.FirstOrDefault(m =>
                        {
                            var normCpn = NormPart(m.CustomerPartNumber);
                            return (normCpn == normExt || m.CustomerPartNumber?.Trim().ToUpper() == extPart)
                                && MatchesTarget(m.Customer);
                        });
                        if (mapping != null) belongsToTarget = true;
                    }

                    // Strategy B: Check Item.Customer field
                    if (!belongsToTarget && di.Item != null)
                        belongsToTarget = MatchesTarget(di.Item.Customer);

                    if (belongsToTarget)
                        itemsToMove.Add(di);
                    else
                        itemsToKeep.Add(di);
                }

                // Dry run: just return summary without making changes
                if (request.DryRun)
                {
                    return Json(new
                    {
                        success = true,
                        dryRun = true,
                        sourceScheduleNumber = sourceSchedule.ScheduleNumber,
                        sourceCustomer = (await _context.Customers.FindAsync(sourceSchedule.CustomerId))?.CustomerName,
                        targetCustomer = targetCustomer.CustomerName,
                        totalItems = sourceSchedule.DeliveryItems.Count,
                        itemsToMove = itemsToMove.Count,
                        itemsToKeep = itemsToKeep.Count,
                        movedPartNos = itemsToMove.Select(d => d.ExternalPartNo ?? d.Item?.CustomerPartNumber ?? d.Item?.VIN ?? "?").ToList(),
                        message = itemsToMove.Count == 0
                            ? "TIDAK ADA item yang teridentifikasi sebagai milik target customer. Cek apakah ItemMapping sudah diisi atau hapus jadwal C1 dan import ulang."
                            : $"Akan memindahkan {itemsToMove.Count} item ke jadwal baru {targetCustomer.CustomerName}."
                    });
                }

                if (itemsToMove.Count == 0)
                    return Json(new { success = false, message = "Tidak ada item yang bisa dipindahkan. Cek ItemMapping atau import ulang file setelah fix kode." });

                // 5. Create new schedule for target customer
                var scheduledDate = sourceSchedule.ScheduledDate;
                var newSchedule = new DeliverySchedule
                {
                    ScheduleNumber = sourceSchedule.ScheduleNumber,
                    CustomerId     = targetCustomer.CustomerId,
                    ScheduledDate  = scheduledDate,
                    Status         = "Scheduled",
                    CreatedDate    = DateTime.Now,
                    CreatedBy      = User.Identity?.Name ?? "RepairSplit",
                    Route          = targetCustomer.Route,
                    Cycle          = targetCustomer.Cycle,
                    Area           = targetCustomer.Area,
                    SKID           = targetCustomer.SKID,
                    Range          = targetCustomer.Range,
                    StartPrepareTime = targetCustomer.StartPrepareTime,
                    StdPrepareTime   = targetCustomer.StdPrepareTime,
                    EnterDockTime    = ParseTimeToDateTime(targetCustomer.Docking, scheduledDate),
                    PickupTime       = ParseTimeToDateTime(targetCustomer.Pickup,  scheduledDate),
                    ETD              = ParseTimeToDateTime(targetCustomer.ETD,     scheduledDate)
                };

                // 6. Move items: detach from source, attach to new schedule
                decimal movedQty = 0;
                foreach (var di in itemsToMove)
                {
                    sourceSchedule.DeliveryItems.Remove(di);
                    newSchedule.DeliveryItems.Add(di);
                    movedQty += di.Quantity;
                }

                // 7. Update totals
                sourceSchedule.TotalTargetQuantity = itemsToKeep.Sum(d => d.Quantity);
                newSchedule.TotalTargetQuantity = movedQty;

                _context.DeliverySchedules.Add(newSchedule);
                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    dryRun = false,
                    newScheduleId = newSchedule.ScheduleId,
                    message = $"Berhasil! {itemsToMove.Count} item dipindahkan dari {sourceSchedule.ScheduleNumber} ({(await _context.Customers.FindAsync(sourceSchedule.CustomerId))?.CustomerName}) ke jadwal baru {targetCustomer.CustomerName}."
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        public class RepairSplitRequest
        {
            public int SourceScheduleId { get; set; }
            public int TargetCustomerId { get; set; }
            public bool DryRun { get; set; } = true;
        }


        public class SmartImportConfirmRequest
        {
            public int CustomerId { get; set; }
            public List<SmartImportItemRequest> Items { get; set; } = new();
            /// <summary>
            /// Tanggal jadwal yang terdeteksi dari file (misal tgl 9 dari file TMMIN).
            /// Digunakan sebagai fallback ScheduledDate untuk manifest yang tidak memiliki tanggal per-item.
            /// Jika null, fallback ke DateTime.Today.
            /// </summary>
            public string? DetectedScheduledDate { get; set; }
            public bool PerItemDock { get; set; }
        }

        public class SmartImportItemRequest
        {
            public string? Manifesting { get; set; }
            public string PartNo { get; set; } = "";
            /// <summary>
            /// VIN yang sudah di-resolve saat preview (dari ItemMappings/Items).
            /// Jika tersedia, digunakan sebagai shortcut lookup item — tidak perlu matching ulang.
            /// </summary>
            public string? Vin { get; set; }
            public int Qty { get; set; }
            public int ExcelKanban { get; set; }
            /// <summary>
            /// Tanggal jadwal yang terdeteksi dari file per item (format yyyy-MM-dd).
            /// Digunakan sebagai ScheduledDate jadwal preparation — bisa berbeda dari tanggal upload.
            /// </summary>
            public string? ScheduledDate { get; set; }
            /// <summary>
            /// CustomerId dock yang sudah di-match per item (untuk TMMIN multi-dock).
            /// Jika 0 atau null, pakai CustomerId dari request.
            /// </summary>
            public int ItemCustomerId { get; set; }
        }

        #endregion

        public class ClearPreparationRequest
        {
            public string Mode { get; set; } = "all"; // "date" atau "all"
            public string? Date { get; set; }         // format "yyyy-MM-dd", hanya dipakai jika Mode = "date"
        }

        [HttpPost]
        public async Task<IActionResult> ClearPreparationData([FromBody] ClearPreparationRequest? request)
        {
            request ??= new ClearPreparationRequest { Mode = "all" };

            int scheduleCount;
            int prepCount;

            if (request.Mode == "date" && !string.IsNullOrWhiteSpace(request.Date) && DateTime.TryParse(request.Date, out DateTime targetDate))
            {
                var targetDay = targetDate.Date;

                // Hapus jadwal pada tanggal tersebut beserta DeliveryItems-nya (cascade)
                var schedules = await _context.DeliverySchedules
                    .Where(s => s.ScheduledDate.Date == targetDay)
                    .ToListAsync();
                scheduleCount = schedules.Count;
                _context.DeliverySchedules.RemoveRange(schedules);

                // Hapus PreparationRecords yang tanggal scan-nya sama
                var preps = await _context.PreparationRecords
                    .Where(p => p.CreatedDate.Date == targetDay)
                    .ToListAsync();
                prepCount = preps.Count;
                _context.PreparationRecords.RemoveRange(preps);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Berhasil menghapus {scheduleCount} jadwal dan {prepCount} data Preparation tanggal {targetDay:dd/MM/yyyy}." });
            }
            else
            {
                // Hapus semua
                scheduleCount = await _context.DeliverySchedules.CountAsync();
                _context.DeliverySchedules.RemoveRange(_context.DeliverySchedules);

                prepCount = await _context.PreparationRecords.CountAsync();
                _context.PreparationRecords.RemoveRange(_context.PreparationRecords);

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Berhasil menghapus semua data: {scheduleCount} jadwal dan {prepCount} data Preparation." });
            }
        }

        // =====================================================================
        // EXPORT EXCEL — Semua jadwal berdasarkan filter tanggal
        // =====================================================================
        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? filterDate)
        {
            var targetDate = filterDate ?? DateTime.Today;
            var schedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == targetDate.Date)
                .OrderBy(s => s.Customer!.CustomerCode)
                .ThenBy(s => s.Route)
                .ThenBy(s => s.Cycle)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();

            return BuildExcelFile(schedules, $"Jadwal_{targetDate:yyyyMMdd}");
        }

        // =====================================================================
        // EXPORT EXCEL — Hanya jadwal yang dipilih (dari bulk select)
        // =====================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportExcelSelected(string selectedIds)
        {
            if (string.IsNullOrWhiteSpace(selectedIds))
                return BadRequest("Tidak ada jadwal yang dipilih.");

            var ids = selectedIds.Split(',')
                .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (!ids.Any())
                return BadRequest("ID tidak valid.");

            var schedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems).ThenInclude(di => di.Item)
                .Where(s => ids.Contains(s.ScheduleId))
                .OrderBy(s => s.Customer!.CustomerCode)
                .ThenBy(s => s.Route)
                .ThenBy(s => s.Cycle)
                .ThenBy(s => s.ScheduleNumber)
                .ToListAsync();

            return BuildExcelFile(schedules, $"Jadwal_Pilihan_{DateTime.Now:yyyyMMdd_HHmm}");
        }

        // =====================================================================
        // Helper: bangun file Excel dari list schedule
        // =====================================================================
        private IActionResult BuildExcelFile(List<DeliverySchedule> schedules, string filePrefix)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Jadwal Preparation");

            // ── HEADER ──────────────────────────────────────────────────────
            string[] headers = {
                "NO", "TANGGAL", "MANIFEST", "DOCK", "CUSTOMER", "ROUTE", "CYCLE",
                "PART NO", "LABEL PART", "QTY TARGET (PCS)", "QTY ACTUAL (PCS)",
                "QTY LOT / KBN", "ACTUAL KBN",
                "START PREP", "END PREP",
                "ENTER DOCK (STD)", "ENTER DOCK (ACT)",
                "PICKUP (STD)", "PICKUP (ACT)",
                "ETD", "ACT DELIVERY",
                "ACT PREPARE TIME", "STATUS PREP",
                "STATUS JADWAL", "RANGE"
            };

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a3a5c");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            int row = 2;
            int no = 1;

            foreach (var s in schedules)
            {
                var custCode   = s.Customer?.CustomerCode ?? "-";
                var dockName   = s.Customer?.CustomerName ?? (s.Area ?? "-");
                var startPrep  = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
                var endPrep    = s.StdPrepareTime   > 0 ? s.StdPrepareTime   : (s.Customer?.StdPrepareTime   ?? 0);
                var startPStr  = $"{startPrep / 60:D2}:{startPrep % 60:D2}";
                var endPStr    = $"{endPrep   / 60:D2}:{endPrep   % 60:D2}";

                // Hitung kanban
                double kbnTarget = 0, kbnActual = 0;
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        if (qpc > 0)
                        {
                            var diTarget = Math.Ceiling((double)di.Quantity / qpc);
                            kbnTarget += diTarget;
                            // Jika completed, kembalikan target (hindari Floor < Ceiling untuk angka ganjil)
                            kbnActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                ? diTarget
                                : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                        }
                    }
                }

                // ReadyToDockTime (waktu selesai prepare)
                var readyTime = s.ReadyToDockTime?.ToString("HH:mm") ?? "-";

                var items = s.DeliveryItems != null && s.DeliveryItems.Any()
                    ? s.DeliveryItems.ToList()
                    : new List<DeliveryItem>();

                if (!items.Any())
                {
                    // Schedule tanpa item — 1 baris kosong
                    WriteExcelRow(ws, row++, no++, s, null, custCode, dockName, startPStr, endPStr,
                                  kbnTarget, kbnActual, readyTime);
                }
                else
                {
                    foreach (var di in items)
                    {
                        WriteExcelRow(ws, row++, no++, s, di, custCode, dockName, startPStr, endPStr,
                                      kbnTarget, kbnActual, readyTime);
                    }
                }
            }

            // Hanya header yang berwarna; baris data biarkan putih polos
            // (tidak ada zebra striping agar tidak terlalu rame)

            ws.Columns().AdjustToContents();
            ws.Row(1).Height = 22;

            // Freeze header row
            ws.SheetView.FreezeRows(1);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileName = $"{filePrefix}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private static void WriteExcelRow(IXLWorksheet ws, int row, int no,
            DeliverySchedule s, DeliveryItem? di,
            string custCode, string dockName,
            string startPStr, string endPStr,
            double kbnTarget, double kbnActual, string readyTime)
        {
            int c = 1;
            ws.Cell(row, c++).Value = no;
            ws.Cell(row, c++).Value = s.ScheduledDate.ToString("dd/MM/yyyy");
            ws.Cell(row, c++).Value = s.ScheduleNumber ?? "-";
            ws.Cell(row, c++).Value = dockName;
            ws.Cell(row, c++).Value = custCode;
            ws.Cell(row, c++).Value = s.Route ?? "-";
            ws.Cell(row, c++).Value = s.Cycle ?? "-";
            ws.Cell(row, c++).Value = di?.Item?.CustomerPartNumber ?? di?.Item?.ItemName ?? "-";
            ws.Cell(row, c++).Value = di?.Item?.ItemName ?? "-";
            ws.Cell(row, c++).Value = (double)(di?.Quantity ?? 0);
            ws.Cell(row, c++).Value = (double)(di?.ActualQuantity ?? 0);
            ws.Cell(row, c++).Value = kbnTarget;
            ws.Cell(row, c++).Value = kbnActual;
            ws.Cell(row, c++).Value = startPStr;
            ws.Cell(row, c++).Value = endPStr;
            ws.Cell(row, c++).Value = s.EnterDockTime?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = s.ActualEnterDockTime?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = s.PickupTime?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = s.ActualStartTime?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = s.ETD?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = s.ActualEndTime?.ToString("HH:mm") ?? "-";
            ws.Cell(row, c++).Value = readyTime;
            ws.Cell(row, c++).Value = s.PreparationStatus ?? "-";
            ws.Cell(row, c++).Value = s.Status ?? "-";
            ws.Cell(row, c).Value   = s.Range ?? "-";

            // Border tipis tiap sel
            ws.Row(row).Cells(1, c).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    public class ExcelRowData
    {
        public int RowNumber { get; set; }
        public Customer Customer { get; set; } = null!;
        public string ManifestNum { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string? DockLocation { get; set; }
        public string Route { get; set; } = "";
        public string Cycle { get; set; } = "";
        public Item? MatchedItem { get; set; }
        public int Quantity { get; set; }
        public IXLRow Row { get; set; } = null!;
        public int PickupColIndex { get; set; } = -1;
        public int EtdColIndex { get; set; } = -1;
    }

    // DTO untuk menerima request bulk delete dari frontend
    public class BulkDeleteRequest
    {
        public int[] ids { get; set; } = Array.Empty<int>();
    }

    // DTO untuk RemapItems
    public class RemapItemsRequest
    {
        public string? ScheduleDate { get; set; }
    }

    public class RecalcQtyRequest
    {
        public string? ScheduleDate { get; set; }
    }
}
