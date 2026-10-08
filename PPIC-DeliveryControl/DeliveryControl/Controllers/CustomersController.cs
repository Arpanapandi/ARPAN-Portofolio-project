using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    [AuthorizeAdmin]
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string searchString, int pageNumber = 1)
        {
            ViewData["CurrentFilter"] = searchString;

            var customers = _context.Customers
                .Include(c => c.DeliverySchedules)
                .AsNoTracking();

            if (!String.IsNullOrEmpty(searchString))
            {
                customers = customers.Where(c => c.CustomerCode.Contains(searchString)
                                       || c.CustomerName.Contains(searchString));
            }

            var totalItems = await customers.CountAsync();
            int pageSize = 20;

            var items = await customers
                .OrderBy(c => c.CustomerCode)
                .ThenBy(c => c.CustomerId)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string> { { "searchString", searchString } };

            return View(items);
        }

        // GET: Customers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id);

            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // GET: Customers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("CustomerId,CustomerCode,CustomerName,Route,Cycle,Docking,Pickup,ETD,SKID,Area,IsActive,StdPrepareTime,StartPrepareTime")] Customer customer)
        {
            // Remove CustomerCode from ModelState since it's auto-generated
            ModelState.Remove("Range"); // Range is auto-calculated
            
            if (ModelState.IsValid)
            {
                // Check for duplicates — kombinasi CustomerName + Dock + Route + Cycle harus unik
                if (await _context.Customers.AnyAsync(c => c.CustomerName == customer.CustomerName && c.Route == customer.Route && c.Cycle == customer.Cycle))
                {
                    ModelState.AddModelError("CustomerCode", $"Kombinasi Customer, Dock, Route, dan Cycle sudah ada.");
                    return View(customer);
                }

                // Generate AutoCode
                var lastCode = await _context.Customers
                    .Where(c => c.AutoCode != null && c.AutoCode.StartsWith("C-"))
                    .OrderByDescending(c => c.AutoCode)
                    .Select(c => c.AutoCode)
                    .FirstOrDefaultAsync();

                int nextNum = 1;
                if (!string.IsNullOrEmpty(lastCode))
                {
                    if (int.TryParse(lastCode.Replace("C-", ""), out int lastNum))
                    {
                        nextNum = lastNum + 1;
                    }
                }
                customer.AutoCode = $"C-{nextNum:D4}";

                customer.CreatedDate = DateTime.Now;
                
                // Auto-calculate Range dari ETD - Pickup
                customer.CalculateRange();
                
                _context.Add(customer);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Customer berhasil ditambahkan dengan Kode: {customer.AutoCode}!";
                return RedirectToAction(nameof(Index));
            }
            return View(customer);
        }
        


        // GET: Customers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == id);
            
            if (customer == null)
            {
                return NotFound();
            }
            return View(customer);
        }

        // POST: Customers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CustomerId,CustomerCode,CustomerName,Route,Cycle,Docking,Pickup,ETD,SKID,Area,IsActive,CreatedDate,StdPrepareTime,StartPrepareTime")] Customer customer)
        {
            if (id != customer.CustomerId)
            {
                return NotFound();
            }

            // Remove navigation properties from validation
            ModelState.Remove("DeliverySchedules");
            ModelState.Remove("Range"); // Range is auto-calculated

            if (ModelState.IsValid)
            {
                try
                {
                    customer.UpdatedDate = DateTime.Now;
                    
                    // Auto-calculate Range dari ETD - Pickup
                    customer.CalculateRange();
                    
                    _context.Update(customer);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Customer berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CustomerExists(customer.CustomerId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            
            // If called from modal, return to Index
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return RedirectToAction(nameof(Index));
            }
            
            return View(customer);
        }

        // GET: Customers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            // Tidak perlu Include disini, cukup load data customer saja untuk performa
            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.CustomerId == id);
            
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // POST: Customers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var customer = await _context.Customers
                .Include(c => c.DeliverySchedules)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer != null)
            {
                try
                {
                    // Set CustomerId = NULL pada jadwal terkait agar jadwal tidak ikut terhapus
                    foreach (var schedule in customer.DeliverySchedules)
                        schedule.CustomerId = null;

                    _context.Customers.Remove(customer);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Customer '{customer.CustomerCode}' berhasil dihapus!";
                }
                catch (DbUpdateException ex)
                {
                    TempData["ErrorMessage"] = $"Error saat menghapus customer: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return RedirectToAction(nameof(Index));
        }
        
        // POST: Force Delete Customer
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceDelete(int id)
        {
            var customer = await _context.Customers
                .Include(c => c.DeliverySchedules)
                .FirstOrDefaultAsync(c => c.CustomerId == id);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer tidak ditemukan!";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Set CustomerId = NULL pada jadwal terkait agar jadwal tidak ikut terhapus
                foreach (var schedule in customer.DeliverySchedules)
                    schedule.CustomerId = null;

                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"✅ Customer '{customer.CustomerCode}' berhasil dihapus!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat menghapus: {ex.InnerException?.Message ?? ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CustomerExists(int id)
        {
            return _context.Customers.Any(e => e.CustomerId == id);
        }

        // Bulk Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada customer yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            var ids = selectedIds.Split(',').Select(int.Parse).ToList();
            var customersToDelete = await _context.Customers
                .Include(c => c.DeliverySchedules)
                .Where(c => ids.Contains(c.CustomerId))
                .ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var errorMessages = new List<string>();

            foreach (var customer in customersToDelete)
            {
                try
                {
                    // Set CustomerId = NULL pada jadwal terkait agar jadwal tidak ikut terhapus
                    if (customer.DeliverySchedules != null)
                    {
                        foreach (var schedule in customer.DeliverySchedules)
                            schedule.CustomerId = null;
                    }

                    _context.Customers.Remove(customer);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errorMessages.Add($"{customer.CustomerCode}: {ex.Message}");
                    errorCount++;
                }
            }

            if (successCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {successCount} customer!";
            }

            if (errorCount > 0)
            {
                TempData["ErrorMessage"] = $"⚠️ {errorCount} customer gagal dihapus: {string.Join(", ", errorMessages.Take(5))}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Template Customer");

                // Header matches the application table (KODE is auto-generated)
                var headers = new[] { 
                    "NAMA CUSTOMER", "DOCK", "ROUTE", "CYCLE", 
                    "START PREP", "END PREP",
                    "DOCKING", "PICKUP", "ETD", "RANGE", "SKID", "AREA" 
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Style header
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f172a"); // Dark theme match
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                headerRange.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                // Example data (Row 2) - Use REAL DATA from Database
                var sampleCustomer = _context.Customers.FirstOrDefault();
                if (sampleCustomer != null)
                {
                    worksheet.Cell(2, 1).Value = sampleCustomer.CustomerCode;
                    worksheet.Cell(2, 2).Value = sampleCustomer.CustomerName;
                    worksheet.Cell(2, 3).Value = sampleCustomer.Route;
                    worksheet.Cell(2, 4).Value = sampleCustomer.Cycle;
                    worksheet.Cell(2, 5).Value = FormatPrepTime(sampleCustomer.StartPrepareTime);
                    worksheet.Cell(2, 6).Value = FormatPrepTime(sampleCustomer.StdPrepareTime);
                    worksheet.Cell(2, 7).Value = sampleCustomer.Docking;
                    worksheet.Cell(2, 8).Value = sampleCustomer.Pickup;
                    worksheet.Cell(2, 9).Value = sampleCustomer.ETD;
                    worksheet.Cell(2, 10).Value = sampleCustomer.Range ?? ""; 
                    worksheet.Cell(2, 11).Value = sampleCustomer.SKID;
                    worksheet.Cell(2, 12).Value = sampleCustomer.Area;
                }
                else
                {
                    // Fallback
                    worksheet.Cell(2, 1).Value = "PT. ASTRA HONDA MOTOR"; 
                    worksheet.Cell(2, 2).Value = "DOCK 42";
                    worksheet.Cell(2, 3).Value = "RC25";
                    worksheet.Cell(2, 4).Value = "C1";
                    worksheet.Cell(2, 5).Value = "00:15";
                    worksheet.Cell(2, 6).Value = "00:30";
                    worksheet.Cell(2, 7).Value = "21:00";
                    worksheet.Cell(2, 8).Value = "04:00";
                    worksheet.Cell(2, 9).Value = "04:30";
                    worksheet.Cell(2, 10).Value = "30 Menit";
                    worksheet.Cell(2, 11).Value = "4 - 8"; 
                    worksheet.Cell(2, 12).Value = "A1-2"; 
                }

                // Instructions
                worksheet.Cell(4, 2).Value = "CATATAN PENGISIAN UNTUK IMPORT BARU:"; 
                worksheet.Cell(5, 2).Value = "• Gunakan template ini untuk MENAMBAH data baru.";
                worksheet.Cell(6, 2).Value = "• NAMA CUSTOMER: Wajib diisi (Manual Input)";
                worksheet.Cell(7, 2).Value = "• DOCK: Wajib diisi";

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Input_Customer.xlsx");
                }
            }
        }

        // Export All Data (For Backup/Reporting)
        public async Task<IActionResult> ExportExcel()
        {
            var customers = await _context.Customers.OrderBy(c => c.CustomerCode).AsNoTracking().ToListAsync();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Data Customer");

                // Headers
                var headers = new[] { 
                    "KODE", "NAMA CUSTOMER", "DOCK", "ROUTE", "CYCLE", 
                    "START PREP", "END PREP",
                    "DOCKING", "PICKUP", "ETD", "RANGE", "SKID", "AREA" 
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    worksheet.Cell(1, i + 1).Value = headers[i];
                }

                // Header Style
                var headerRange = worksheet.Range(1, 1, 1, headers.Length);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#15803d"); // Green for Export
                headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

                // Data Rows
                int rowIdx = 2;
                foreach (var c in customers)
                {
                    worksheet.Cell(rowIdx, 1).Value = c.AutoCode;
                    worksheet.Cell(rowIdx, 2).Value = c.CustomerCode;
                    worksheet.Cell(rowIdx, 3).Value = c.CustomerName;
                    worksheet.Cell(rowIdx, 4).Value = c.Route;
                    worksheet.Cell(rowIdx, 5).Value = c.Cycle;
                    worksheet.Cell(rowIdx, 6).Value = FormatPrepTime(c.StartPrepareTime);
                    worksheet.Cell(rowIdx, 7).Value = FormatPrepTime(c.StdPrepareTime);
                    worksheet.Cell(rowIdx, 8).Value = c.Docking;
                    worksheet.Cell(rowIdx, 9).Value = c.Pickup;
                    worksheet.Cell(rowIdx, 10).Value = c.ETD;
                    worksheet.Cell(rowIdx, 11).Value = c.Range;
                    worksheet.Cell(rowIdx, 12).Value = c.SKID;
                    worksheet.Cell(rowIdx, 13).Value = c.Area;
                    rowIdx++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Data_Customer_{DateTime.Now:yyyyMMdd}.xlsx");
                }
            }
        }

        private string NormalizeHeader(string header)
        {
            if (string.IsNullOrEmpty(header)) return "";
            return new string(header.ToUpper().Where(c => char.IsLetterOrDigit(c)).ToArray());
        }

        private int ParsePrepTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            if (value.Contains(":"))
            {
                // Try format HH:mm or H:mm
                var parts = value.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int mins))
                {
                    return (hours * 60) + mins;
                }
                
                if (TimeSpan.TryParse(value, out var ts))
                {
                    return (int)ts.TotalMinutes;
                }
            }
            
            // Try numeric
            if (double.TryParse(value, out double numericVal))
            {
                // If it's a fractional number like 0.8263888 (Excel's way of representing time)
                if (numericVal < 1 && numericVal > 0)
                {
                    return (int)Math.Round(numericVal * 24 * 60);
                }
                return (int)Math.Round(numericVal);
            }
            
            return 0;
        }

        private string FormatPrepTime(int totalMinutes)
        {
            if (totalMinutes <= 0) return "00:00";
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }

        // Import Excel Mapping
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid! Silakan pilih file Excel terlebih dahulu.";
                return RedirectToAction(nameof(Create));
            }

            if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && 
                !Path.GetExtension(file.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Format file harus .xlsx atau .xls!";
                return RedirectToAction(nameof(Create));
            }

            var customers = new List<Customer>();
            var errorMessages = new List<string>();
            int successCount = 0;
            int errorCount = 0;
            int totalRows = 0;
            int skippedBlank = 0;

            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new ClosedXML.Excel.XLWorkbook(stream))
                    {
                        var worksheet = workbook.Worksheet(1);
                        var firstRow = worksheet.Row(1);
                        
                        // Dynamic Header Detection
                        var headerMap = new Dictionary<string, int>();
                        for (int c = 1; c <= worksheet.ColumnsUsed().Count(); c++)
                        {
                            var h = NormalizeHeader(firstRow.Cell(c).GetString());
                            if (!string.IsNullOrEmpty(h) && !headerMap.ContainsKey(h)) headerMap[h] = c;
                        }

                        int FindCol(params string[] keywords)
                        {
                            foreach (var kw in keywords)
                            {
                                var normalizedKw = NormalizeHeader(kw);
                                if (headerMap.ContainsKey(normalizedKw)) return headerMap[normalizedKw];
                            }
                            return -1;
                        }

                        // Map columns
                        int colCust = FindCol("NAMA CUSTOMER", "CUSTOMER", "KODE", "PELANGGAN");
                        int colDock = FindCol("DOCK", "DOCKNAME", "NAMA DOCK"); // Removed DOCKING
                        int colRoute = FindCol("ROUTE", "RUTE");
                        int colCycle = FindCol("CYCLE");
                        int colStartPrep = FindCol("START PREP", "START PREPARE", "START");
                        int colEndPrep = FindCol("END PREP", "END PREPARE", "STD PREPARE", "PREPARE");
                        int colDocking = FindCol("DOCKING", "WAKTU DOCKING");
                        int colPickup = FindCol("PICKUP", "WAKTU PICKUP");
                        int colEtd = FindCol("ETD");
                        int colRange = FindCol("RANGE", "JARAK");
                        int colSkid = FindCol("SKID", "QTY SKID");
                        int colArea = FindCol("AREA", "LOKASI");

                        if (colCust == -1 || colDock == -1)
                        {
                            TempData["ErrorMessage"] = "Format header tidak dikenali! Pastikan ada kolom NAMA CUSTOMER dan DOCK.";
                            return RedirectToAction(nameof(Index));
                        }

                        var rows = worksheet.RowsUsed().Skip(1); // Skip header
                        
                        // Load existing customers to avoid unique constraint violations
                        var existingCustomers = await _context.Customers.ToListAsync();
                        
                        // Get current max AutoCode number
                        var lastAutoCode = existingCustomers
                            .Where(c => !string.IsNullOrEmpty(c.AutoCode) && c.AutoCode.StartsWith("C-"))
                            .OrderByDescending(c => c.AutoCode)
                            .Select(c => c.AutoCode)
                            .FirstOrDefault();
                        
                        int nextAutoCodeNum = 1;
                        if (!string.IsNullOrEmpty(lastAutoCode))
                        {
                            var numStr = lastAutoCode.Replace("C-", "");
                            if (int.TryParse(numStr, out int lastNum))
                            {
                                nextAutoCodeNum = lastNum + 1;
                            }
                        }

                        var customerLookup = existingCustomers
                            .GroupBy(c => $"{c.CustomerCode?.Trim().ToUpper()}|{c.CustomerName?.Trim().ToUpper()}|{c.Route?.Trim().ToUpper()}|{c.Cycle?.Trim().ToUpper()}|{c.Docking?.Trim().ToUpper()}|{c.Pickup?.Trim().ToUpper()}|{c.ETD?.Trim().ToUpper()}|{c.Range?.Trim().ToUpper()}|{c.SKID?.Trim().ToUpper()}|{c.Area?.Trim().ToUpper()}|{c.StartPrepareTime}|{c.StdPrepareTime}")
                            .ToDictionary(g => g.Key, g => g.First());

                        string lastCustomerCode = "";

                        foreach (var row in rows)
                        {
                            totalRows++;
                            try
                            {
                                // Get values using dynamic columns
                                var customerCode = colCust != -1 ? row.Cell(colCust).GetString().Trim() : "";
                                var customerName = colDock != -1 ? row.Cell(colDock).GetString().Trim() : "";
                                var rangeExcel = colRange != -1 ? row.Cell(colRange).GetString().Trim() : "";
                                
                                // Carry-over logic for Merged Cells (Customer Name)
                                if (string.IsNullOrWhiteSpace(customerCode) && !string.IsNullOrWhiteSpace(lastCustomerCode))
                                {
                                    customerCode = lastCustomerCode;
                                }

                                if (string.IsNullOrWhiteSpace(customerCode)) 
                                {
                                    skippedBlank++;
                                    continue;
                                }
                                
                                lastCustomerCode = customerCode; // Remember for next row if it's blank

                                // EXPLICIT CHECK: CustomerName (DOCK) is REQUIRED
                                if (string.IsNullOrWhiteSpace(customerName))
                                {
                                    skippedBlank++;
                                    errorMessages.Add($"Baris {row.RowNumber()}: DOCK (kolom {colDock}) kosong. Baris dilewati.");
                                    continue;
                                }

                                var route = colRoute != -1 ? row.Cell(colRoute).GetString().Trim() : "";
                                var cycle = colCycle != -1 ? row.Cell(colCycle).GetString().Trim() : "";
                                
                                int startPrep = ParsePrepTime(colStartPrep != -1 ? row.Cell(colStartPrep).GetString().Trim() : "0");
                                int endPrep = ParsePrepTime(colEndPrep != -1 ? row.Cell(colEndPrep).GetString().Trim() : "0");

                                var docking = colDocking != -1 ? row.Cell(colDocking).GetString().Trim() : "";
                                var pickup = colPickup != -1 ? row.Cell(colPickup).GetString().Trim() : "";
                                var etd = colEtd != -1 ? row.Cell(colEtd).GetString().Trim() : "";
                                var skidStr = colSkid != -1 ? row.Cell(colSkid).GetString().Trim() : "";
                                var area = colArea != -1 ? row.Cell(colArea).GetString().Trim() : "";

                                var key = $"{customerCode.ToUpper()}|{customerName.ToUpper()}|{route.ToUpper()}|{cycle.ToUpper()}|{docking.ToUpper()}|{pickup.ToUpper()}|{etd.ToUpper()}|{rangeExcel.ToUpper()}|{skidStr.ToUpper()}|{area.ToUpper()}|{startPrep}|{endPrep}";
                                
                                if (customerLookup.TryGetValue(key, out var existing))
                                {
                                    // Update existing record
                                    existing.Route = string.IsNullOrWhiteSpace(route) ? null : route;
                                    existing.Cycle = string.IsNullOrWhiteSpace(cycle) ? null : cycle;
                                    existing.StartPrepareTime = startPrep;
                                    existing.StdPrepareTime = endPrep;
                                    existing.Docking = string.IsNullOrWhiteSpace(docking) ? null : docking;
                                    existing.Pickup = string.IsNullOrWhiteSpace(pickup) ? null : pickup;
                                    existing.ETD = string.IsNullOrWhiteSpace(etd) ? null : etd;
                                    existing.SKID = string.IsNullOrWhiteSpace(skidStr) ? null : skidStr;
                                    existing.Area = string.IsNullOrWhiteSpace(area) ? null : area;
                                    existing.UpdatedDate = DateTime.Now;
                                    
                                    if (!string.IsNullOrWhiteSpace(rangeExcel)) existing.Range = rangeExcel;
                                    else existing.CalculateRange();
                                    
                                    if (existing.CustomerId > 0)
                                    {
                                        _context.Update(existing);
                                    }
                                }
                                else
                                {
                                    // Create new record
                                    var customer = new Customer
                                    {
                                        AutoCode = $"C-{nextAutoCodeNum++:D4}",
                                        CustomerCode = customerCode,
                                        CustomerName = customerName,
                                        Route = string.IsNullOrWhiteSpace(route) ? null : route,
                                        Cycle = string.IsNullOrWhiteSpace(cycle) ? null : cycle,
                                        StartPrepareTime = startPrep,
                                        StdPrepareTime = endPrep,
                                        Docking = string.IsNullOrWhiteSpace(docking) ? null : docking,
                                        Pickup = string.IsNullOrWhiteSpace(pickup) ? null : pickup,
                                        ETD = string.IsNullOrWhiteSpace(etd) ? null : etd,
                                        SKID = string.IsNullOrWhiteSpace(skidStr) ? null : skidStr,
                                        Area = string.IsNullOrWhiteSpace(area) ? null : area,
                                        IsActive = true,
                                        CreatedDate = DateTime.Now
                                    };
                                    
                                    if (!string.IsNullOrWhiteSpace(rangeExcel)) customer.Range = rangeExcel;
                                    else customer.CalculateRange();
                                    
                                    _context.Customers.Add(customer);
                                    customerLookup[key] = customer; // Add to lookup to prevent duplicates in same file
                                }
                                
                                successCount++;
                            }
                            catch (Exception ex)
                            {
                                var innerMsg = ex.InnerException != null ? $" -> {ex.InnerException.Message}" : "";
                                errorMessages.Add($"Baris {row.RowNumber()}: {ex.Message}{innerMsg}");
                                errorCount++;
                            }
                        }
                    }
                }

                await _context.SaveChangesAsync();

                // Auto re-link jadwal yang customerId-nya null ke customer baru
                var (relinkUpdated, relinkNotFound) = await RelinkSchedulesToCustomersAsync();

                if (successCount > 0)
                {
                    var msg = $"✅ Berhasil memproses {successCount} data customer (Total {totalRows} baris di Excel).";
                    if (skippedBlank > 0) msg += $" Catatan: {skippedBlank} baris memiliki Customer Name kosong.";
                    if (errorCount > 0) msg += $" Namun ada {errorCount} baris yang error.";
                    if (relinkUpdated > 0) msg += $" 🔗 {relinkUpdated} jadwal di-relink ke customer baru.";
                    TempData["SuccessMessage"] = msg;
                }
                else if (totalRows > 0)
                {
                    TempData["ErrorMessage"] = $"Gagal memproses data. Total baris: {totalRows}, Skipped: {skippedBlank}, Error: {errorCount}.";
                }
                else
                {
                    TempData["ErrorMessage"] = "File kosong atau tidak ada data yang ditemukan.";
                }

                if (errorCount > 0)
                {
                    var errorDetail = string.Join(" | ", errorMessages.Take(5));
                    TempData["WarningMessage"] = $"Detail Error: {errorDetail}";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat import Excel: {ex.Message}<br/>Stack: {ex.StackTrace}";
                return RedirectToAction(nameof(Create));
            }
        }

        // Helper: re-link schedules with null CustomerId to matching customers
        private async Task<(int updated, int notFound)> RelinkSchedulesToCustomersAsync()
        {
            var unlinked = await _context.DeliverySchedules
                .Where(s => s.CustomerId == null)
                .ToListAsync();

            if (!unlinked.Any()) return (0, 0);

            var customers = await _context.Customers.ToListAsync();

            int updated = 0, notFound = 0;
            foreach (var schedule in unlinked)
            {
                // Step 1: strict match — Route + Cycle + both prep times (exact data)
                var match = customers.FirstOrDefault(c =>
                    string.Equals(c.Route?.Trim(), schedule.Route?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Cycle?.Trim(), schedule.Cycle?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    c.StartPrepareTime == schedule.StartPrepareTime &&
                    c.StdPrepareTime == schedule.StdPrepareTime);

                // Step 2: fallback — Route + Cycle only (prep times may have been updated in master)
                if (match == null)
                {
                    var candidates = customers.Where(c =>
                        string.Equals(c.Route?.Trim(), schedule.Route?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.Cycle?.Trim(), schedule.Cycle?.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (candidates.Count == 1)
                    {
                        // Only one customer for this Route+Cycle — safe to link
                        match = candidates[0];
                    }
                    else if (candidates.Count > 1)
                    {
                        // Multiple customers share the same Route+Cycle.
                        // Try to pick the closest prep-time match to minimise wrong links.
                        match = candidates
                            .OrderBy(c => Math.Abs(c.StartPrepareTime - schedule.StartPrepareTime)
                                        + Math.Abs(c.StdPrepareTime - schedule.StdPrepareTime))
                            .First();
                    }
                }

                if (match != null)
                {
                    schedule.CustomerId = match.CustomerId;
                    // Sync prep times from the current customer master so future strict matches work
                    schedule.StartPrepareTime = match.StartPrepareTime;
                    schedule.StdPrepareTime   = match.StdPrepareTime;
                    updated++;
                }
                else
                {
                    notFound++;
                }
            }

            if (updated > 0) await _context.SaveChangesAsync();
            return (updated, notFound);
        }

        // POST: Customers/RelinkSchedules
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RelinkSchedules()
        {
            try
            {
                var (updated, notFound) = await RelinkSchedulesToCustomersAsync();

                if (updated > 0)
                    TempData["SuccessMessage"] = $"✅ Berhasil re-link {updated} jadwal ke data customer baru.";
                else
                    TempData["SuccessMessage"] = "✅ Semua jadwal sudah ter-link ke customer.";

                if (notFound > 0)
                    TempData["WarningMessage"] = $"⚠️ {notFound} jadwal tidak ditemukan pasangan customer-nya (Route/Cycle tidak cocok).";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat re-link: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
