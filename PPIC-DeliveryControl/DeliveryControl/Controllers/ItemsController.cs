using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Filters;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    [AuthorizeAdmin]
    public class ItemsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly StockCacheService _stockCache;
        private readonly IMemoryCache _memoryCache;

        public ItemsController(ApplicationDbContext context, StockCacheService stockCache, IMemoryCache memoryCache)
        {
            _context = context;
            _stockCache = stockCache;
            _memoryCache = memoryCache;
        }

        private void InvalidateDropdownCache()
        {
            _memoryCache.Remove("Dropdown_Categories");
            _memoryCache.Remove("Dropdown_Plants");
            _memoryCache.Remove("Dropdown_Racks");
        }

        // GET: Items
        public async Task<IActionResult> Index(string searchString, string category, string plant, string rack, int pageNumber = 1)
        {
            var query = _context.Items
                .AsNoTracking() // Performance for high data
                .AsQueryable();
            ViewData["CurrentCategory"] = category;

            var categories = await _memoryCache.GetOrCreateAsync("Dropdown_Categories", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                return await _context.Items
                    .AsNoTracking()
                    .Where(i => !string.IsNullOrEmpty(i.Category))
                    .Select(i => i.Category)
                    .Distinct()
                    .ToListAsync();
            });
            ViewData["Categories"] = categories;

            var items = _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted);

            var plants = await _memoryCache.GetOrCreateAsync("Dropdown_Plants", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                return await _context.Items
                    .AsNoTracking()
                    .Where(i => !string.IsNullOrEmpty(i.Plant))
                    .Select(i => i.Plant)
                    .Distinct()
                    .ToListAsync();
            });
            ViewData["Plants"] = plants;

            var racks = await _memoryCache.GetOrCreateAsync("Dropdown_Racks", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                return await _context.Items
                    .AsNoTracking()
                    .Where(i => !string.IsNullOrEmpty(i.Rack))
                    .Select(i => i.Rack)
                    .Distinct()
                    .ToListAsync();
            });
            ViewData["Racks"] = racks;

            if (!String.IsNullOrEmpty(searchString))
            {
                items = items.Where(i => i.ItemCode.Contains(searchString)
                                   || i.ItemName.Contains(searchString)
                                   || (i.VIN ?? "").Contains(searchString)
                                   || (i.CustomerPartNumber ?? "").Contains(searchString)
                                   || (i.Customer ?? "").Contains(searchString)
                                   || (i.Plant ?? "").Contains(searchString)
                                   || (i.Rack ?? "").Contains(searchString));
            }

            if (!String.IsNullOrEmpty(category))
            {
                items = items.Where(i => i.Category == category);
            }

            if (!String.IsNullOrEmpty(plant))
            {
                items = items.Where(i => i.Plant == plant);
            }

            if (!String.IsNullOrEmpty(rack))
            {
                items = items.Where(i => i.Rack == rack);
            }

            var totalItems = await items.CountAsync();
            int pageSize = 20;

            // Get all items for the page, ordered by VIN then Rack so grouping in view works correctly
            var resultItems = await items
                .OrderBy(i => i.VIN)
                .ThenBy(i => i.Rack)
                .ThenBy(i => i.NoRack)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.SearchString = searchString ?? "";
            ViewBag.CategoryFilter = category ?? "";
            ViewBag.RouteData = new Dictionary<string, string> { 
                { "searchString", searchString ?? "" },
                { "category", category ?? "" },
                { "plant", plant ?? "" },
                { "rack", rack ?? "" }
            };

            return View(resultItems);
        }

        // GET: Items/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ItemId == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        public IActionResult Create()
        {
            PrepareViewBags();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,StatusItem,Plant,Rack,NoRack,RackAlt,NoRackAlt,RackAltCapacity,QtyLot,RackMin,RackMax,Customer,VIN,ROP,CustomerPartNumber,KanbanType")] Item item)
        {
            if (ModelState.IsValid)
            {
                item.IsActive = !string.Equals(item.StatusItem, "Tidak Aktif", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(item.StatusItem)) item.StatusItem = "Reguler";

                // Cek item code sudah ada
                if (await _context.Items.AsNoTracking().AnyAsync(i => i.ItemCode == item.ItemCode))
                {
                    ModelState.AddModelError("ItemCode", "Item Code sudah digunakan. Silakan gunakan kode lain.");
                    PrepareViewBags();
                    return View(item);
                }

                item.CreatedDate = DateTime.Now;
                _context.Add(item);
                await _context.SaveChangesAsync();
                InvalidateDropdownCache();
                TempData["SuccessMessage"] = "Item berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
            return View(item);
        }

        // GET: Items/Edit/5
        public async Task<IActionResult> Edit(int? id, int returnPage = 1, string returnSearch = "", string returnCategory = "")
        {
            if (id == null) return NotFound();

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemId == id);

            if (item == null) return NotFound();

            // Load sibling rack rows (same VIN, different ItemId)
            if (!string.IsNullOrWhiteSpace(item.VIN))
            {
                var siblings = await _context.Items
                    .AsNoTracking()
                    .Where(i => i.VIN == item.VIN && i.ItemId != id)
                    .OrderBy(i => i.Rack).ThenBy(i => i.NoRack)
                    .ToListAsync();
                ViewBag.SiblingRacks = siblings;
            }
            else
            {
                ViewBag.SiblingRacks = new List<Item>();
            }

            ViewBag.ReturnPage = returnPage;
            ViewBag.ReturnSearch = returnSearch;
            ViewBag.ReturnCategory = returnCategory;
            PrepareViewBags();
            return View(item);
        }

        // POST: Items/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id,
            [Bind("ItemId,ItemCode,ItemName,Description,Unit,Category,Weight,Volume,MinStock,MaxStock,IsActive,StatusItem,Plant,Rack,NoRack,RackAlt,NoRackAlt,RackAltCapacity,QtyLot,RackMin,RackMax,Customer,VIN,ROP,CustomerPartNumber,KanbanType")] Item item,
            [FromForm] string? rackLocationsJson,
            int returnPage = 1, string returnSearch = "", string returnCategory = "")
        {
            if (id != item.ItemId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    // Parse rack locations from JSON
                    var rackLocs = new List<RackLocationDto>();
                    if (!string.IsNullOrWhiteSpace(rackLocationsJson))
                    {
                        try
                        {
                            rackLocs = System.Text.Json.JsonSerializer.Deserialize<List<RackLocationDto>>(
                                rackLocationsJson,
                                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                                ?? new List<RackLocationDto>();
                        }
                        catch { /* fallback: use item.Rack/NoRack */ }
                    }

                    // If no rack locations provided, use the item's current Rack/NoRack
                    if (rackLocs.Count == 0)
                    {
                        rackLocs.Add(new RackLocationDto { Rack = item.Rack ?? "", NoRack = item.NoRack ?? 0 });
                    }

                    // Load all existing rows for this VIN
                    var allVinRows = await _context.Items
                        .Where(i => i.VIN == item.VIN)
                        .OrderBy(i => i.ItemId)
                        .ToListAsync();

                    // Sync: match new locs to existing rows
                    var now = DateTime.Now;
                    var usedIds = new HashSet<int>();

                    foreach (var loc in rackLocs)
                    {
                        // Find existing row matching this Rack/NoRack
                        var existing = allVinRows.FirstOrDefault(r =>
                            r.Rack == loc.Rack && r.NoRack == loc.NoRack && !usedIds.Contains(r.ItemId));

                        if (existing != null)
                        {
                            usedIds.Add(existing.ItemId);
                            // Update shared fields
                            existing.ItemCode = (existing.ItemId == id) ? item.ItemCode : existing.ItemCode;
                            existing.ItemName = item.ItemName;
                            existing.Description = item.Description;
                            existing.Unit = item.Unit;
                            existing.Category = item.Category;
                            existing.Weight = item.Weight;
                            existing.Volume = item.Volume;
                            existing.MinStock = item.MinStock;
                            existing.MaxStock = item.MaxStock;
                            existing.StatusItem = string.IsNullOrWhiteSpace(item.StatusItem) ? "Reguler" : item.StatusItem;
                            existing.IsActive = !string.Equals(existing.StatusItem, "Tidak Aktif", StringComparison.OrdinalIgnoreCase);
                            existing.Plant = item.Plant;
                            existing.RackAlt = item.RackAlt;
                            existing.NoRackAlt = item.NoRackAlt;
                            existing.RackAltCapacity = item.RackAltCapacity;
                            existing.QtyLot = item.QtyLot;
                            existing.RackMin = item.RackMin;
                            existing.RackMax = item.RackMax;
                            existing.ROP = item.ROP;
                            existing.Customer = item.Customer;
                            existing.VIN = item.VIN;
                            existing.CustomerPartNumber = item.CustomerPartNumber;
                            existing.KanbanType = item.KanbanType;
                            existing.UpdatedDate = now;
                        }
                        else
                        {
                            // Create new sibling row (clone of primary item)
                            var newRow = new Item
                            {
                                ItemCode = item.ItemCode + "_" + loc.Rack + loc.NoRack,
                                ItemName = item.ItemName,
                                Description = item.Description,
                                Unit = item.Unit,
                                Category = item.Category,
                                Weight = item.Weight,
                                Volume = item.Volume,
                                MinStock = item.MinStock,
                                MaxStock = item.MaxStock,
                                StatusItem = string.IsNullOrWhiteSpace(item.StatusItem) ? "Reguler" : item.StatusItem,
                                IsActive = !string.Equals(item.StatusItem, "Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                                Plant = item.Plant,
                                Rack = loc.Rack,
                                NoRack = loc.NoRack,
                                RackAlt = item.RackAlt,
                                NoRackAlt = item.NoRackAlt,
                                RackAltCapacity = item.RackAltCapacity,
                                QtyLot = item.QtyLot,
                                RackMin = item.RackMin,
                                RackMax = item.RackMax,
                                ROP = item.ROP,
                                Customer = item.Customer,
                                VIN = item.VIN,
                                CustomerPartNumber = item.CustomerPartNumber,
                                KanbanType = item.KanbanType,
                                CreatedDate = now,
                                UpdatedDate = now
                            };
                            _context.Items.Add(newRow);
                        }
                    }

                    // Delete rows no longer in the list (but keep the primary item's row)
                    foreach (var row in allVinRows)
                    {
                        if (!usedIds.Contains(row.ItemId))
                        {
                            _context.Items.Remove(row);
                        }
                    }

                    // Also update the primary item's own Rack from the first matching loc
                    var primaryRow = allVinRows.FirstOrDefault(r => r.ItemId == id);
                    if (primaryRow != null && !usedIds.Contains(id))
                    {
                        // primary was removed (its rack was deleted) — redirect to Index
                        await _context.SaveChangesAsync();
                        InvalidateDropdownCache();
                        TempData["SuccessMessage"] = "Item berhasil diperbarui!";
                        return RedirectToAction(nameof(Index), new { pageNumber = returnPage, searchString = returnSearch, category = returnCategory });
                    }

                    await _context.SaveChangesAsync();
                    InvalidateDropdownCache();
                    TempData["SuccessMessage"] = "Item berhasil diperbarui!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ItemExists(item.ItemId))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index), new { pageNumber = returnPage, searchString = returnSearch, category = returnCategory });
            }

            // Reload siblings for validation failure re-render
            if (!string.IsNullOrWhiteSpace(item.VIN))
            {
                var siblings = await _context.Items.AsNoTracking()
                    .Where(i => i.VIN == item.VIN && i.ItemId != id)
                    .OrderBy(i => i.Rack).ThenBy(i => i.NoRack).ToListAsync();
                ViewBag.SiblingRacks = siblings;
            }
            else
            {
                ViewBag.SiblingRacks = new List<Item>();
            }

            PrepareViewBags();
            return View(item);
        }

        private void PrepareViewBags()
        {
            ViewBag.Plants = new List<string>();
            ViewBag.Units = new List<string> { "PCS", "KG", "BOX" };
            ViewBag.Racks = new List<string>();
        }

        private class RackLocationDto
        {
            public string Rack { get; set; } = "";
            public int NoRack { get; set; }
            public string? Plant { get; set; }
        }


        // GET: Items/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _context.Items
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ItemId == id);
            
            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        // POST: Items/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.Items.FirstOrDefaultAsync(i => i.ItemId == id);

            if (item != null)
            {
                try
                {
                    // SOFT DELETE: tandai IsDeleted=true agar PullingRecord tetap bisa resolve item
                    // Data Dashboard FG (PullingRecords) & Log Transaksi TIDAK akan hilang
                    item.IsDeleted = true;
                    item.UpdatedDate = DateTime.Now;
                    await _context.SaveChangesAsync();
                    InvalidateDropdownCache();
                    TempData["SuccessMessage"] = "Item berhasil dihapus! Data transaksi (Pulling, Preparation, Dashboard FG) tetap tersimpan.";
                }
                catch (DbUpdateException ex)
                {
                    TempData["ErrorMessage"] = $"Error saat menghapus item: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item != null)
            {
                item.IsActive = !item.IsActive;
                item.StatusItem = item.IsActive ? "Reguler" : "Tidak Aktif";
                item.UpdatedDate = DateTime.Now;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Item {item.ItemCode} berhasil {(item.IsActive ? "diaktifkan (Reguler)" : "dinonaktifkan (Tidak Aktif)")}!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ItemExists(int id)
        {
            return _context.Items.Any(e => e.ItemId == id);
        }

        // Bulk Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada item yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',').Select(int.Parse).ToList();
            var itemsToDelete = await _context.Items
                .Where(i => ids.Contains(i.ItemId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var item in itemsToDelete)
            {
                try
                {
                    // SOFT DELETE: tandai IsDeleted=true agar data Dashboard FG tidak rusak
                    item.IsDeleted = true;
                    item.UpdatedDate = DateTime.Now;
                    successCount++;
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{item.ItemCode}: {ex.Message}");
                    errorCount++;
                }
            }

            if (successCount > 0)
            {
                await _context.SaveChangesAsync();
                InvalidateDropdownCache();
                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {successCount} item!";
            }

            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = $"⚠️ {errorCount} item gagal dihapus: {string.Join(", ", errorMessages.Take(5))}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Bulk Delete All Items & Related Transactions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteAll()
        {
            try
            {
                // SOFT DELETE SEMUA: set IsDeleted=true agar data PullingRecord/PreparationRecord
                // tetap bisa di-resolve di Dashboard FG dan Log Transaksi
                var allItems = await _context.Items.Where(i => !i.IsDeleted).ToListAsync();
                foreach (var item in allItems)
                {
                    item.IsDeleted = true;
                    item.UpdatedDate = DateTime.Now;
                }
                await _context.SaveChangesAsync();
                InvalidateDropdownCache();

                TempData["SuccessMessage"] = "✅ Seluruh data master item berhasil dihapus! Data transaksi (Pulling, Preparation, Delivery, Dashboard FG) tetap tersimpan.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "❌ Gagal menghapus data master item: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Item");

                // Headers: NO | LOKASI RACK | PROD. PLANT | RAK | NO RAK | CUST | STATUS | PROD. | VIN | QPC | MIN 1D
                // ROP 2D dan MAX 3D DIHAPUS dari template — dihitung otomatis saat upload
                worksheet.Cell(1, 1).Value = "NO";
                worksheet.Cell(1, 2).Value = "LOKASI RACK";
                worksheet.Cell(1, 3).Value = "PROD. PLANT";
                worksheet.Cell(1, 4).Value = "RAK";
                worksheet.Cell(1, 5).Value = "NO RAK";
                worksheet.Cell(1, 6).Value = "CUST";
                worksheet.Cell(1, 7).Value = "STATUS";
                worksheet.Cell(1, 8).Value = "PROD.";
                worksheet.Cell(1, 9).Value = "VIN";
                worksheet.Cell(1, 10).Value = "QPC";
                worksheet.Cell(1, 11).Value = "MIN 0,5D";

                // Style header (11 kolom)
                var headerRange = worksheet.Range(1, 1, 1, 11);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f172a");
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                // Contoh data
                worksheet.Cell(2, 1).Value = 1;
                worksheet.Cell(2, 2).Value = "RVI";
                worksheet.Cell(2, 3).Value = "Molded";
                worksheet.Cell(2, 4).Value = "X";
                worksheet.Cell(2, 5).Value = "";
                worksheet.Cell(2, 6).Value = "PASI EXP";
                worksheet.Cell(2, 7).Value = "Aktif";
                worksheet.Cell(2, 8).Value = "HBR";
                worksheet.Cell(2, 9).Value = "VIN001";
                worksheet.Cell(2, 10).Value = 12;
                worksheet.Cell(2, 11).Value = 5;

                // Baris note: ROP 1.5D dan MAX 2D dihitung otomatis
                var noteCell = worksheet.Cell(3, 1);
                noteCell.Value = "ℹ️ ROP 1.5D dan MAX 2D dihitung OTOMATIS oleh sistem saat upload  →  ROP 1.5D = MIN 1D × 2  |  MAX 2D = MIN 1D × 4";
                noteCell.Style.Font.Italic = true;
                noteCell.Style.Font.Bold = false;
                noteCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#b45309");
                noteCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#fef3c7");
                worksheet.Range(3, 1, 3, 11).Merge();
                worksheet.Range(3, 1, 3, 11).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Item.xlsx");
                }
            }
        }

        // EXPORT EXCEL — Semua master item, multi-rak di-expand jadi baris terpisah
        public async Task<IActionResult> ExportExcel()
        {
            // Sertakan RackLocations agar multi-rak bisa di-expand
            var items = await _context.Items
                .AsNoTracking()
                .Where(i => !i.IsDeleted)
                .Include(i => i.RackLocations)
                .OrderBy(i => i.Plant).ThenBy(i => i.VIN)
                .ToListAsync();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("Master Item");

                // Headers
                var headers = new string[] { "No", "Lokasi Rack", "Prod. Plant", "Rak", "No Rak", "Cust", "Status", "Prod.", "VIN", "QPC", "Min 1D", "ROP 2D", "Max 3D", "Customer Part No", "Kanban Type" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var hCell = ws.Cell(1, i + 1);
                    hCell.Value = headers[i];
                    hCell.Style.Font.Bold = true;
                    hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f172a");
                    hCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    hCell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    hCell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                }

                int row = 2;
                int no  = 1;
                foreach (var item in items)
                {
                    // GROUPED: satu baris per VIN/item, gabungkan semua lokasi rak
                    var rackRows = item.RackLocations != null && item.RackLocations.Count > 0
                        ? item.RackLocations.OrderBy(r => r.Rack).ThenBy(r => r.NoRack).ToList()
                        : new List<ItemRackLocation>
                          {
                              new ItemRackLocation
                              {
                                  Rack   = item.Rack ?? "",
                                  NoRack = item.NoRack ?? 0,
                                  Plant  = item.Plant
                              }
                          };

                    // Ambil Lokasi Rack (Plant dari RackLocation, e.g. V3) dari baris pertama
                    var primaryLoc = rackRows.First();
                    var lokasiRack = primaryLoc.Plant ?? item.Plant ?? "-";

                    // Gabungkan RAK: jika semua sama cukup satu huruf, jika beda pakai koma
                    var uniqueRacks = rackRows.Select(r => (r.Rack ?? "").Trim().ToUpper())
                                              .Where(r => r != "")
                                              .Distinct().OrderBy(r => r).ToList();
                    var rakCombined = uniqueRacks.Count > 0 ? string.Join(",", uniqueRacks) : "-";

                    // Gabungkan NO RAK dengan koma, grouped per huruf rak
                    // Format: jika semua rak sama → "20,21,22"; jika beda → "B:20,21 | E:5,6"
                    string noRakCombined;
                    if (uniqueRacks.Count <= 1)
                    {
                        var nos = rackRows.Select(r => r.NoRack).Where(n => n > 0)
                                         .Distinct().OrderBy(n => n)
                                         .Select(n => n.ToString()).ToList();
                        noRakCombined = nos.Count > 0 ? string.Join(",", nos) : "";
                    }
                    else
                    {
                        // Rak berbeda: format "B:20,21 | E:5,6"
                        var groups = rackRows.GroupBy(r => (r.Rack ?? "").Trim().ToUpper())
                                             .OrderBy(g => g.Key);
                        noRakCombined = string.Join(" | ", groups.Select(g =>
                            g.Key + ":" + string.Join(",",
                                g.Select(r => r.NoRack).Where(n => n > 0)
                                 .Distinct().OrderBy(n => n))));
                    }

                    bool isEven = (no % 2 == 0);
                    
                    ws.Cell(row, 1).Value  = no;
                    ws.Cell(row, 2).Value  = item.ItemName;
                    ws.Cell(row, 3).Value  = lokasiRack;
                    ws.Cell(row, 4).Value  = rakCombined;
                    ws.Cell(row, 5).Value  = noRakCombined;
                    ws.Cell(row, 6).Value  = item.Customer ?? "-";
                    ws.Cell(row, 7).Value  = item.StatusItem ?? (item.IsActive ? "Reguler" : "Tidak Aktif");
                    ws.Cell(row, 8).Value  = item.Category ?? "-";
                    ws.Cell(row, 9).Value  = item.VIN ?? "-";
                    ws.Cell(row, 10).Value = item.QtyLot;
                    ws.Cell(row, 11).Value = item.RackMin;
                    ws.Cell(row, 12).Value = item.ROP;
                    ws.Cell(row, 13).Value = item.RackMax;
                    ws.Cell(row, 14).Value = item.CustomerPartNumber ?? "-";
                    ws.Cell(row, 15).Value = item.KanbanType ?? "-";

                    if (isEven)
                        ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f8fafc");

                    row++;
                    no++;
                }

                ws.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    var dateStr = DateTime.Today.ToString("dd-MM-yyyy");
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"MasterItem_{dateStr}.xlsx");
                }
            }
        }

        // Import Excel
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file, string? returnUrl = null)
        {
            IActionResult RedirectToLocalOrIndex()
        {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction(nameof(Index));
            }

            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak ditemukan!";
                return RedirectToLocalOrIndex();
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToLocalOrIndex();
            }

            int successCount = 0;
            int errorCount = 0;
            var errorSamples = new List<string>();

            try
            {
                // 1. Pre-load ALL existing items — keyed by VIN|Rack|NoRack (one Item per rack location)
                var existingItems = await _context.Items.ToListAsync();

                // Customer|VIN|Rack|NoRack → Item (exact rack-level lookup)
                var itemByVinRack = existingItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => $"{NormalizeKey(i.Customer)}|{NormalizeKey(i.VIN!)}|{i.Rack?.Trim().ToUpper() ?? ""}|{i.NoRack ?? 0}")
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                // Customer|VIN → primary Item (lowest ItemId, used for master data reference)
                var itemByVin = existingItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => $"{NormalizeKey(i.Customer)}|{NormalizeKey(i.VIN!)}")
                    .ToDictionary(g => g.Key, g => g.OrderBy(i => i.ItemId).First());

                // Track (VIN|Rack|NoRack) added in this import batch (avoid duplicates within Excel)
                var batchRackSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // Track VINs counted for new/updated statistics
                var countedVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        
                        // --- v7.0 COMPREHENSIVE HEADER MAPPING ---
                        var targetKeywords = new[] { "VIN", "LOKASI", "RACK", "RAK", "CUST", "PLANT", "QPC", "MIN", "ROP", "MAX", "PROD", "STATUS" };
                        var headerRow = worksheet.Row(1);
                        int bestScore = -1;

                        for (int r = 1; r <= 30; r++) {
                            var testRow = worksheet.Row(r);
                            int currentScore = 0;
                            for (int c = 1; c <= 30; c++) {
                                var val = NormalizeHeader(GetSafeString(testRow.Cell(c)));
                                if (string.IsNullOrEmpty(val)) continue;
                                foreach (var k in targetKeywords) if (val.Contains(NormalizeHeader(k))) currentScore++;
                            }
                            if (currentScore >= 6) { headerRow = testRow; break; }
                            if (currentScore > bestScore && currentScore >= 3) { bestScore = currentScore; headerRow = testRow; }
                        }

                        var cleanedHeaders = new Dictionary<string, int>();
                        for (int col = 1; col <= worksheet.LastColumnUsed().ColumnNumber(); col++) {
                            var cleaned = NormalizeHeader(GetSafeString(headerRow.Cell(col)));
                            if (!string.IsNullOrEmpty(cleaned) && !cleanedHeaders.ContainsKey(cleaned)) cleanedHeaders.Add(cleaned, col);
                        }

                        int FindCol(params string[] keywords) {
                            // Phase 1: Exact normalized match (highest priority, no ambiguity)
                            foreach (var kw in keywords) {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanedHeaders.ContainsKey(cleanKw)) return cleanedHeaders[cleanKw];
                            }
                            // Phase 2: Fuzzy — only if keyword is long enough (>=5 chars) and is UNAMBIGUOUS
                            // (no other header key also contains this keyword)
                            foreach (var kw in keywords) {
                                var cleanKw = NormalizeHeader(kw);
                                if (cleanKw.Length < 5) continue; // too short = too ambiguous
                                var matches = cleanedHeaders.Keys.Where(k => k.Contains(cleanKw)).ToList();
                                if (matches.Count == 1) return cleanedHeaders[matches[0]]; // unique match only
                            }
                            return -1;
                        }
                        // Get Column Letter for Auditor Report
                        string GetColLetter(int colIndex) => colIndex != -1 ? worksheet.Column(colIndex).ColumnLetter() : "?";

                // Column Mapping — mendukung semua format header (export baru, export lama, custom)
                // Urutan keyword = prioritas: exact header dari export sistem didahulukan
                var colMap = new
                {
                    // VIN: kolom kode produk
                    Vin        = FindCol("VIN", "VIN INTERNAL", "INTERNAL", "KODE VIN"),

                    // LOKASI RACK: nama display item (V3, TANGO, RVI, dll)
                    ItemName   = FindCol("LOKASI RAK", "LOKASI RACK", "Lokasi Rack", "LOKASIRAK", "LOKASIACK",
                                         "NAMA ITEM", "NAMA BARANG", "ITEM NAME"),

                    // PROD. PLANT: plant produksi (Molded, Hose, RVI)
                    // ⚠️ Harus lebih spesifik dari "PROD." agar tidak tabrakan
                    Plant      = FindCol("PROD. PLANT", "PROD.Plant", "PRODPLANT",
                                         "PROD PLANT", "PLANT", "PABRIK"),

                    // RAK: huruf rak (A-K)
                    // ⚠️ Jangan pakai "LOKASI" sebagai fallback karena bertabrakan dengan LOKASI RACK
                    Rack       = FindCol("RAK", "Rak", "RACK", "HURUF RAK"),

                    // NO RAK: nomor kolom rak
                    NoRack     = FindCol("NO RAK", "No Rak", "NORAK", "NOMOR RAK", "NO. RAK"),

                    // CUST: kode customer
                    Customer   = FindCol("CUST", "CUSTOMER", "NAMA CUSTOMER", "PELANGGAN"),

                    // STATUS: Aktif / Tidak Aktif
                    StatusCol  = FindCol("STATUS", "STATUS ITEM", "IS ACTIVE", "AKTIF"),

                    // PROD.: kategori produk (Internal, HBR, dll)
                    // ⚠️ "PROD" (4 char) → hanya exact match, TIDAK fuzzy ke "PROD. PLANT"
                    CategoryCol= FindCol("PROD.", "PROD", "KATEGORI", "CATEGORY", "PRODUK"),

                    // QPC: qty per carton/kanban
                    Qpc        = FindCol("QPC", "QTY PER CARTON", "QTY LOT", "LOT SIZE", "LOT"),

                    // MIN 1D: minimum stock 1 hari
                    MinStock   = FindCol("MIN 0,5D", "MIN0.5D", "MIN STOCK", "MINIMUM"),

                    // MAX 3D: maximum stock 3 hari
                    MaxStock   = FindCol("MAX 2D", "MAX3D", "MAX STOCK", "MAXIMUM"),

                    // ROP 2D: reorder point 2 hari
                    RopStock   = FindCol("ROP 1D", "ROP2D", "ROP", "REORDER POINT"),

                    // Customer Part No & Kanban Type (optional)
                    CustPartNo = FindCol("CUSTOMER PART NO", "CUST PART NO", "PART NO", "CUSTOMERPARTNO"),
                    KanbanType = FindCol("KANBAN TYPE", "KANBANTYPE", "TIPE KANBAN"),
                };

                // Validasi Kolom Wajib (Hanya VIN yang wajib untuk Master Item)
                if (colMap.Vin == -1)
                {
                     TempData["ErrorMessage"] = "Kolom Wajib tidak ditemukan: VIN (INTERNAL). Pastikan format baru sesuai template";
                     return RedirectToLocalOrIndex();
                }

                        // --- SHERLOCK MODE (v7.0): Deep Diagnostics ---
                        var excelVins = new HashSet<string>();
                        var sampleVins = new List<string>();
                        var sampleMins = new List<string>();
                        
                        int rowProcessCount = 0;
                        int newItemCount = 0;       // VIN baru, item dibuat
                        int updatedItemCount = 0;   // VIN sudah ada di DB, item diupdate

                        // --- FIX v6.3: Ensure we start EXACTLY after the header row ---
                        var rows = worksheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber());
                        
                        // Variables for "Fill Down" (useful when cells are merged or left blank for subsequent rack locations)
                        string lastVin = "";
                        string lastPlant = "";
                        string lastCust = "";
                        string lastItemName = "";
                        string lastCategory = "";
                        string lastRak = "";
                        string lastQpc = "";
                        string lastMin = "";
                        string lastStatus = "";
                        string lastPartNo = "";
                        string lastKanban = "";

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;
                            rowProcessCount++;
                            try
                            {
                                string rawVin = GetSafeString(row, colMap.Vin);
                                string rawPlt = GetSafeString(row, colMap.Plant);
                                string rawCust = GetSafeString(row, colMap.Customer);
                                string rawItemName = GetSafeString(row, colMap.ItemName);
                                string rawCategory = GetSafeString(row, colMap.CategoryCol);
                                string rawRak = GetSafeString(row, colMap.Rack);
                                string rawQpc = GetSafeNumberString(row, colMap.Qpc);
                                string rawMin = GetSafeNumberString(row, colMap.MinStock);
                                string rawStatus = GetSafeString(row, colMap.StatusCol);
                                string rawPartNo = GetSafeString(row, colMap.CustPartNo);
                                string rawKanban = GetSafeString(row, colMap.KanbanType);

                                string vin = !string.IsNullOrWhiteSpace(rawVin) ? rawVin : lastVin;
                                string plt = !string.IsNullOrWhiteSpace(rawPlt) ? rawPlt : lastPlant;
                                string cust = !string.IsNullOrWhiteSpace(rawCust) ? rawCust : lastCust;
                                string itemName = !string.IsNullOrWhiteSpace(rawItemName) ? rawItemName : lastItemName;
                                string category = !string.IsNullOrWhiteSpace(rawCategory) ? rawCategory : lastCategory;

                                // Normalize Category to match Master Item standard (Subcon / Internal)
                                if (category.Equals("BESQ", StringComparison.OrdinalIgnoreCase)) {
                                    category = "Subcon";
                                } else if (category.Equals("ARPS", StringComparison.OrdinalIgnoreCase) || 
                                           category.Equals("BANSHU", StringComparison.OrdinalIgnoreCase) || 
                                           category.Equals("ASAHI", StringComparison.OrdinalIgnoreCase) || 
                                           category == "-" || category == "0" || string.IsNullOrWhiteSpace(category)) {
                                    category = "Internal";
                                }

                                string rak = !string.IsNullOrWhiteSpace(rawRak) ? rawRak : lastRak;
                                string qpc = !string.IsNullOrWhiteSpace(rawQpc) && rawQpc != "0" ? rawQpc : lastQpc;
                                string min = !string.IsNullOrWhiteSpace(rawMin) && rawMin != "0" ? rawMin : lastMin;
                                string status = !string.IsNullOrWhiteSpace(rawStatus) ? rawStatus : lastStatus;
                                string partNo = !string.IsNullOrWhiteSpace(rawPartNo) ? rawPartNo : lastPartNo;
                                string kanban = !string.IsNullOrWhiteSpace(rawKanban) ? rawKanban : lastKanban;

                                lastVin = vin;
                                lastPlant = plt;
                                lastCust = cust;
                                lastItemName = itemName;
                                lastCategory = category;
                                lastRak = rak;
                                lastQpc = qpc;
                                lastMin = min;
                                lastStatus = status;
                                lastPartNo = partNo;
                                lastKanban = kanban;

                                string nrk  = GetSafeString(row, colMap.NoRack);

                                // Filter out invalid rows
                                if (string.IsNullOrEmpty(vin) || 
                                    vin.Contains("#N/A") || vin.Contains("#REF!") || vin.Contains("#VALUE!"))
                                {
                                    continue;
                                }

                                // AUTO-CALCULATE: ROP 1D = MIN 0,5D * 2 | MAX 2D = MIN 0,5D * 4
                                int min1DVal = ParseInt(min) ?? 0;
                                string rop = (min1DVal * 2).ToString();
                                string max = (min1DVal * 4).ToString();

                                // v12.1: Key by Customer and VIN — same VIN but diff Customer = diff Item
                                string vinKey = $"{NormalizeKey(cust)}|{NormalizeKey(vin)}";

                                // Capture Samples (First 3 rows)
                                if (sampleVins.Count < 3) {
                                    sampleVins.Add(vin);
                                    sampleMins.Add(GetSafeNumberString(row, colMap.MinStock)); 
                                }

                                var dto = new ItemDto {
                                    ItemCode  = itemName,
                                    Plant     = plt,
                                    Rack      = rak,
                                    NoRackStr = nrk,
                                    Customer  = cust,
                                    StatusStr = GetSafeString(row, colMap.StatusCol),
                                    Category  = category,
                                    VIN       = vin,
                                    QpcStr    = qpc,
                                    MinStr    = min, 
                                    RopStr    = rop,
                                    MaxStr    = max,
                                    PartNoStr = GetSafeString(row, colMap.CustPartNo),
                                    KanbanStr = GetSafeString(row, colMap.KanbanType),
                                };

                                // === PARSE RACK ENTRIES FOR THIS ROW ===
                                var rakEntries = new List<(string Rack, int NoRack)>();

                                if (!string.IsNullOrWhiteSpace(rak))
                                {
                                    if (nrk.Contains(":"))
                                    {
                                        // Format: "B:20,21 | E:5,6"
                                        foreach (var segment in nrk.Split('|'))
                                        {
                                            var parts = segment.Trim().Split(':');
                                            if (parts.Length != 2) continue;
                                            var segRak = parts[0].Trim().ToUpper();
                                            var matches = System.Text.RegularExpressions.Regex.Matches(parts[1], @"\d+");
                                            foreach (System.Text.RegularExpressions.Match m in matches)
                                            {
                                                rakEntries.Add((segRak, int.Parse(m.Value)));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Extract all numbers from nrk regardless of delimiters (comma, space, slash, etc)
                                        // "1,2, 3" -> 1, 2, 3
                                        // "L3, L4" -> 3, 4
                                        var matches = System.Text.RegularExpressions.Regex.Matches(nrk, @"\d+");
                                        if (matches.Count > 0)
                                        {
                                            var rakLetters = rak.Split(new[] { ',', '/', '-', '&', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                                                .Select(r => r.Trim().ToUpper()).ToList();
                                            
                                            for (int ri = 0; ri < matches.Count; ri++)
                                        {
                                                int nVal = int.Parse(matches[ri].Value);
                                            var letter = rakLetters.Count > ri ? rakLetters[ri] : rakLetters.LastOrDefault() ?? rak.Trim().ToUpper();
                                            rakEntries.Add((letter, nVal));
                                        }
                                    }
                                    else
                                    {
                                            // Fallback if no numbers found in NO RAK
                                            rakEntries.Add((rak.Trim().ToUpper(), 0));
                                        }
                                    }
                                }
                                else
                                {
                                    // No rack info — create single item with no rack
                                    rakEntries.Add(("", 0));
                                }

                                // === STATISTICS COUNTING (per VIN, not per rack entry) ===
                                if (!countedVins.Contains(vinKey))
                                {
                                    countedVins.Add(vinKey);
                                    if (itemByVin.ContainsKey(vinKey))
                                        updatedItemCount++;
                                    else
                                        newItemCount++;
                                }

                                excelVins.Add(vinKey);

                                // === CREATE/UPDATE ONE Item ROW PER RACK ENTRY ===
                                // This matches the Edit behavior: each Rack+NoRack = a separate Item row with same VIN
                                string baseVinCode = !string.IsNullOrWhiteSpace(dto.VIN) ? dto.VIN.Trim().ToUpper() : "ITEM";

                                if (colMap.Rack == -1 && colMap.NoRack == -1)
                                {
                                    // Mode update master by VIN (Dashboard FG upload without RAK column)
                                    var siblings = itemByVinRack.Values.Where(i => $"{NormalizeKey(i.Customer)}|{NormalizeKey(i.VIN ?? "")}" == vinKey).ToList();
                                    
                                    if (siblings.Count > 0)
                                    {
                                        foreach (var existingItem in siblings)
                                        {
                                            string rackKey = $"{vinKey}|{existingItem.Rack?.Trim().ToUpper() ?? ""}|{existingItem.NoRack ?? 0}";
                                            if (batchRackSet.Contains(rackKey)) continue; // Already updated in this batch
                                            batchRackSet.Add(rackKey);

                                            UpdateItem(existingItem, dto);
                                        }
                                    }
                                    else
                                    {
                                        // New VIN, no rack specified. Create default.
                                        string rackKey = $"{vinKey}||0";
                                        if (!batchRackSet.Contains(rackKey))
                                        {
                                            batchRackSet.Add(rackKey);
                                            var newItem = CreateItem(dto, baseVinCode);
                                            _context.Items.Add(newItem);
                                            itemByVinRack[rackKey] = newItem;
                                            itemByVin[vinKey] = newItem;
                                        }
                                    }
                                }
                                else
                                {
                                foreach (var (entryRak, entryNoRak) in rakEntries)
                                {
                                    string rackKey = $"{vinKey}|{entryRak}|{entryNoRak}";
                                    if (batchRackSet.Contains(rackKey)) continue;
                                    batchRackSet.Add(rackKey);

                                    if (itemByVinRack.TryGetValue(rackKey, out var existingRackItem))
                                    {
                                        // Update existing Item row — preserve its Rack/NoRack, update all other fields
                                        UpdateItem(existingRackItem, dto);
                                        existingRackItem.Rack  = string.IsNullOrWhiteSpace(entryRak) ? existingRackItem.Rack : entryRak;
                                        existingRackItem.NoRack = entryNoRak > 0 ? entryNoRak : existingRackItem.NoRack;
                                    }
                                    else
                                    {
                                        // Create new Item row for this VIN+Rack+NoRack (like sibling in Edit)
                                        bool isPrimary = !itemByVin.ContainsKey(vinKey);
                                        string finalCode;
                                        if (isPrimary)
                                        {
                                            // First rack of a new VIN → use VIN as ItemCode
                                            finalCode = baseVinCode;
                                        }
                                        else
                                        {
                                            // Sibling row → use VIN_RackNoRack suffix (same as Edit)
                                            finalCode = $"{baseVinCode}_{entryRak}{entryNoRak}";
                                        }

                                        // Ensure ItemCode uniqueness
                                        string originalCode = finalCode;
                                        int dupCounter = 1;
                                        while (existingItems.Any(i => i.ItemCode == finalCode) ||
                                               itemByVinRack.Values.Any(i => i.ItemCode == finalCode))
                                        {
                                            finalCode = originalCode + "_" + dupCounter++;
                                        }

                                        // Build dto with correct rack for this entry
                                        var entryDto = new ItemDto
                                        {
                                            ItemCode  = dto.ItemCode,
                                            Plant     = dto.Plant,
                                            Rack      = string.IsNullOrWhiteSpace(entryRak) ? dto.Rack : entryRak,
                                            NoRackStr = entryNoRak > 0 ? entryNoRak.ToString() : dto.NoRackStr,
                                            Customer  = dto.Customer,
                                            StatusStr = dto.StatusStr,
                                            Category  = dto.Category,
                                            VIN       = dto.VIN,
                                            QpcStr    = dto.QpcStr,
                                            MinStr    = dto.MinStr,
                                            RopStr    = dto.RopStr,
                                            MaxStr    = dto.MaxStr,
                                            PartNoStr = dto.PartNoStr,
                                            KanbanStr = dto.KanbanStr,
                                        };

                                        var newItem = CreateItem(entryDto, finalCode);
                                        _context.Items.Add(newItem);
                                        itemByVinRack[rackKey] = newItem;

                                        // Register as primary for subsequent siblings
                                        if (isPrimary)
                                            itemByVin[vinKey] = newItem;
                                        }
                                    }
                                }

                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                if (errorSamples.Count < 5) {
                                    errorSamples.Add($"Baris {row.RowNumber()}: {ex.Message}");
                                }
                            }
                        }
                        
                        if (successCount > 0) 
                        {
                            int totalUniqueVin = newItemCount + updatedItemCount;
                            int totalItemRows = batchRackSet.Count; // total Item rows created/updated
                            string colAudit = $"[Kolom: VIN={GetColLetter(colMap.Vin)}, PLANT={GetColLetter(colMap.Plant)}, RAK={GetColLetter(colMap.Rack)}]";
                            
                            // Pesan utama: ringkasan jelas
                            var msgParts = new List<string>();
                            if (newItemCount > 0)    msgParts.Add($"🆕 {newItemCount} VIN baru");
                            if (updatedItemCount > 0) msgParts.Add($"🔄 {updatedItemCount} VIN diperbarui");
                            
                            TempData["SuccessMessage"] = 
                                $"✅ Import selesai! Total Excel: {rowProcessCount} baris → {totalUniqueVin} VIN unik ({totalItemRows} lokasi rak). " +
                                string.Join(", ", msgParts) + ". " + colAudit;
                        }
                        if (errorCount > 0) {
                            TempData["ErrorMessage"] = $"⚠️ {errorCount} baris gagal. Contoh: " + string.Join(", ", errorSamples);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Fatal Error: " + ex.Message;
            }

            return RedirectToLocalOrIndex();
        }


        private string NormalizeKey(string? val)
        {
            return (val ?? "").Trim().ToUpper();
        }

        // --- Helper Methods (v6.0 Type-Safe & Alt+Enter Resilient) ---

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            // v7.1 Robust Normalization: Keep Alphanumeric only, handle Alt+Enter/Newlines
            return new string(header.ToUpper().Where(c => char.IsLetterOrDigit(c)).ToArray());
        }

        private string GetSafeString(ClosedXML.Excel.IXLRow row, int colIndex)
        {
            if (colIndex <= 0) return "";
            return GetSafeString(row.Cell(colIndex));
        }

        private string GetSafeNumberString(ClosedXML.Excel.IXLRow row, int colIndex)
        {
            if (colIndex <= 0) return "0";
            return GetSafeNumberString(row.Cell(colIndex));
        }

        private string GetSafeNumberString(ClosedXML.Excel.IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "0";
            
            try
            {
                // v8.0: "UNIVERSAL STRING PARSING" - Force Teks-ke-Angka
                // Kita tidak lagi mengandalkan IsNumber karena user konfirmasi Excel adalah kolom TEKS.
                string raw = "";
                
                if (cell.HasFormula) {
                    try { raw = cell.CachedValue.ToString(); } catch { raw = cell.Value.ToString(); }
                } else {
                    // Ambil sebagai string mentah, abaikan format sel murni angka/teks
                    raw = cell.Value.ToString();
                }

                if (string.IsNullOrWhiteSpace(raw) || raw == "-" || raw.Equals("null", StringComparison.OrdinalIgnoreCase)) return "0";

                // Regex Extraction: Cari urutan angka pertama (integer atau desimal)
                var match = System.Text.RegularExpressions.Regex.Match(raw, @"[0-9]+([.,][0-9]+)?");
                
                if (match.Success)
                {
                    string cleanNumber = match.Value;
                    
                    // Logika Thousand Separator (Misal: 5.000 -> 5000)
                    if (cleanNumber.Contains(".") && !cleanNumber.Contains(",")) {
                        var parts = cleanNumber.Split('.');
                        if (parts.Length > 1 && parts[parts.Length-1].Length == 3) {
                             cleanNumber = cleanNumber.Replace(".", "");
                        }
                    } 
                    // Logika Desimal Komma (Misal: 5,5 -> 5.5)
                    else if (cleanNumber.Contains(",")) {
                        cleanNumber = cleanNumber.Replace(".", "").Replace(",", ".");
                    }

                    if (double.TryParse(cleanNumber, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    {
                        return Math.Round(parsed).ToString();
                    }
                }

                return "0";
            }
            catch { 
                return "0"; 
            }
        }

        private string GetSafeString(ClosedXML.Excel.IXLCell cell)
        {
            if (cell == null || cell.IsEmpty()) return "";
            try
            {
                // Avoid direct casting, use ToString() which handles XLCellValue correctly
                return cell.Value.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }


        private int? ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal d))
                return (int)d;
            return null;
        }

        private void UpdateItem(Item item, ItemDto data)
        {
            // Do NOT overwrite item.ItemCode (Guid) once set.
            item.ItemName = data.ItemCode; // DISPLAY: LOKASI RACK (Excel Col B)
            item.Plant    = data.Plant;
            item.Rack     = data.Rack;
            item.NoRack   = ParseInt(data.NoRackStr);
            item.Customer = data.Customer;
            item.Category = data.Category;
            item.VIN      = data.VIN;
            item.QtyLot   = ParseInt(data.QpcStr);
            item.RackMin  = ParseInt(data.MinStr);
            // AUTO-CALCULATE: ROP 1D = MIN 0,5D * 2 | MAX 2D = MIN 0,5D * 4
            item.ROP      = (item.RackMin ?? 0) * 2;
            item.RackMax  = (item.RackMin ?? 0) * 4;
            item.StatusItem = data.StatusStr;
            item.IsActive = !data.StatusStr.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase);
            item.IsDeleted = false; // Restore jika sebelumnya soft-deleted
            if (!string.IsNullOrWhiteSpace(data.PartNoStr)) item.CustomerPartNumber = data.PartNoStr;
            if (!string.IsNullOrWhiteSpace(data.KanbanStr)) item.KanbanType = data.KanbanStr;
            item.UpdatedDate = DateTime.Now;
        }

        private Item CreateItem(ItemDto data, string? uniqueCode = null)
        {
            int min1D = ParseInt(data.MinStr) ?? 0;
            return new Item
            {
                ItemCode    = uniqueCode ?? Guid.NewGuid().ToString().ToUpper(), 
                ItemName    = data.ItemCode, // DISPLAY: LOKASI RACK (Excel Col B)
                Plant       = data.Plant,
                Rack        = data.Rack,
                NoRack      = ParseInt(data.NoRackStr),
                Customer    = data.Customer,
                Category    = data.Category,
                VIN         = data.VIN,
                QtyLot      = ParseInt(data.QpcStr),
                RackMin     = min1D,
                // AUTO-CALCULATE: ROP 1D = MIN 0,5D * 2 | MAX 2D = MIN 0,5D * 4
                ROP         = min1D * 2,
                RackMax     = min1D * 4,
                StatusItem  = data.StatusStr,
                IsActive    = !data.StatusStr.Equals("Tidak Aktif", StringComparison.OrdinalIgnoreCase),
                CustomerPartNumber = string.IsNullOrWhiteSpace(data.PartNoStr) ? null : data.PartNoStr,
                KanbanType  = string.IsNullOrWhiteSpace(data.KanbanStr) ? null : data.KanbanStr,
                CreatedDate = DateTime.Now
            };
        }

        private class ItemDto
        {
            public string ItemCode { get; set; } = "";
            public string Plant { get; set; } = "";
            public string Rack { get; set; } = "";
            public string NoRackStr { get; set; } = "";
            public string Customer { get; set; } = "";
            public string StatusStr { get; set; } = "";
            public string Category { get; set; } = "";
            public string VIN { get; set; } = "";
            public string QpcStr { get; set; } = "";
            public string MinStr { get; set; } = "";
            public string RopStr { get; set; } = "";
            public string MaxStr { get; set; } = "";
            public string PartNoStr { get; set; } = "";
            public string KanbanStr { get; set; } = "";
        }

        // Helper for PartNumber lookup
        [HttpGet]
        public async Task<IActionResult> GetItemInfo(string itemCode)
        {
            if (string.IsNullOrEmpty(itemCode)) return Json(new { success = false });

            // v4.0 Unified Tag Lookup
            // Logic: Operator scans "RVI-A-10"
            // We search for item where (ItemName + "-" + Rack + "-" + NoRack) matches.
            var item = await _context.Items
                .AsNoTracking()
                .ToListAsync();

            var matched = item.FirstOrDefault(i => 
            {
                var combinedTag = $"{i.ItemName}-{i.Rack}-{i.NoRack}".ToUpper();
                return combinedTag == itemCode.ToUpper();
            });

            if (matched != null)
            {
                return Json(new { success = true, itemName = matched.ItemName, rack = matched.Rack, noRack = matched.NoRack });
            }

            return Json(new { success = false });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> EditMasterAjax([FromBody] EditMasterDto dto)
        {
            try
            {
                var item = await _context.Items.FindAsync(dto.ItemId);
                if (item == null) return Json(new { success = false, message = "Item not found." });

                if (dto.Rack != null) item.Rack = dto.Rack;
                if (dto.NoRack.HasValue) item.NoRack = dto.NoRack.Value;
                if (dto.Plant != null) item.Plant = dto.Plant;
                if (dto.Customer != null) item.Customer = dto.Customer;
                if (dto.StatusItem != null) 
                {
                    item.StatusItem = dto.StatusItem;
                    item.IsActive = !string.Equals(dto.StatusItem, "Tidak Aktif", StringComparison.OrdinalIgnoreCase);
                }
                if (dto.Category != null) item.Category = dto.Category;
                if (dto.VIN != null) item.VIN = dto.VIN;
                if (dto.QtyLot.HasValue) item.QtyLot = dto.QtyLot.Value;
                if (dto.RackMin.HasValue) item.RackMin = dto.RackMin.Value;
                if (dto.RackMax.HasValue) item.RackMax = dto.RackMax.Value;
                if (dto.ItemName != null) item.ItemName = dto.ItemName;
                
                item.UpdatedDate = DateTime.Now;
                _context.Update(item);
                await _context.SaveChangesAsync();
                
                // Invalidate cache agar FG dashboard langsung memuat status terbaru (PMSP/Reguler dll)
                _stockCache.Invalidate();
                
                return Json(new { success = true, message = "Master data berhasil diupdate." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    public class EditMasterDto
    {
        public int ItemId { get; set; }
        public string? Rack { get; set; }
        public int? NoRack { get; set; }
        public string? Plant { get; set; }
        public string? Customer { get; set; }
        public string? StatusItem { get; set; }
        public string? Category { get; set; }
        public string? VIN { get; set; }
        public int? QtyLot { get; set; }
        public int? RackMin { get; set; }
        public int? RackMax { get; set; }
        public string? ItemName { get; set; }
    }
}

