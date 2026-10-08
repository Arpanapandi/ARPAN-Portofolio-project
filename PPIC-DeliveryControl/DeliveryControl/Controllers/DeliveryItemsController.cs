using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Helpers;
using DeliveryControl.Hubs;
using DeliveryControl.Models;

namespace DeliveryControl.Controllers
{
    public class DeliveryItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _deliveryHubContext;
        private readonly IHubContext<StockHub> _stockHubContext;

        public DeliveryItemsController(
            ApplicationDbContext context,
            IHubContext<DeliveryHub> deliveryHubContext,
            IHubContext<StockHub> stockHubContext)
        {
            _context = context;
            _deliveryHubContext = deliveryHubContext;
            _stockHubContext = stockHubContext;
        }

        private async Task<bool> IsAutoEnterDockActiveAsync()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            return setting != null && setting.Value == "1";
        }

        private async Task<bool> IsSkipLeaderVerificationActiveAsync()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            return setting != null && setting.Value == "1";
        }

        private async Task RecalculateScheduleTotals(int scheduleId)
        {
            bool autoEnterDockActive = await IsAutoEnterDockActiveAsync();
            bool skipLeaderVerifActive = await IsSkipLeaderVerificationActiveAsync();
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule != null)
            {
                schedule.TotalTargetQuantity = schedule.DeliveryItems.Sum(di => di.Quantity);
                schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);

                bool allItemsDone = schedule.DeliveryItems.Any() &&
                                    schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);

                if (allItemsDone)
                {
                    var now = DateTime.Now;
                    bool anyDockInSet = false;
                    // Load seluruh group (Cycle+Route+Area+Date) untuk set ActualEnterDockTime bersama
                    var groupSchedules = await _context.DeliverySchedules
                        .Include(s => s.DeliveryItems)
                        .Where(s => s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                                    s.Cycle == schedule.Cycle &&
                                    s.Route == schedule.Route &&
                                    s.Area == schedule.Area &&
                                    s.Status != "Cancelled")
                        .ToListAsync();

                    // Cek apakah SEMUA schedule dalam group sudah kanban 100%
                    // Schedule tanpa item dianggap selesai (tidak memblokir group)
                    bool isGroupReady = groupSchedules.All(gs =>
                        !gs.DeliveryItems.Any() ||
                        gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity));

                    if (isGroupReady)
                    {
                        foreach (var gs in groupSchedules)
                        {
                            if (gs.PreparationStatus != "Prepared")
                            {
                                gs.PreparationStatus = "Prepared";
                                gs.ReadyToDockTime ??= now;
                            }

                            if (gs.ActualEndTime.HasValue)
                            {
                                gs.Status = "Completed";
                                gs.DriverStatus = "Completed";
                            }
                            else if (gs.Status != "In Progress")
                            {
                                gs.Status = "In Progress";
                            }

                            // Auto-set ActualEnterDockTime HANYA jika mode otomatis Enter Dock aktif DAN Skip Leader aktif.
                            if (autoEnterDockActive && skipLeaderVerifActive && !gs.ActualEnterDockTime.HasValue)
                            {
                                gs.ActualEnterDockTime = now;
                                gs.DriverStatus = "In Progress";
                                anyDockInSet = true;
                            }
                        }

                        await _context.SaveChangesAsync();

                        // Broadcast sesuai hasil: enterDock saat auto mode ON, readyToDock saat OFF.
                        await _deliveryHubContext.Clients.All.SendAsync("deliveryUpdated", new {
                            action = anyDockInSet ? "enterDock" : "readyToDock",
                            scheduleNumber = schedule.ScheduleNumber
                        });
                        await _stockHubContext.Clients.All.SendAsync("updateStock");
                        return;
                    }
                    else if (schedule.PreparationStatus != "Prepared")
                    {
                        schedule.PreparationStatus = "Prepared";
                        schedule.ReadyToDockTime ??= now;
                    }
                }
                else
                {
                    // Kanban tidak lagi terpenuhi (ada item yang dikurangi/direset)
                    // Batalkan departure driver agar jadwal bisa dilengkapi kembali
                    if (schedule.PreparationStatus == "Prepared" && !schedule.ActualEnterDockTime.HasValue)
                    {
                        schedule.PreparationStatus = "In Progress";
                        schedule.ReadyToDockTime = null;
                    }
                    if (schedule.ActualEndTime.HasValue)
                    {
                        schedule.ActualEndTime = null;
                        schedule.DriverStatus = schedule.ActualEnterDockTime.HasValue ? "In Progress" : null;
                    }
                }

                await _context.SaveChangesAsync();
            }
        }

        // GET: DeliveryItems/Index/5 (by ScheduleId)
        public async Task<IActionResult> Index(int? scheduleId)
        {
            if (scheduleId == null)
            {
                return RedirectToAction("Index", "DeliverySchedules");
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null)
            {
                return NotFound();
            }

            ViewBag.Schedule = schedule;

            var items = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.Item)
                .Where(di => di.ScheduleId == scheduleId)
                .ToListAsync();

            return View(items);
        }

        // GET: DeliveryItems/Create
        public async Task<IActionResult> Create(int? scheduleId)
        {
            if (scheduleId == null)
            {
                return RedirectToAction("Index", "DeliverySchedules");
            }

            var schedule = await _context.DeliverySchedules.FindAsync(scheduleId);
            if (schedule == null)
            {
                return NotFound();
            }

            ViewBag.Schedule = schedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName");
            
            var model = new DeliveryItem
            {
                ScheduleId = scheduleId.Value
            };

            return View(model);
        }

        // POST: DeliveryItems/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("DeliveryItemId,ScheduleId,ItemId,Quantity,ActualQuantity,Unit,TotalWeight,TotalVolume,Notes,IsCompleted")] DeliveryItem deliveryItem)
        {
            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedule");
            ModelState.Remove("Item");

            if (ModelState.IsValid)
            {
                deliveryItem.CreatedDate = DateTime.Now;
                _context.Add(deliveryItem);
                await _context.SaveChangesAsync();
                
                await RecalculateScheduleTotals(deliveryItem.ScheduleId);

                TempData["SuccessMessage"] = "Item delivery berhasil ditambahkan!";
                return RedirectToAction(nameof(Index), new { scheduleId = deliveryItem.ScheduleId });
            }
            
            var schedule = await _context.DeliverySchedules.FindAsync(deliveryItem.ScheduleId);
            ViewBag.Schedule = schedule;
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // GET: DeliveryItems/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var deliveryItem = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.DeliverySchedule)
                .Include(di => di.Item)
                .FirstOrDefaultAsync(di => di.DeliveryItemId == id);

            if (deliveryItem == null)
            {
                return NotFound();
            }

            var qtyLot = (deliveryItem.Item?.QtyLot > 0) ? deliveryItem.Item!.QtyLot!.Value : 1;
            ViewBag.Schedule = deliveryItem.DeliverySchedule;
            ViewBag.QtyLot   = qtyLot;
            ViewBag.KbnTarget = (int)Math.Ceiling((double)deliveryItem.Quantity / qtyLot);
            ViewBag.KbnActual = (int)Math.Ceiling((double)(deliveryItem.ActualQuantity ?? 0) / qtyLot);
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // POST: DeliveryItems/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("DeliveryItemId,ScheduleId,ItemId,Quantity,ActualQuantity,Unit,TotalWeight,TotalVolume,Notes,IsCompleted,CreatedDate")] DeliveryItem deliveryItem,
            int? kanbanActualInput = null)
        {
            if (id != deliveryItem.DeliveryItemId)
            {
                return NotFound();
            }

            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedule");
            ModelState.Remove("Item");

            List<PreparationRecord>? existingRecords = null;

            // Jika kanbanActualInput dikirim, konversi KBN → Qty menggunakan QtyLot dari DB
            if (kanbanActualInput.HasValue && kanbanActualInput.Value >= 0)
            {
                var itemRef = await _context.Items.FindAsync(deliveryItem.ItemId);
                var qtyLot  = (itemRef?.QtyLot > 0) ? itemRef.QtyLot!.Value : 1;
                deliveryItem.ActualQuantity = kanbanActualInput.Value * qtyLot;
                // Hapus error validasi untuk ActualQuantity (kita override dari KBN)
                ModelState.Remove("ActualQuantity");

                // Sinkronkan PreparationRecords: hapus record berlebih agar ExecuteSave
                // tidak menghitung ulang dari record lama dan membalikkan nilai yang baru di-edit.
                var tagCandidates = new List<string>();
                if (!string.IsNullOrEmpty(itemRef?.VIN))                tagCandidates.Add(itemRef.VIN);
                if (!string.IsNullOrEmpty(itemRef?.ItemCode))           tagCandidates.Add(itemRef.ItemCode);
                if (!string.IsNullOrEmpty(itemRef?.CustomerPartNumber)) tagCandidates.Add(itemRef.CustomerPartNumber);

                if (tagCandidates.Any())
                {
                    var parentSchedule = await _context.DeliverySchedules
                        .Include(s => s.Customer)
                        .FirstOrDefaultAsync(s => s.ScheduleId == deliveryItem.ScheduleId);
                    var scheduleNumber = parentSchedule?.ScheduleNumber;

                    int? parentCustomerId = parentSchedule?.CustomerId;
                    string? parentCycle = parentSchedule?.Cycle?.Trim();

                    existingRecords = await _context.PreparationRecords
                        .Where(p => (p.ScheduleId == deliveryItem.ScheduleId ||
                                     (p.ScheduleId == null && 
                                      !string.IsNullOrEmpty(p.ManifestNumber) && 
                                      p.ManifestNumber == scheduleNumber &&
                                      (!p.DockCustomerId.HasValue || p.DockCustomerId.Value == parentCustomerId) &&
                                      (string.IsNullOrEmpty(p.DockCycle) || p.DockCycle == "-" || 
                                       (!string.IsNullOrEmpty(parentCycle) && parentCycle != "-" && p.DockCycle.Trim() == parentCycle)))) &&
                                    tagCandidates.Contains(p.Tag) &&
                                    p.Remark == "Match")
                        .OrderByDescending(p => p.PreparationId)
                        .ToListAsync();

                    // Link any pending records to this schedule
                    var pendingRecords = existingRecords.Where(p => p.ScheduleId == null).ToList();
                    if (pendingRecords.Any())
                    {
                        foreach (var pr in pendingRecords)
                        {
                            pr.ScheduleId = deliveryItem.ScheduleId;
                        }
                    }

                    if (existingRecords.Count > kanbanActualInput.Value)
                    {
                        // Hapus record paling baru sampai jumlah tersisa = kanbanActualInput
                        var toDelete = existingRecords
                            .Take(existingRecords.Count - kanbanActualInput.Value)
                            .ToList();
                        _context.PreparationRecords.RemoveRange(toDelete);
                    }
                    else if (existingRecords.Count < kanbanActualInput.Value)
                    {
                        // Edit manual menaikkan kanban: buat synthetic scan records agar
                        // Recalc KBN menghitung jumlah yang sama dan tidak membalikkan nilai.
                        // Tag pakai normalized VIN agar cocok dengan query RecalculateActualQty
                        // dan SyncAllPendingAsync (keduanya pakai VinHelper.Normalize).
                        var normalizedTag = VinHelper.Normalize(itemRef?.VIN ?? tagCandidates.First());
                        int toAdd = kanbanActualInput.Value - existingRecords.Count;
                        for (int i = 0; i < toAdd; i++)
                        {
                            await _context.PreparationRecords.AddAsync(new PreparationRecord
                            {
                                Tag         = normalizedTag,
                                Label       = "MANUAL",
                                Kanban      = "MANUAL",
                                Remark      = "Match",
                                ScheduleId  = deliveryItem.ScheduleId,
                                CreatedDate = DateTime.Now,
                                CreatedBy   = HttpContext.Session.GetString("FullName") ?? HttpContext.Session.GetString("Username") ?? "ManualEdit",
                                TargetDate  = parentSchedule?.ScheduledDate,
                                DockCustomerId = parentSchedule?.CustomerId,
                                DockName    = parentSchedule?.Customer?.CustomerName,
                                DockCycle   = parentSchedule?.Cycle,
                                ManifestNumber = parentSchedule?.ScheduleNumber
                            });
                        }
                    }
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    deliveryItem.UpdatedDate = DateTime.Now;
                    // Sinkronkan IsCompleted berdasarkan actual vs target
                    deliveryItem.IsCompleted = (deliveryItem.ActualQuantity ?? 0) >= deliveryItem.Quantity;
                    if (deliveryItem.ActualQuantity > 0 && !deliveryItem.PrepScanTime.HasValue)
                    {
                        var firstRealScanTime = existingRecords?.Where(r => r.Kanban != "MANUAL").Select(r => (DateTime?)r.CreatedDate).Min();
                        deliveryItem.PrepScanTime = firstRealScanTime ?? DateTime.Now;
                    }
                    _context.Update(deliveryItem);
                    await _context.SaveChangesAsync();

                    await RecalculateScheduleTotals(deliveryItem.ScheduleId);

                    TempData["SuccessMessage"] = "Item delivery berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DeliveryItemExists(deliveryItem.DeliveryItemId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                // Redirect ke Details schedule (bukan Index DeliveryItems yang tidak punya view)
                return RedirectToAction("Details", "DeliverySchedules", new { id = deliveryItem.ScheduleId }, $"item-{deliveryItem.DeliveryItemId}");
            }

            var schedule = await _context.DeliverySchedules.FindAsync(deliveryItem.ScheduleId);
            var itemForView = await _context.Items.FindAsync(deliveryItem.ItemId);
            var qtyLotView = (itemForView?.QtyLot > 0) ? itemForView.QtyLot!.Value : 1;
            ViewBag.Schedule  = schedule;
            ViewBag.QtyLot    = qtyLotView;
            ViewBag.KbnTarget = (int)Math.Ceiling((double)deliveryItem.Quantity / qtyLotView);
            ViewBag.KbnActual = (int)Math.Ceiling((double)(deliveryItem.ActualQuantity ?? 0) / qtyLotView);
            ViewData["ItemId"] = new SelectList(_context.Items.Where(i => i.IsActive), "ItemId", "ItemName", deliveryItem.ItemId);
            return View(deliveryItem);
        }

        // GET: DeliveryItems/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var deliveryItem = await _context.DeliveryItems
                .AsNoTracking()
                .Include(di => di.DeliverySchedule)
                .Include(di => di.Item)
                .FirstOrDefaultAsync(m => m.DeliveryItemId == id);

            if (deliveryItem == null)
            {
                return NotFound();
            }

            return View(deliveryItem);
        }

        // POST: DeliveryItems/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var deliveryItem = await _context.DeliveryItems.FindAsync(id);
            int scheduleId = 0;
            
            if (deliveryItem != null)
            {
                scheduleId = deliveryItem.ScheduleId;
                _context.DeliveryItems.Remove(deliveryItem);
                await _context.SaveChangesAsync();
                
                await RecalculateScheduleTotals(scheduleId);

                TempData["SuccessMessage"] = "Item delivery berhasil dihapus!";
            }

            return RedirectToAction(nameof(Index), new { scheduleId });
        }

        private bool DeliveryItemExists(int id)
        {
            return _context.DeliveryItems.Any(e => e.DeliveryItemId == id);
        }
    }
}

