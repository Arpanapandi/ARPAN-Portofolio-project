using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Filters;
using DeliveryControl.Helpers;
using ClosedXML.Excel;
using System.IO;
using System.Text.RegularExpressions;

namespace DeliveryControl.Controllers
{
    [AuthorizeAdmin]
    public class ItemMappingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ItemMappingsController> _logger;

        public ItemMappingsController(ApplicationDbContext context, ILogger<ItemMappingsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: ItemMappings
        // GET: ItemMappings
        public async Task<IActionResult> Index(string searchString, int pageNumber = 1)
        {
            var query = _context.ItemMappings.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                searchString = searchString.Trim();
                query = query.Where(i => 
                    EF.Functions.Like(i.Customer ?? "", $"%{searchString}%") || 
                    EF.Functions.Like(i.CustomerPartNumber ?? "", $"%{searchString}%") || 
                    EF.Functions.Like(i.VIN ?? "", $"%{searchString}%"));
            }

            var totalItems = await query.CountAsync();
            int pageSize = 20;

            var items = await query
                .OrderBy(i => i.Customer)
                .ThenBy(i => i.CustomerPartNumber)
                .ThenBy(i => i.MappingId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { { "searchString", searchString } };

            ViewData["CurrentFilter"] = searchString;
            return View(items);
        }        

        // GET: ItemMappings/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: ItemMappings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Customer,CustomerPartNumber,VIN")] ItemMapping itemMapping)
        {
            if (ModelState.IsValid)
            {
                // Uniqueness Check: (Customer + PartNo) must be unique
                var exists = await _context.ItemMappings.AnyAsync(m => 
                    m.Customer.ToUpper() == itemMapping.Customer.ToUpper() && 
                    m.CustomerPartNumber.ToUpper() == itemMapping.CustomerPartNumber.ToUpper());
                
                if (exists) {
                    ModelState.AddModelError("", "Mapping untuk Customer dan Part No ini sudah ada!");
                    return View(itemMapping);
                }

                itemMapping.CreatedDate = DateTime.Now;
                _context.Add(itemMapping);
                await _context.SaveChangesAsync();

                // Auto-remap jadwal preparation yang sedang berjalan
                int remapped = await AutoRemapByMapping(itemMapping.CustomerPartNumber, itemMapping.VIN);
                TempData["SuccessMessage"] = remapped > 0
                    ? $"Mapping baru berhasil ditambahkan! ✅ {remapped} item di jadwal preparation otomatis dikoreksi."
                    : "Mapping baru berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            return View(itemMapping);
        }

        // GET: ItemMappings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var mapping = await _context.ItemMappings.FindAsync(id);
            if (mapping == null) return NotFound();

            return View(mapping);
        }

        // POST: ItemMappings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("MappingId,Customer,CustomerPartNumber,VIN")] ItemMapping itemMapping)
        {
            if (id != itemMapping.MappingId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.ItemMappings.FindAsync(id);
                    if (existing == null) return NotFound();

                    existing.Customer = itemMapping.Customer;
                    existing.CustomerPartNumber = itemMapping.CustomerPartNumber;
                    existing.VIN = itemMapping.VIN;
                    existing.UpdatedDate = DateTime.Now;

                    _context.Update(existing);
                    await _context.SaveChangesAsync();

                    // Auto-remap jadwal preparation yang sedang berjalan
                    int remapped = await AutoRemapByMapping(itemMapping.CustomerPartNumber, itemMapping.VIN);
                    TempData["SuccessMessage"] = remapped > 0
                        ? $"Mapping berhasil diupdate! ✅ {remapped} item di jadwal preparation otomatis dikoreksi."
                        : "Mapping berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ItemMappingExists(itemMapping.MappingId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(itemMapping);
        }

        // GET: ItemMappings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var mapping = await _context.ItemMappings.FirstOrDefaultAsync(m => m.MappingId == id);
            if (mapping == null) return NotFound();

            return View(mapping);
        }

        // POST: ItemMappings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var mapping = await _context.ItemMappings.FindAsync(id);
            if (mapping != null)
            {
                _context.ItemMappings.Remove(mapping);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Mapping berhasil dihapus!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ItemMappingExists(int id)
        {
            return _context.ItemMappings.Any(e => e.MappingId == id);
        }

        /// <summary>
        /// Setelah mapping baru ditambahkan/diupdate, otomatis koreksi DeliveryItems di jadwal aktif
        /// yang itemnya belum match (QPC=1/null) dengan PartNo tersebut.
        /// </summary>
        private async Task<int> AutoRemapByMapping(string? customerPartNumber, string? targetVin)
        {
            if (string.IsNullOrWhiteSpace(customerPartNumber) || string.IsNullOrWhiteSpace(targetVin))
                return 0;

            try
            {
                static string NormCode(string? s) =>
                    string.IsNullOrWhiteSpace(s) ? "" :
                    Regex.Replace(s.Trim().ToUpper(), @"[^A-Z0-9]", "");

                string normPartNo = NormCode(customerPartNumber);
                string normVin    = NormCode(targetVin);

                // Cari master Item yang cocok dengan VIN target
                var allItems = await _context.Items.ToListAsync();
                var targetItem = allItems.FirstOrDefault(i =>
                    VinHelper.IsMatch(i.VIN, targetVin) ||
                    (!string.IsNullOrWhiteSpace(i.ItemCode) && VinHelper.IsMatch(i.ItemCode, targetVin)));

                if (targetItem == null || (targetItem.QtyLot ?? 0) <= 0)
                {
                    _logger.LogWarning("AutoRemap: master item untuk VIN '{VIN}' tidak ditemukan atau QtyLot=0.", targetVin);
                    return 0;
                }

                // Cari DeliveryItems yang itemnya cocok dengan PartNo lama (belum ter-remap)
                // Kandidat: item yang VIN/ItemCode-nya adalah normalized PartNo atau PartNo itu sendiri
                var deliveryItems = await _context.DeliveryItems
                    .Include(di => di.Item)
                    .Include(di => di.DeliverySchedule)
                    .Where(di => di.ItemId != targetItem.ItemId) // bukan target item
                    .ToListAsync();

                int remapped = 0;
                foreach (var di in deliveryItems)
                {
                    if (di.Item == null) continue;

                    // Kode-kode yang tersimpan di item saat ini
                    var codes = new[] { di.Item.CustomerPartNumber, di.Item.VIN, di.Item.ItemCode }
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList();

                    bool isMatch = codes.Any(code =>
                        NormCode(code) == normPartNo ||
                        NormCode(code) == normVin ||
                        VinHelper.IsMatch(code, customerPartNumber) ||
                        VinHelper.IsMatch(code, targetVin));

                    if (isMatch)
                    {
                        _logger.LogInformation(
                            "AutoRemap: DeliveryItemId={Id} PartNo='{Code}' oldItemId={Old} → ItemId={New} VIN='{VIN}' QtyLot={QPC}",
                            di.DeliveryItemId, codes.FirstOrDefault(), di.ItemId,
                            targetItem.ItemId, targetItem.VIN, targetItem.QtyLot);

                        // Simpan Part No asli sebelum remap (agar tetap tampil di jadwal)
                        if (string.IsNullOrWhiteSpace(di.ExternalPartNo))
                        {
                            // Ambil Part No yang paling mirip format eksternal (mengandung tanda hubung)
                            di.ExternalPartNo = codes.FirstOrDefault(c => c?.Contains('-') == true)
                                ?? customerPartNumber
                                ?? codes.FirstOrDefault();
                        }

                        di.ItemId = targetItem.ItemId;
                        di.UpdatedDate = DateTime.Now;
                        remapped++;
                    }
                }

                if (remapped > 0)
                    await _context.SaveChangesAsync();

                return remapped;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoRemapByMapping error untuk PartNo='{PartNo}' VIN='{VIN}'", customerPartNumber, targetVin);
                return 0;
            }
        }

        // Download Template Mapping
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Mapping");

                var headers = new[] { "DOCK", "PART NO (EKSTERNAL)", "VIN (INTERNAL)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
                headerRange.Style.Font.FontColor = XLColor.White;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                // Sample data
                worksheet.Cell(2, 1).Value = "CUSTOMER_A";
                worksheet.Cell(2, 2).Value = "EXT-PART-001";
                worksheet.Cell(2, 3).Value = "VIN-INT-001";

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Item_Mapping.xlsx");
                }
            }
        }

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return new string(header.ToUpper().Where(c => char.IsLetterOrDigit(c)).ToArray());
        }

        // Import Excel Mapping
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid!";
                return RedirectToAction(nameof(Index));
            }

            int createdCount = 0;
            int updatedCount = 0;


            try
            {
                // 1. Load Existing Mappings (Performance: Memory Lookup)
                var existingMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
                
                // 2. Load Master Items VINs for Validation
                var masterVinsList = await _context.Items.AsNoTracking()
                                       .Select(i => i.VIN)
                                       .Where(v => v != null)
                                       .Distinct()
                                       .ToListAsync();
                var masterVins = masterVinsList.ToHashSet();

                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var rows = worksheet.RowsUsed().Skip(1); // Skip Header

                        // Column Detection
                        var headerRow = worksheet.Row(1);
                        var headers = new Dictionary<string, int>();
                        for (int i = 1; i <= 20; i++) 
                        {
                            var cellVal = headerRow.Cell(i).GetValue<string>().Trim().ToUpper();
                            if (!string.IsNullOrEmpty(cellVal)) headers[cellVal] = i;
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords) {
                                foreach (var h in headers) {
                                    if (h.Key.Contains(kw)) return h.Value;
                                }
                            }
                            return -1;
                        }

                        int colCust = FindCol("DOCK", "KODE CUSTOMER", "CUSTOMER", "CUST", "NAMA CUSTOMER");
                        int colPart = FindCol("PART NO EKSTERNAL", "PART NO EXTERNAL", "PART NO", "EXT");
                        int colVin  = FindCol("VIN INTERNAL", "VIN", "INTERNAL");
                        
                        // Validation
                        if (colCust == -1 || colPart == -1 || colVin == -1)
                        {
                            TempData["ErrorMessage"] = "Kolom Wajib (Customer, Part No, VIN) tidak ditemukan!";
                            return RedirectToAction(nameof(Index));
                        }

                        // Lookup for Fast Update
                        var mappingLookup = existingMappings
                            .GroupBy(m => $"{m.CustomerPartNumber.ToUpper()}|{m.Customer.ToUpper()}")
                            .ToDictionary(g => g.Key, g => g.First());

                        string lastVin = "";
                        string lastCust = "";

                        foreach (var row in rows)
                        {
                            if (row.IsEmpty()) continue;

                            string cust = row.Cell(colCust).GetString().Trim();
                            string partNo = row.Cell(colPart).GetString().Trim();
                            string vin = row.Cell(colVin).GetString().Trim();

                            // Fill Down
                            if (string.IsNullOrEmpty(vin)) vin = lastVin; else lastVin = vin;
                            if (string.IsNullOrEmpty(cust)) cust = lastCust; else lastCust = cust;

                            if (string.IsNullOrEmpty(partNo)) continue;

                            // Create/Update Mapping
                            string key = $"{partNo.ToUpper()}|{cust.ToUpper()}";
                            
                            if (mappingLookup.TryGetValue(key, out var existingMapping))
                            {
                                // Update
                                existingMapping.VIN = vin;
                                existingMapping.UpdatedDate = DateTime.Now;
                                
                                // Hanya update state jika data memang sudah ada di DB (bukan baru di-add di loop ini)
                                if (existingMapping.MappingId > 0)
                                {
                                    _context.Update(existingMapping);
                                }
                                updatedCount++;
                            }
                            else
                            {
                                // Create
                                var newMapping = new ItemMapping
                                {
                                    Customer = cust,
                                    CustomerPartNumber = partNo,
                                    VIN = vin,

                                    CreatedDate = DateTime.Now
                                };
                                _context.ItemMappings.Add(newMapping);
                                mappingLookup[key] = newMapping; // Add to lookup to prevent duplicates in same file
                                createdCount++;
                            }
                            // successCount = createdCount + updatedCount; // This line is not needed here, total success is calculated at the end
                        }
                        await _context.SaveChangesAsync();
                        TempData["SuccessMessage"] = $"Berhasil! {createdCount} mapping baru, {updatedCount} updated.";
                    }
                }
                // The success message is now set inside the using block, so this outer one is redundant.
 
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Index));
        }

        // Bulk Delete All Mappings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteAll()
        {
            try
            {
                // USER REQUEST: Only delete Item Mapping data.
                // SAFE NOW: This only deletes from ItemMappings table, Master Items are safe.
                
                _context.ItemMappings.RemoveRange(_context.ItemMappings);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Berhasil menghapus seluruh data Master Item Mapping! Data Master Item (Stok) AMAN.";
            }
            catch (DbUpdateException)
            {
                // Catch Foreign Key violation
                TempData["ErrorMessage"] = "Gagal menghapus! Beberapa Item sedang digunakan dalam transaksi/jadwal. Hapus data transaksi terlebih dahulu jika ingin mereset master item.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
