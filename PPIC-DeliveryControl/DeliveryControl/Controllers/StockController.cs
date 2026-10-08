using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DeliveryControl.Services;
using DeliveryControl.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace DeliveryControl.Controllers
{
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class StockController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly StockSnapshotService _snapshotService;
        private readonly IMemoryCache _cache;
        private readonly DeliveryControl.Services.StockCacheService _stockCache;

        public StockController(ApplicationDbContext context, StockSnapshotService snapshotService, IMemoryCache cache, DeliveryControl.Services.StockCacheService stockCache)
        {
            _context = context;
            _snapshotService = snapshotService;
            _cache = cache;
            _stockCache = stockCache;
        }


        private class RawStockCacheData
        {
            public List<StockItemDetail> StockDetails { get; set; } = new List<StockItemDetail>();
            public List<Item> AllItems { get; set; } = new List<Item>();
            public List<PullingRecord> AllPullingInRange { get; set; } = new List<PullingRecord>();
            public List<PreparationRecord> AllPreparationInRange { get; set; } = new List<PreparationRecord>();
        }

        public async Task<IActionResult> Index(string plant = "Overall", DateTime? date = null, string period = "Day", int pageNumber = 1, string status = "All", string search = "", string customer = "All", string category = "All", string itemStatus = "All")
        {
            return View(await GetStockViewModel(plant, date, period, pageNumber, status, search, null, customer, category, itemStatus));
        }

        [HttpGet]
        public async Task<IActionResult> GetStockQuickJson(string plant = "Overall", string search = "", string customer = "All", string category = "All", string itemStatus = "All")
        {
            // Ambil seluruh data tanpa paging (limit = -1) agar JSON memiliki semua ID item
            var vm = await GetStockViewModel(plant, null, "Day", 1, "All", search, -1, customer, category, itemStatus);
            
            var data = vm.StockDetails.Select(d => new {
                itemId = d.ItemId,
                act = d.CurrentStock,
                level = d.Status, // text, e.g. "Over", "Normal", "Shortage"
                ratio = d.LevelStock
            }).ToList();

            // Ambil snapshot hari ini agar counter dashboard (Critical Stock dll) tidak auto-update (live),
            // melainkan HANYA update ketika Snapshot Manual (atau otomatis) dipicu.
            var today = DateTime.Today;
            var todaySnaps = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date == today)
                .GroupBy(s => s.ItemCode)
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .AsNoTracking()
                .ToListAsync();

            var snapDict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in todaySnaps)
            {
                var code = (s.ItemCode ?? "").Trim().ToUpper();
                if (string.IsNullOrEmpty(code)) continue;
                snapDict[code] = s.DaysCoverage;
                if (code.Contains('|'))
                {
                    var parts = code.Split('|');
                    if (parts.Length > 1)
                    {
                        var clean = parts[1].Trim();
                        snapDict[clean] = s.DaysCoverage;
                        var cleanNoSuffix = clean.Split('_')[0];
                        snapDict[cleanNoSuffix] = s.DaysCoverage;
                    }
                }
            }

            int snapShortage = 0, snapNormal = 0, snapOver = 0;
            foreach (var d in vm.StockDetails)
            {
                var key = (d.ItemCode ?? "").Trim().ToUpper();
                var vinKey = (d.VIN ?? "").Trim().ToUpper();
                decimal coverage = 0;
                bool found = snapDict.TryGetValue(key, out coverage) ||
                             (!string.IsNullOrEmpty(vinKey) && snapDict.TryGetValue(vinKey, out coverage));
                if (found)
                {
                    if (coverage < 1.0m) snapShortage++;
                    else if (coverage <= 2.0m) snapNormal++;
                    else snapOver++;
                }
            }

            return Json(new {
                success = true,
                data = data,
                summary = new {
                    totalAct = vm.StockDetails.Sum(x => x.CurrentStock),
                    totalItemSafe = snapNormal,
                    totalItemOver = snapOver,
                    totalItemShortage = snapShortage
                }
            });
        }


        public async Task<IActionResult> Molded(DateTime? date, string period = "Day", int pageNumber = 1, string search = "", string customer = "All", string category = "All")
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.SearchKeyword = search;
            var vm = await GetStockViewModel("Molded", date, period, pageNumber, search: search, customer: customer, category: category);
            
            int pageSize = 20;
            int maxLogCount = Math.Max(vm.TotalPullingToday, vm.TotalPreparationToday);
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalItems = vm.TotalPullingToday + vm.TotalPreparationToday;
            ViewBag.TotalPages = (int)Math.Ceiling(maxLogCount / (double)pageSize);
            ViewBag.RouteData = new Dictionary<string, string> { { "date", date?.ToString("yyyy-MM-dd") ?? "" }, { "period", period }, { "search", search }, { "customer", customer }, { "category", category } };
            
            return View(vm);
        }

        public async Task<IActionResult> Hose(DateTime? date, string period = "Day", int pageNumber = 1, string search = "", string customer = "All", string category = "All")
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.SearchKeyword = search;
            var vm = await GetStockViewModel("Hose", date, period, pageNumber, search: search, customer: customer, category: category);
            
            int pageSize = 20;
            int maxLogCount = Math.Max(vm.TotalPullingToday, vm.TotalPreparationToday);
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalItems = vm.TotalPullingToday + vm.TotalPreparationToday;
            ViewBag.TotalPages = (int)Math.Ceiling(maxLogCount / (double)pageSize);
            ViewBag.RouteData = new Dictionary<string, string> { { "date", date?.ToString("yyyy-MM-dd") ?? "" }, { "period", period }, { "search", search }, { "customer", customer }, { "category", category } };
            
            return View(vm);
        }

        public async Task<IActionResult> RVI(DateTime? date, string period = "Day", int pageNumber = 1, string search = "", string customer = "All", string category = "All")
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.SearchKeyword = search;
            var vm = await GetStockViewModel("RVI", date, period, pageNumber, search: search, customer: customer, category: category);
            
            int pageSize = 20;
            int maxLogCount = Math.Max(vm.TotalPullingToday, vm.TotalPreparationToday);
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalItems = vm.TotalPullingToday + vm.TotalPreparationToday;
            ViewBag.TotalPages = (int)Math.Ceiling(maxLogCount / (double)pageSize);
            ViewBag.RouteData = new Dictionary<string, string> { { "date", date?.ToString("yyyy-MM-dd") ?? "" }, { "period", period }, { "search", search }, { "customer", customer }, { "category", category } };
            
            return View(vm);
        }

        public async Task<IActionResult> BTR(DateTime? date, string period = "Day", int pageNumber = 1, string search = "", string customer = "All", string category = "All")
        {
            ViewBag.SelectedDate = date?.ToString("yyyy-MM-dd") ?? DateTime.Today.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.SearchKeyword = search;
            var vm = await GetStockViewModel("BTR", date, period, pageNumber, search: search, customer: customer, category: category);

            int pageSize = 20;
            int maxLogCount = Math.Max(vm.TotalPullingToday, vm.TotalPreparationToday);
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalItems = vm.TotalPullingToday + vm.TotalPreparationToday;
            ViewBag.TotalPages = (int)Math.Ceiling(maxLogCount / (double)pageSize);
            ViewBag.RouteData = new Dictionary<string, string> { { "date", date?.ToString("yyyy-MM-dd") ?? "" }, { "period", period }, { "search", search }, { "customer", customer }, { "category", category } };

            return View(vm);
        }


        public async Task<IActionResult> LogScanNG(DateTime? date, string period = "Day", int pageNumber = 1, string search = "")
        {
            var today = DateTime.Today;
            var filterDate = date ?? today;
            ViewBag.SelectedDate = filterDate.ToString("yyyy-MM-dd");
            ViewBag.SelectedPeriod = period;
            ViewBag.Search = search;

            DateTime startDate, endDate;
            if (period == "Week")
            {
                startDate = filterDate.Date.AddDays(-6);
                endDate = filterDate.Date.AddDays(1).AddSeconds(-1);
            }
            else if (period == "Month")
            {
                startDate = new DateTime(filterDate.Year, filterDate.Month, 1);
                endDate = startDate.AddMonths(1).AddSeconds(-1);
            }
            else if (period == "Year")
            {
                startDate = new DateTime(filterDate.Year, 1, 1);
                endDate = startDate.AddYears(1).AddSeconds(-1);
            }
            else // Day
            {
                startDate = filterDate.Date;
                endDate = startDate.AddDays(1).AddSeconds(-1);
            }

            int pageSize = 25;

            var ngPullingQuery = _context.ScanNGLogs.AsNoTracking()
                .Where(r => r.Module == "Pulling" && r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            var ngPreparationQuery = _context.ScanNGLogs.AsNoTracking()
                .Where(r => r.Module == "Preparation" && r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                ngPullingQuery = ngPullingQuery.Where(r =>
                    r.Tag.ToLower().Contains(s) ||
                    r.Label.ToLower().Contains(s) ||
                    r.CreatedBy.ToLower().Contains(s));
                ngPreparationQuery = ngPreparationQuery.Where(r =>
                    r.Tag.ToLower().Contains(s) ||
                    r.Label.ToLower().Contains(s) ||
                    r.Kanban.ToLower().Contains(s) ||
                    r.CreatedBy.ToLower().Contains(s));
            }

            ngPullingQuery = ngPullingQuery.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id);
            ngPreparationQuery = ngPreparationQuery.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id);

            var totalNGPulling = await ngPullingQuery.CountAsync();
            var totalNGPreparation = await ngPreparationQuery.CountAsync();
            var totalItems = totalNGPulling + totalNGPreparation;

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(Math.Max(totalNGPulling, totalNGPreparation) / (double)pageSize);
            ViewBag.TotalItems = totalItems;
            ViewBag.RouteData = new Dictionary<string, string>
            {
                { "date", date?.ToString("yyyy-MM-dd") },
                { "period", period },
                { "search", search }
            };

            var viewModel = new StockDashboardViewModel
            {
                PlantName = "Overall",
                SearchDate = date,
                Period = period,
                NGPulling = await ngPullingQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(),
                NGPreparation = await ngPreparationQuery.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(),
                TotalNGPulling = totalNGPulling,
                TotalNGPreparation = totalNGPreparation
            };

            return View(viewModel);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Pulling", "Leader", "User", "Preparation")]
        public async Task<IActionResult> LogNG([FromBody] NGLogRequest req)
        {
            // Fallback endpoint — seharusnya tidak dipakai lagi (masing-masing controller punya LogNG sendiri)
            if (string.IsNullOrWhiteSpace(req?.Tag)) return Json(new { ok = false });
            var createdBy = HttpContext.Session.GetString("FullName") 
                         ?? HttpContext.Session.GetString("Username") 
                         ?? "Operator";
            var rec = new ScanNGLog
            {
                Module = req.Module ?? "Unknown",
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

        /// <summary>POST /Stock/SaveItemRemark — simpan keterangan item dari DashboardControlFG</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveItemRemark([FromBody] SaveRemarkRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.ItemCode))
                return Json(new { ok = false, message = "Item code kosong" });

            var item = await _context.Items
                .FirstOrDefaultAsync(i => i.ItemCode == req.ItemCode && !i.IsDeleted);
            if (item == null)
                return Json(new { ok = false, message = "Item tidak ditemukan" });

            item.Remark = req.Remark?.Trim();
            item.UpdatedDate = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { ok = true, remark = item.Remark ?? "" });
        }

        public class SaveRemarkRequest
        {
            public string? ItemCode { get; set; }
            public string? Remark { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string plant = "Overall", DateTime? date = null, string period = "Day", string customer = "All", string category = "All", string itemStatus = "All", bool template = false)
        {
            // Jika template = true, kita tidak butuh data stok, cukup kembalikan headers kosong.
            if (template)
            {
                using (var workbook = new ClosedXML.Excel.XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Template Stock");
                    
                    var headers = new string[] { "NO", "LOKASI RACK", "PROD. PLANT", "RACK", "NO RACK", "CUST", "STATUS", "PROD", "VIN", "QPC", "MIN", "ROP", "MAX", "ACT" };

                    // Tambahkan keterangan/deskripsi di baris 1-3
                    worksheet.Cell(1, 1).Value = "PANDUAN PENGISIAN IMPORT STOCK";
                    worksheet.Cell(1, 1).Style.Font.Bold = true;
                    worksheet.Cell(2, 1).Value = "1. Kolom NO hanya untuk nomor urut. Kolom VIN wajib diisi.";
                    worksheet.Cell(3, 1).Value = "2. Kolom NO RACK bisa diisi beberapa rack sekaligus (contoh: 1, 2, 3) pada satu baris.";
                    
                    // Style the description
                    worksheet.Range(1, 1, 1, headers.Length).Merge();
                    worksheet.Range(2, 1, 2, headers.Length).Merge();
                    worksheet.Range(3, 1, 3, headers.Length).Merge();
                    worksheet.Cell(1, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.DarkBlue;

                    // Write Headers at row 4
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var hCell = worksheet.Cell(4, i + 1);
                        hCell.Value = headers[i];
                        hCell.Style.Font.Bold = true;
                        hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                        hCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.Black;
                        hCell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    }

                    worksheet.Columns().AdjustToContents();
                    
                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();
                        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_ImportStock.xlsx");
                    }
                }
            }

            // --- JIKA BUKAN TEMPLATE (EXPORT DATA) ---
            // Ambil SEMUA data (tanpa paginasi) untuk EXCEL
            var viewModel = await GetStockViewModel(plant, date, period, 1, "All", "", -1, customer, category, itemStatus);
            var dateStr = (date ?? DateTime.Today).ToString("dd-MM-yyyy");
            
            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Stock Report");
                
                // UNIFIED HEADER FORMAT - SAME ACROSS EXCEL EXPORT, TEMPLATE, AND IMPORT
                var headers = new string[] { "NO", "LOKASI RACK", "PROD. PLANT", "RACK", "NO RACK", "CUST", "STATUS", "PROD", "VIN", "QPC", "MIN 1D", "ROP 1.5D", "MAX 2D", "ACT" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var hCell = worksheet.Cell(1, i + 1);
                    hCell.Value = headers[i];
                    hCell.Style.Font.Bold = true;
                    // Warna kolom min/rop/max sesuai dashboard
                    if (i == 10) hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#ef4444"); // Min
                    else if (i == 11) hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f59e0b"); // ROP
                    else if (i == 12) hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#06b6d4"); // Max
                    else hCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                    hCell.Style.Font.FontColor = (i >= 10 && i <= 12) ? ClosedXML.Excel.XLColor.White : ClosedXML.Excel.XLColor.Black;
                }

                // Data — item multi-lokasi di-expand jadi baris terpisah per lokasi rak dan diurutkan
                var flatData = viewModel.StockDetails.SelectMany(detail => 
                {
                    var racks = detail.RackBreakdown != null && detail.RackBreakdown.Count > 0
                        ? detail.RackBreakdown
                        : new List<RackLocationDetail>
                          {
                              new RackLocationDetail
                              {
                                  Rack         = detail.RackInfo,
                                  Column       = 0,
                                  Stock        = detail.CurrentStock,
                                  LastActivity = detail.Time
                              }
                          };

                    // Group by RackLetter so same rack goes into one row
                    var groupedRacks = racks
                        .GroupBy(r => (r.Rack ?? "").Trim().ToUpper())
                        .Select(g => new 
                        {
                            RackLetter = g.Key,
                            ColumnsStr = string.Join(", ", g.Where(x => x.Column > 0).Select(x => x.Column).Distinct().OrderBy(c => c)),
                            TotalStock = g.Sum(x => x.Stock)
                        })
                        .ToList();

                    return groupedRacks.Select(gr => new 
                    {
                        LokasiRak = detail.ItemName ?? "",
                        RackLetter = gr.RackLetter,
                        RackNoStr = gr.ColumnsStr,
                        Plant = detail.Plant,
                        Label = detail.Label,
                        Act = gr.TotalStock,
                        LevelStock = detail.LevelStock,
                        Customer = detail.Customer,
                        Status = detail.StatusItem ?? (detail.IsActive ? "Reguler" : "No Order"),
                        Prod = detail.Category,
                        Vin = detail.VIN,
                        Qpc = detail.QtyLot,
                        Min = detail.Min ?? 0,
                        Rop = detail.Rop ?? 0,
                        Max = detail.Max ?? 0,
                        Time = detail.Time,
                        Date = detail.Date,
                        Operator = detail.Operator
                    });
                })
                .OrderBy(x => x.LokasiRak)
                .ThenBy(x => x.RackLetter)
                .ThenBy(x => x.RackNoStr)
                .ToList();

                int row = 2;
                int no  = 1;
                foreach (var data in flatData)
                {
                    worksheet.Cell(row, 1).Value  = no;
                    worksheet.Cell(row, 2).Value  = data.LokasiRak;
                    worksheet.Cell(row, 3).Value  = data.Plant;
                    worksheet.Cell(row, 4).Value  = data.RackLetter;
                    worksheet.Cell(row, 5).Value  = data.RackNoStr;
                    worksheet.Cell(row, 6).Value  = data.Customer;
                    worksheet.Cell(row, 7).Value  = data.Status;
                    worksheet.Cell(row, 8).Value  = data.Prod;
                    worksheet.Cell(row, 9).Value  = data.Vin;
                    worksheet.Cell(row, 10).Value  = data.Qpc;
                    worksheet.Cell(row, 11).Value  = data.Min;
                    worksheet.Cell(row, 12).Value = data.Rop;
                    worksheet.Cell(row, 13).Value = data.Max;
                    worksheet.Cell(row, 14).Value = data.Act;
                    
                    row++;
                    no++;
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"StockReport_{plant}_{dateStr}.xlsx");
                }
            }
        }

        public async Task<IActionResult> ExportTransactionLogToExcel(string plant = "Overall", DateTime? from = null, DateTime? to = null, string mode = "All", string user = "All", string format = "Normal")
        {
            var dateStr = "";
            var start = from ?? DateTime.Today;
            var end = (to ?? start).Date.AddDays(1).AddSeconds(-1);
            
            if (from.HasValue && to.HasValue) dateStr = $"{start:ddMM}-{end:ddMM}";
            else dateStr = start.ToString("dd-MM-yyyy");

            var pullingQuery = _context.PullingRecords.AsNoTracking().Include(r => r.Item)
                .Where(r => r.CreatedDate >= start && r.CreatedDate <= end && (r.AdjustNote ?? "") != "Opname Reduce" && r.Remark != "Mismatch");
            if (user != "All" && !string.IsNullOrWhiteSpace(user)) pullingQuery = pullingQuery.Where(r => r.CreatedBy != null && r.CreatedBy.Contains(user));

            var prepQuery = _context.PreparationRecords.AsNoTracking()
                .Where(r => r.CreatedDate >= start && r.CreatedDate <= end && r.Remark != "Mismatch");
            if (user != "All" && !string.IsNullOrWhiteSpace(user)) prepQuery = prepQuery.Where(r => r.CreatedBy != null && r.CreatedBy.Contains(user));

            var recentPulling = await pullingQuery.OrderByDescending(r => r.CreatedDate).ToListAsync();
            var recentPrep = await prepQuery.OrderByDescending(r => r.CreatedDate).ToListAsync();

            if (plant != "Overall")
            {
                recentPulling = recentPulling.Where(r =>
                {
                    var plantVal = r.Item?.Plant?.Trim();
                    if (string.IsNullOrEmpty(plantVal) || plantVal == "-")
                    {
                        var itemName = r.Item?.ItemName?.Trim();
                        var knownAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Molded", "Hose", "RVI", "BTR" };
                        plantVal = (itemName != null && knownAreas.Contains(itemName)) ? itemName : (r.Plant ?? "").Trim();
                    }
                    return plantVal.Equals(plant, StringComparison.OrdinalIgnoreCase);
                }).ToList();

                recentPrep = recentPrep.Where(r => (r.Plant ?? "").Trim().Equals(plant, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (format == "Sum")
            {
                recentPulling = recentPulling
                    .GroupBy(r => new { Date = r.CreatedDate.Date, Tag = (r.Tag ?? "").Trim().ToUpper(), Label = (r.Label ?? "").Trim().ToUpper(), Rack = (r.Rack ?? "").Trim().ToUpper(), Column = r.Column, CreatedBy = (r.CreatedBy ?? "").Trim().ToUpper() })
                    .Select(g => new PullingRecord
                    {
                        CreatedDate = g.Max(x => x.CreatedDate),
                        Tag = g.First().Tag,
                        Label = g.First().Label,
                        Rack = g.First().Rack,
                        Column = g.First().Column,
                        CreatedBy = g.First().CreatedBy,
                        Quantity = g.Sum(x => x.Quantity)
                    })
                    .OrderByDescending(r => r.CreatedDate)
                    .ToList();

                recentPrep = recentPrep
                    .GroupBy(r => new { Date = r.CreatedDate.Date, Tag = (r.Tag ?? "").Trim().ToUpper(), Label = (r.Label ?? "").Trim().ToUpper(), Kanban = (r.Kanban ?? "").Trim().ToUpper(), CreatedBy = (r.CreatedBy ?? "").Trim().ToUpper() })
                    .Select(g => new PreparationRecord
                    {
                        CreatedDate = g.Max(x => x.CreatedDate),
                        Tag = g.First().Tag,
                        Label = g.First().Label,
                        Kanban = g.First().Kanban,
                        CreatedBy = g.First().CreatedBy,
                        SumQty = g.Count()
                    })
                    .OrderByDescending(r => r.CreatedDate)
                    .ToList();
            }

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                if (mode == "All")
                {
                    var ws = workbook.Worksheets.Add("Transaction Logs");
                    
                    // --- HEADERS PULLING (A-H) ---
                    var hIn = format == "Sum" ? new string[] { "No", "Tanggal", "Jam", "Total Qty", "Tag", "Label", "Rak", "User" } : new string[] { "No", "Tanggal", "Jam", "Stok", "Tag", "Label", "Rak", "User" };
                    for (int i = 0; i < hIn.Length; i++) {
                        var cell = ws.Cell(1, i + 1);
                        cell.Value = hIn[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.CornflowerBlue;
                        cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                        cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    }

                    // --- GAP COLUMN (I) ---
                    ws.Column(9).Width = 3;
                    ws.Cell(1, 9).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;

                    // --- HEADERS PREPARE (J-P or J-Q) ---
                    var hOut = format == "Sum" ? new string[] { "No", "Tanggal", "Jam", "Tag", "Label", "Kanban", "User", "Qty" } : new string[] { "No", "Tanggal", "Jam", "Tag", "Label", "Kanban", "User" };
                    for (int i = 0; i < hOut.Length; i++) {
                        var cell = ws.Cell(1, i + 10);
                        cell.Value = hOut[i];
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.Teal;
                        cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                        cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    }

                    int maxRows = Math.Max(recentPulling.Count, recentPrep.Count);
                    for (int i = 0; i < maxRows; i++)
                    {
                        if (i < recentPulling.Count) {
                            var r = recentPulling[i];
                            ws.Cell(i + 2, 1).Value = i + 1;
                            ws.Cell(i + 2, 2).Value = r.CreatedDate.ToString("dd/MM/yyyy");
                            ws.Cell(i + 2, 3).Value = r.CreatedDate.ToString("HH:mm:ss");
                            ws.Cell(i + 2, 4).Value = r.Quantity;
                            ws.Cell(i + 2, 5).Value = r.Tag;
                            ws.Cell(i + 2, 6).Value = r.Label;
                            ws.Cell(i + 2, 7).Value = (r.Rack ?? "-") + "." + r.Column;
                            ws.Cell(i + 2, 8).Value = r.CreatedBy;
                        }

                        if (i < recentPrep.Count) {
                            var r = recentPrep[i];
                            ws.Cell(i + 2, 10).Value = i + 1;
                            ws.Cell(i + 2, 11).Value = r.CreatedDate.ToString("dd/MM/yyyy");
                            ws.Cell(i + 2, 12).Value = r.CreatedDate.ToString("HH:mm:ss");
                            ws.Cell(i + 2, 13).Value = r.Tag;
                            ws.Cell(i + 2, 14).Value = r.Label;
                            ws.Cell(i + 2, 15).Value = r.Kanban;
                            ws.Cell(i + 2, 16).Value = r.CreatedBy;
                            if (format == "Sum") ws.Cell(i + 2, 17).Value = r.SumQty;
                        }
                    }
                    ws.Columns().AdjustToContents();
                }
                else if (mode == "Pulling")
                {
                    var wsIn = workbook.Worksheets.Add("PULLING (IN)");
                    var headersIn = format == "Sum" ? new string[] { "No", "Tanggal", "Jam", "Total Qty", "Tag", "Label", "Rak", "User" } : new string[] { "No", "Tanggal", "Jam", "Stok", "Tag", "Label", "Rak", "User" };
                    for (int i = 0; i < headersIn.Length; i++)
                    {
                        wsIn.Cell(1, i + 1).Value = headersIn[i];
                        wsIn.Cell(1, i + 1).Style.Font.Bold = true;
                        wsIn.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.CornflowerBlue;
                        wsIn.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    }

                    for (int i = 0; i < recentPulling.Count; i++)
                    {
                        var r = recentPulling[i];
                        wsIn.Cell(i + 2, 1).Value = i + 1;
                        wsIn.Cell(i + 2, 2).Value = r.CreatedDate.ToString("dd/MM/yyyy");
                        wsIn.Cell(i + 2, 3).Value = r.CreatedDate.ToString("HH:mm:ss");
                        wsIn.Cell(i + 2, 4).Value = r.Quantity;
                        wsIn.Cell(i + 2, 5).Value = r.Tag;
                        wsIn.Cell(i + 2, 6).Value = r.Label;
                        wsIn.Cell(i + 2, 7).Value = (r.Rack ?? "-") + "." + r.Column;
                        wsIn.Cell(i + 2, 8).Value = r.CreatedBy;
                    }
                    wsIn.Columns().AdjustToContents();
                }
                else if (mode == "Preparation")
                {
                    var wsOut = workbook.Worksheets.Add("PREPARATION (OUT)");
                    var headersOut = format == "Sum" ? new string[] { "No", "Tanggal", "Jam", "Tag", "Label", "Kanban", "User", "Qty" } : new string[] { "No", "Tanggal", "Jam", "Tag", "Label", "Kanban", "User" };
                    for (int i = 0; i < headersOut.Length; i++)
                    {
                        wsOut.Cell(1, i + 1).Value = headersOut[i];
                        wsOut.Cell(1, i + 1).Style.Font.Bold = true;
                        wsOut.Cell(1, i + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.Teal;
                        wsOut.Cell(1, i + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    }

                    for (int i = 0; i < recentPrep.Count; i++)
                    {
                        var r = recentPrep[i];
                        wsOut.Cell(i + 2, 1).Value = i + 1;
                        wsOut.Cell(i + 2, 2).Value = r.CreatedDate.ToString("dd/MM/yyyy");
                        wsOut.Cell(i + 2, 3).Value = r.CreatedDate.ToString("HH:mm:ss");
                        wsOut.Cell(i + 2, 4).Value = r.Tag;
                        wsOut.Cell(i + 2, 5).Value = r.Label;
                        wsOut.Cell(i + 2, 6).Value = r.Kanban;
                        wsOut.Cell(i + 2, 7).Value = r.CreatedBy;
                        if (format == "Sum") wsOut.Cell(i + 2, 8).Value = r.SumQty;
                    }
                    wsOut.Columns().AdjustToContents();
                }

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var content = stream.ToArray();
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TransactionLog_{plant}_{mode}_{dateStr}.xlsx");
                }
            }
        }

        public IActionResult Trend(string period = "Month", string mode = "Activity")
        {
            return RedirectToAction("TrendCriticalStock");
        }

        /// <summary>
        /// Admin endpoint untuk sync ulang Critical Stock (TANPA menghapus data pulling/preparation)
        /// Hanya refresh snapshot agar Critical Stock sinkron dengan Dashboard FG
        /// </summary>
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> CleanOldDataAndSync()
        {
            try
            {
                // TIDAK menghapus PullingRecords dan PreparationRecords!
                // Data pulling/preparation adalah data transaksi penting yang tidak boleh dihapus.
                
                // 1. Hapus snapshot lama saja (akan di-generate ulang)
                var todayStart = DateTime.Today;
                var oldSnapshots = await _context.StockSnapshots
                    .Where(s => s.SnapshotDate.Date < todayStart).ToListAsync();
                if (oldSnapshots.Any())
                    _context.StockSnapshots.RemoveRange(oldSnapshots);
                
                await _context.SaveChangesAsync();
                
                // 2. Trigger snapshot baru untuk sync Critical Stock dengan dashboard FG
                var (count, msg) = await _snapshotService.TakeSnapshotAsync(isManual: true);
                
                return Json(new { 
                    success = true, 
                    message = $"Sync completed! Snapshot refreshed: {count} items. Old snapshots cleaned: {oldSnapshots.Count}.",
                    deletedPulling = 0,
                    deletedPrep = 0,
                    deletedSnapshots = oldSnapshots.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Take manual snapshot — sync Critical Stock dengan Dashboard FG real-time
        /// </summary>
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> TakeManualSnapshot()
        {
            try
            {
                await _snapshotService.TakeSnapshotAsync(isManual: true);
                
                // Get updated counts — filter by active items only (IDENTIK dengan FG Dashboard)
                var activeVinSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var activeItemRows = await _context.Items
                    .Where(i => !i.IsDeleted && i.IsActive)
                    .Select(i => new { i.VIN, i.ItemCode })
                    .AsNoTracking()
                    .ToListAsync();
                foreach (var it in activeItemRows)
                {
                    if (!string.IsNullOrWhiteSpace(it.VIN))      activeVinSet.Add(it.VIN.Trim().ToUpper());
                    if (!string.IsNullOrWhiteSpace(it.ItemCode)) activeVinSet.Add(it.ItemCode.Trim().ToUpper());
                }

                var todaySnapshots = await _context.StockSnapshots
                    .Where(s => s.SnapshotDate.Date == DateTime.Today)
                    .GroupBy(s => s.ItemCode)
                    .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                    .ToListAsync();

                // Hanya hitung item aktif — sama seperti FG Dashboard indicator cards
                var activeSnapshots = todaySnapshots
                    .Where(s => 
                    {
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        if (string.IsNullOrEmpty(rawCode)) return false;
                        if (activeVinSet.Contains(rawCode)) return true;
                        if (rawCode.Contains('|'))
                        {
                            var parts = rawCode.Split('|');
                            if (parts.Length > 1)
                            {
                                var clean = parts[1].Trim();
                                if (activeVinSet.Contains(clean)) return true;
                                var cleanNoSuffix = clean.Split('_')[0];
                                if (activeVinSet.Contains(cleanNoSuffix)) return true;
                            }
                        }
                        return false;
                    })
                    .ToList();

                var shortage = activeSnapshots.Count(s => s.DaysCoverage < 1.0m);
                var normal   = activeSnapshots.Count(s => s.DaysCoverage >= 1.0m && s.DaysCoverage <= 2.0m);
                var over     = activeSnapshots.Count(s => s.DaysCoverage > 2.0m);
                
                return Json(new { 
                    success = true, 
                    message = $"Snapshot taken! Total: {activeSnapshots.Count} items. Shortage: {shortage}, Normal: {normal}, Over: {over}",
                    total = activeSnapshots.Count,
                    shortage,
                    normal,
                    over
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Bulk delete items dari Dashboard FG (hapus semua pulling records hari ini untuk itemId yang dipilih)
        /// </summary>
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> BulkDeletePulling([FromBody] List<int> itemIds)
        {
            try
            {
                if (itemIds == null || !itemIds.Any())
                    return Json(new { success = false, message = "Tidak ada item yang dipilih." });
                
                var todayStart = DateTime.Today;
                
                // Hapus semua pulling records hari ini untuk item-item yang dipilih
                var records = await _context.PullingRecords
                    .Where(p => itemIds.Contains(p.ItemId ?? 0) && p.CreatedDate >= todayStart)
                    .ToListAsync();
                
                if (!records.Any())
                    return Json(new { success = false, message = "Tidak ada data pulling hari ini untuk item yang dipilih." });
                
                _context.PullingRecords.RemoveRange(records);
                await _context.SaveChangesAsync();
                _stockCache.Invalidate();
                return Json(new { success = true, message = $"{records.Count} pulling records dihapus dari {itemIds.Count} item.", deleted = records.Count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Admin endpoint: Repair Plant pada PullingRecords & PreparationRecords yang kosong/salah.
        /// Mengisi Plant berdasarkan Item.Plant dari master data — fix data historis.
        /// </summary>
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> RepairPlantData()
        {
            try
            {
                // Load Item master (VIN → Plant & ItemId → Plant lookup)
                var allItems = await _context.Items.AsNoTracking()
                    .Where(i => !string.IsNullOrWhiteSpace(i.Plant))
                    .ToListAsync();
                var itemByVin = allItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => i.VIN!.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
                var itemById = allItems
                    .ToDictionary(i => i.ItemId, i => i.Plant ?? "");

                int fixedPulling = 0, fixedPrep = 0;

                // Fix PullingRecords dengan Plant kosong / "Unknown" / nilai tidak valid
                var badPulling = await _context.PullingRecords
                    .Where(p => p.Plant == null || p.Plant == ""
                             || (p.Plant != "Hose" && p.Plant != "Molded" && p.Plant != "RVI"))
                    .ToListAsync();

                foreach (var rec in badPulling)
                {
                    string resolvedPlant = "";
                    if (rec.ItemId.HasValue && itemById.TryGetValue(rec.ItemId.Value, out var ip) && !string.IsNullOrWhiteSpace(ip))
                        resolvedPlant = ip.Trim();
                    else if (!string.IsNullOrWhiteSpace(rec.Tag) && itemByVin.TryGetValue(rec.Tag.Trim().ToUpper(), out var ti))
                        resolvedPlant = (ti.Plant ?? "").Trim();

                    if (!string.IsNullOrWhiteSpace(resolvedPlant))
                    {
                        rec.Plant = resolvedPlant;
                        fixedPulling++;
                    }
                }

                // Fix PreparationRecords dengan Plant kosong / "-"
                var badPrep = await _context.PreparationRecords
                    .Where(p => p.Plant == null || p.Plant == "" || p.Plant == "-")
                    .ToListAsync();

                foreach (var rec in badPrep)
                {
                    if (string.IsNullOrWhiteSpace(rec.Tag)) continue;
                    if (itemByVin.TryGetValue(rec.Tag.Trim().ToUpper(), out var ti))
                    {
                        var resolvedPlant = (ti.Plant ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(resolvedPlant))
                        {
                            rec.Plant = resolvedPlant;
                            fixedPrep++;
                        }
                    }
                }

                // Fix PullingRecords dengan Plant valid TAPI salah (mismatch dengan Item.Plant)
                // Contoh: PullingRecord.Plant="Molded" tapi Item.Plant="RVI" → koreksi ke "RVI"
                int fixedMismatch = 0;
                var mismatchPulling = await _context.PullingRecords
                    .Where(p => (p.Plant == "Hose" || p.Plant == "Molded" || p.Plant == "RVI" || p.Plant == "BTR") && p.ItemId.HasValue)
                    .ToListAsync();
                foreach (var rec in mismatchPulling)
                {
                    if (!rec.ItemId.HasValue) continue;
                    if (!itemById.TryGetValue(rec.ItemId.Value, out var correctPlant)) continue;
                    if (string.IsNullOrWhiteSpace(correctPlant)) continue;
                    if (correctPlant != "Hose" && correctPlant != "Molded" && correctPlant != "RVI" && correctPlant != "BTR") continue;
                    if (string.Equals(rec.Plant, correctPlant, StringComparison.OrdinalIgnoreCase)) continue;
                    rec.Plant = correctPlant.Trim();
                    fixedMismatch++;
                    fixedPulling++;
                }

                // =========================================================
                // CLEANUP INVALID MOLDED RACK LOCATIONS
                // =========================================================
                int clearedMoldedRacks = 0;
                var allMoldedItemsDb = await _context.Items.AsNoTracking()
                    .Where(i => !i.IsDeleted && (i.Plant == "Molded" || i.ItemName == "Molded"))
                    .Select(i => new { 
                        VIN = (i.VIN ?? "").Trim().ToUpper(), 
                        ItemCode = (i.ItemCode ?? "").Trim().ToUpper(),
                        Rack = (i.Rack ?? "").Trim().ToUpper(),
                        NoRack = i.NoRack ?? 0
                    })
                    .ToListAsync();

                var validLocsByVin = allMoldedItemsDb
                    .Where(i => i.VIN != "")
                    .GroupBy(i => i.VIN, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => $"{x.Rack}_{x.NoRack}").ToHashSet(StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);

                var validLocsByCode = allMoldedItemsDb
                    .Where(i => i.ItemCode != "")
                    .GroupBy(i => i.ItemCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => $"{x.Rack}_{x.NoRack}").ToHashSet(StringComparer.OrdinalIgnoreCase),
                        StringComparer.OrdinalIgnoreCase);

                var activeMoldedPulling = await _context.PullingRecords
                    .Where(p => p.Plant == "Molded" && p.Remark != "Mismatch")
                    .ToListAsync();

                foreach (var rec in activeMoldedPulling)
                {
                    if (string.IsNullOrWhiteSpace(rec.Tag)) continue;
                    var tagKey = rec.Tag.Trim().ToUpper();
                    var rRack = (rec.Rack ?? "").Trim().ToUpper();
                    var rCol = rec.Column;
                    var locKey = $"{rRack}_{rCol}";

                    HashSet<string> validLocs = null;
                    if (validLocsByVin.TryGetValue(tagKey, out var vLocs)) validLocs = vLocs;
                    else if (validLocsByCode.TryGetValue(tagKey, out var cLocs)) validLocs = cLocs;

                    if (validLocs != null && !validLocs.Contains(locKey, StringComparer.OrdinalIgnoreCase))
                    {
                        rec.Remark = "Mismatch";
                        var clearNote = " | Cleared by Repair (Invalid Molded Rack)";
                        var baseNote = rec.AdjustNote ?? "";
                        if (baseNote.Length + clearNote.Length > 200) baseNote = baseNote.Substring(0, 200 - clearNote.Length);
                        rec.AdjustNote = baseNote + clearNote;
                        clearedMoldedRacks++;
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"✅ Repair selesai! Pulling: {fixedPulling} (mismatch: {fixedMismatch}), Prep: {fixedPrep}. Molded Invalid Racks Cleared: {clearedMoldedRacks}.",
                    fixedPulling,
                    fixedMismatch,
                    fixedPrep,
                    clearedMoldedRacks,
                    skippedPulling = badPulling.Count - (fixedPulling - fixedMismatch),
                    skippedPrep = badPrep.Count - fixedPrep
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Leader")]
        public async Task<IActionResult> Targets()
        {
            var items = await _context.Items.OrderBy(i => i.ItemCode).ToListAsync();
            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Leader")]
        public async Task<IActionResult> UpdateTargets(List<ItemTargetUpdate> updates)
        {
            if (updates == null || !updates.Any()) return RedirectToAction(nameof(Molded));

            foreach (var update in updates)
            {
                var item = await _context.Items.FindAsync(update.ItemId);
                if (item != null)
                {
                    item.Rack = update.Rack;
                    item.NoRack = update.NoRack;
                    item.QtyLot = update.QtyLot;
                    item.RackMin = update.RackMin;
                    item.RackMax = update.RackMax;
                    item.UpdatedDate = DateTime.Now;
                    _context.Update(item);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Stock targets updated successfully.";
            return RedirectToAction("Index", "Items");
        }

        public class ItemTargetUpdate
        {
            public int ItemId { get; set; }
            public string? Rack { get; set; }
            public int? NoRack { get; set; }
            public int? QtyLot { get; set; }
            public int? RackMin { get; set; }
            public int? RackMax { get; set; }
        }

        /// <summary>
        /// GET /Stock/DashboardControlFG — Halaman Dashboard Control FG (publik, tanpa login)
        /// </summary>
        public IActionResult DashboardControlFG()
        {
            return View();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OPNAME
        // ════════════════════════════════════════════════════════════════════════


        /// <summary>
        /// GET /Stock/Opname — Halaman Stock Opname (Super Admin &amp; STO only)
        /// </summary>
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin", "STO")]
        public IActionResult Opname()
        {
            return View();
        }

        /// <summary>
        /// GET /Stock/GetOpnameData?plant=Overall
        /// Ambil semua item aktif per baris lokasi rak (tidak di-group) untuk form opname.
        /// </summary>
        [HttpGet]
        [IgnoreAntiforgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin", "STO")]
        public async Task<IActionResult> GetOpnameData(string plant = "Overall")
        {
            // Selalu ambil semua data (Overall) — filter berdasarkan lokasi rak di bawah,
            // bukan berdasarkan Item.Plant, agar item yang raknya ada di plant ini tetap muncul
            // meski Item.Plant berbeda.
            // [FIX] Invalidate cache sebelum ambil data agar item yang baru dihapus tidak muncul kembali
            _stockCache.Invalidate();
            var dashboard = await GetStockViewModel("Overall", null, "Day", 1, "All", "", -1, "All", "All", "All", true);

            // ItemMapping untuk Part Number
            var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
            var mappingByVin = allMappings
                .GroupBy(m => {
                    var v = (m.VIN ?? "").Trim().ToUpper();
                    return v.EndsWith("LB") ? v[..^2] : v;
                })
                .ToDictionary(g => g.Key,
                    g => g.Select(x => (x.CustomerPartNumber ?? "").Trim())
                          .Where(p => !string.IsNullOrWhiteSpace(p))
                          .Distinct().ToList());

            // Query Items langsung dari DB untuk mendapatkan ItemName dan Plant yang otoritatif.
            // Ini menghindari masalah jika detail.ItemName di StockDetails tidak terisi dengan benar.
            var allItemsDb = await _context.Items.AsNoTracking()
                .Where(i => i.VIN != null && i.VIN != "")
                .Select(i => new { VIN = i.VIN!.Trim().ToUpper(), ItemName = (i.ItemName ?? "").Trim(), Plant = (i.Plant ?? "").Trim() })
                .ToListAsync();
            // Jika VIN sama, ambil yang ItemName-nya diketahui (Molded/Hose/RVI/BTR) terlebih dahulu
            var itemAreaByVin = allItemsDb
                .GroupBy(i => i.VIN, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(i => i.ItemName == "Molded" || i.ItemName == "Hose" || i.ItemName == "RVI" || i.ItemName == "BTR" ? 1 : 0).First(),
                    StringComparer.OrdinalIgnoreCase);

            // Kumpulkan dulu ke list sementara agar bisa diurutkan berdasarkan lokasi rak (abjad)
            var tempRows = new List<(string SortRack, int SortNoRack, object Row)>();

            // Set nama area rak yang valid (BTR = lokasi rak baru)
            var knownAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Molded", "Hose", "RVI", "BTR" };

            // Semua item — termasuk Inactive — perlu tampil di opname agar stok fisik bisa dikoreksi.
            foreach (var detail in dashboard.StockDetails)
            {
                var vin = (detail.VIN ?? "").Trim().ToUpper();
                var partNumber = vin != "" && mappingByVin.ContainsKey(vin)
                    ? mappingByVin[vin]
                        .OrderByDescending(p => p.Count(char.IsLetterOrDigit))
                        .ThenByDescending(p => p.Length)
                        .FirstOrDefault() ?? ""
                    : "";

                // Tentukan area rak item ini.
                // Prioritas:
                // 1. Jika detail.ItemName sudah berupa area yang valid (Molded/Hose/RVI), gunakan itu (agar konsisten dengan UI).
                // 2. Jika tidak, coba ambil dari DB (itemAreaByVin)
                // 3. Fallback ke detail.Plant
                string rawItemName = (detail.ItemName ?? "").Trim();
                string rawPlant = (detail.Plant ?? "").Trim();

                if (!knownAreas.Contains(rawItemName) && !vin.StartsWith("__") && itemAreaByVin.TryGetValue(vin, out var itemInfo))
                {
                    rawItemName = itemInfo.ItemName;
                    rawPlant    = itemInfo.Plant;
                }

                var itemRackArea = knownAreas.Contains(rawItemName)
                    ? rawItemName
                    : rawPlant;

                // Filter berdasarkan area rak item (bukan PullingRecord.Plant)
                if (plant != "Overall" && !itemRackArea.Equals(plant, StringComparison.OrdinalIgnoreCase))
                    continue;

                var racks = detail.RackBreakdown.OrderBy(r => r.Rack).ThenBy(r => r.Column).ToList();

                if (!racks.Any())
                {
                    tempRows.Add(("-", 0, (object)new
                    {
                        vin      = detail.VIN ?? "-",
                        itemName = detail.ItemName,
                        partNo   = partNumber,
                        rack     = "-",
                        noRack   = 0,
                        lokasi   = "Tanpa Rak",
                        qpc      = detail.QtyLot ?? 1,
                        act      = (int)detail.CurrentStock,
                        totalAct = (int)detail.CurrentStock,
                        min      = detail.Min ?? 0,
                        rop      = detail.Rop ?? 0,
                        max      = detail.Max ?? 0,
                        level    = Math.Round((double)detail.LevelStock, 2),
                        status   = detail.Status,
                        isActive = detail.IsActive,
                        statusItem = detail.StatusItem,
                        plant    = itemRackArea != "" ? itemRackArea : "-",
                        itemCode = detail.Tag ?? "",
                        itemId   = detail.ItemId
                    }));
                }
                else
                {
                    foreach (var rb in racks)
                    {
                        tempRows.Add((rb.Rack.Trim().ToUpper(), rb.Column, (object)new
                        {
                            vin      = detail.VIN ?? "-",
                            itemName = detail.ItemName,
                            partNo   = partNumber,
                            rack     = rb.Rack,
                            noRack   = rb.Column,
                            lokasi   = rb.Column > 0 ? $"Rak {rb.Rack}{rb.Column}" : $"Rak {rb.Rack}",
                            qpc      = detail.QtyLot ?? 1,
                            act      = rb.Stock,
                            totalAct = (int)detail.CurrentStock,
                            min      = detail.Min ?? 0,
                            rop      = detail.Rop ?? 0,
                            max      = detail.Max ?? 0,
                            level    = Math.Round((double)detail.LevelStock, 2),
                            status   = detail.Status,
                            isActive = detail.IsActive,
                            statusItem = detail.StatusItem,
                            plant    = itemRackArea != "" ? itemRackArea : (detail.Plant ?? "-"),
                            itemCode = detail.Tag ?? "",
                            itemId   = detail.ItemId
                        }));
                    }
                }
            }

            // Urutkan berdasarkan lokasi rak (abjad), kemudian nomor kolom
            var sorted = tempRows
                .OrderBy(t => t.SortRack)
                .ThenBy(t => t.SortNoRack)
                .Select((t, i) => t.Row)
                .ToList();

            return Json(sorted);
        }

        /// <summary>
        /// POST /Stock/SaveOpname — Simpan hasil opname.
        /// Untuk setiap VIN, hitung selisih qty vs stok dashboard FIFO saat ini,
        /// lalu buat adjusting pulling record (tambah stok) atau preparation record (kurangi stok).
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin", "STO")]
        public async Task<IActionResult> SaveOpname([FromBody] OpnameSaveRequest req)
        {
            if (req?.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Tidak ada data opname." });

            var username = HttpContext.Session.GetString("Username") ?? "STO";
            var now = DateTime.Now;
            int adjustedItems = 0, addedPulling = 0, addedPrep = 0;
            int msOffset = 0;

            // Item master untuk lookup Plant, ItemId
            var allItems = await _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted)
                .ToListAsync();
            var itemByVin = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                .GroupBy(i => i.VIN!.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
            var itemByCode = allItems
                .GroupBy(i => (i.ItemCode ?? "").Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

            // Proses setiap baris opname secara individual (per-rack, bukan aggregate per-VIN)
            // Ini memastikan perubahan offset antar-rak (misal +2 di Rak D, -2 di Rak D12) benar-benar tersimpan.
            foreach (var row in req.Items)
            {
                var vinKey = (row.VIN ?? row.ItemCode ?? "").Trim().ToUpper();
                if (string.IsNullOrEmpty(vinKey)) continue;

                // Resolve item master
                itemByVin.TryGetValue(vinKey, out var resolvedItem);
                if (resolvedItem == null)
                {
                    var codeKey = (row.ItemCode ?? "").Trim().ToUpper();
                    itemByCode.TryGetValue(codeKey, out resolvedItem);
                }

                var itemPlant = resolvedItem?.Plant ?? row.Plant ?? "Unknown";
                var itemId    = resolvedItem?.ItemId ?? row.ItemId;
                var tag       = resolvedItem?.VIN ?? row.VIN ?? row.ItemCode ?? vinKey;
                var oldRack   = (row.Rack ?? "").Trim().ToUpper();
                var oldNoRack = row.NoRack;

                // Cek apakah lokasi rak berubah
                var newRack   = !string.IsNullOrEmpty(row.NewRack) ? row.NewRack.Trim().ToUpper() : oldRack;
                var newNoRack = row.NewNoRack ?? oldNoRack;
                bool rackChanged = !string.Equals(newRack, oldRack, StringComparison.OrdinalIgnoreCase)
                                || newNoRack != oldNoRack;

                int newQty = Math.Max(0, row.NewQty);
                int act    = Math.Max(0, row.Act);

                // Cek apakah lokasi rak dihapus
                if (row.IsDeleted)
                {
                    // Bersihkan semua record aktif di rak ini
                    var allAtRack = await _context.PullingRecords
                        .Where(p => p.Tag == tag
                                 && (p.Rack ?? "").Trim().ToUpper() == oldRack
                                 && p.Column == oldNoRack
                                 && p.Remark != "Mismatch")
                        .ToListAsync();

                    foreach (var r in allAtRack)
                    {
                        r.Remark = "Mismatch";
                        var clearNote = " | Rack Removed (Opname)";
                        var baseNote = r.AdjustNote ?? "";
                        if (baseNote.Length + clearNote.Length > 200) baseNote = baseNote.Substring(0, 200 - clearNote.Length);
                        r.AdjustNote = baseNote + clearNote;
                    }
                    adjustedItems++;
                    continue;
                }

                if (rackChanged)
                {
                    // 1. Bersihkan SEMUA record aktif di rak LAMA → Mismatch
                    var allAtOldRack = await _context.PullingRecords
                        .Where(p => p.Tag == tag
                                 && (p.Rack ?? "").Trim().ToUpper() == oldRack
                                 && p.Column == oldNoRack
                                 && p.Remark != "Mismatch")
                        .ToListAsync();

                    foreach (var r in allAtOldRack)
                    {
                        r.Remark = "Mismatch";
                        var moveNote = " | Moved to " + newRack + "." + newNoRack + " (Opname)";
                        var baseNote = r.AdjustNote ?? "";
                        if (baseNote.Length + moveNote.Length > 200) baseNote = baseNote.Substring(0, 200 - moveNote.Length);
                        r.AdjustNote = baseNote + moveNote;
                    }
                    // [FIX] Jangan bersihkan record di rak TUJUAN. Jika user memindah rak, 
                    // stok tersebut harus DITAMBAHKAN ke rak tujuan tanpa menghapus stok yang sudah ada di sana.
                    // (Menghapus rak tujuan akan menyebabkan data hilang jika sebelumnya sudah ada item di rak tujuan).

                    // 2. Tambahkan newQty record baru di rak tujuan (dengan unique label agar FIFO tidak bocor)
                    if (newQty > 0)
                    {
                        var batchLabel = tag + "LB" + newRack + newNoRack + "U" + now.Ticks.ToString().Substring(10);
                        if (batchLabel.Length > 100) batchLabel = batchLabel.Substring(0, 100);
                        for (int i = 0; i < newQty; i++)
                        {
                            _context.PullingRecords.Add(new PullingRecord
                            {
                                ItemId         = itemId,
                                Tag            = tag,
                                Label          = batchLabel,
                                Plant          = itemPlant,
                                Rack           = newRack,
                                Column         = newNoRack,
                                Quantity       = 1,
                                IsManualAdjust = true,
                                AdjustNote     = "Opname Adjust",
                                CreatedDate    = now.AddMilliseconds(msOffset++),
                                CreatedBy      = username
                            });
                            addedPulling++;
                        }
                    }
                    else
                    {
                        // newQty = 0: Zero Stock marker di rak tujuan agar item tetap tampil dengan stock=0
                        _context.PullingRecords.Add(new PullingRecord
                        {
                            ItemId         = itemId,
                            Tag            = tag,
                            Label          = tag + "-ZERO",
                            Plant          = itemPlant,
                            Rack           = newRack,
                            Column         = newNoRack,
                            Quantity       = 0,
                            IsManualAdjust = true,
                            AdjustNote     = "Zero Stock",
                            CreatedDate    = now.AddMilliseconds(msOffset++),
                            CreatedBy      = username
                        });
                    }

                    adjustedItems++;
                }
                else
                {
                    // Rack sama: reset absolut — clear semua record aktif di rak ini, lalu buat newQty baru.
                    // Menggunakan diff (selisih) tidak aman karena FIFO mungkin tidak sinkron 100% dengan ACT
                    // yang ditampilkan, sehingga hasil akhir bisa berbeda dari yang diinput user.
                    // [FIX] Hapus pengecekan diff == 0. 
                    // Kita harus selalu melakukan absolute reset tanpa mempedulikan nilai 'act' (stok di layar user).
                    // Hal ini mencegah kegagalan Opname jika stok asli di database diam-diam berubah (stale data).

                    adjustedItems++;

                    // Bersihkan semua record aktif di rak ini untuk item ini
                    var allAtRack = await _context.PullingRecords
                        .Where(p => p.Tag == tag
                                 && (p.Rack ?? "").Trim().ToUpper() == oldRack
                                 && p.Column == oldNoRack
                                 && p.Remark != "Mismatch")
                        .ToListAsync();

                    foreach (var r in allAtRack)
                    {
                        r.Remark = "Mismatch";
                        var clearNote = " | Cleared by Opname (absolute reset)";
                        var baseNote = r.AdjustNote ?? "";
                        if (baseNote.Length + clearNote.Length > 200) baseNote = baseNote.Substring(0, 200 - clearNote.Length);
                        r.AdjustNote = baseNote + clearNote;
                    }

                    if (newQty > 0)
                    {
                        var batchLabel = tag + "LB" + oldRack + oldNoRack + "U" + now.Ticks.ToString().Substring(10);
                        if (batchLabel.Length > 100) batchLabel = batchLabel.Substring(0, 100);
                        for (int i = 0; i < newQty; i++)
                        {
                            _context.PullingRecords.Add(new PullingRecord
                            {
                                ItemId         = itemId,
                                Tag            = tag,
                                Label          = batchLabel,
                                Plant          = itemPlant,
                                Rack           = oldRack,
                                Column         = oldNoRack,
                                Quantity       = 1,
                                IsManualAdjust = true,
                                AdjustNote     = "Opname Adjust",
                                CreatedDate    = now.AddMilliseconds(msOffset++),
                                CreatedBy      = username
                            });
                        }
                        addedPulling += newQty;
                    }
                    else
                    {
                        // newQty = 0: Zero Stock marker agar item tetap tampil di dashboard dengan stock=0
                        _context.PullingRecords.Add(new PullingRecord
                        {
                            ItemId         = itemId,
                            Tag            = tag,
                            Label          = tag + "-ZERO",
                            Plant          = itemPlant,
                            Rack           = oldRack,
                            Column         = oldNoRack,
                            Quantity       = 0,
                            IsManualAdjust = true,
                            AdjustNote     = "Zero Stock",
                            CreatedDate    = now.AddMilliseconds(msOffset++),
                            CreatedBy      = username
                        });
                    }
                }
            }

            await _context.SaveChangesAsync();
            _stockCache.Invalidate();

            return Json(new
            {
                success      = true,
                message      = $"Opname disimpan! {adjustedItems} baris disesuaikan. +{addedPulling} pcs bertambah, -{addedPrep} pcs berkurang.",
                adjustedItems,
                addedPulling,
                addedPrep
            });
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> ImportStockExcel(IFormFile importExcelFile)
        {
            if (importExcelFile == null || importExcelFile.Length == 0)
                return Json(new { success = false, message = "File tidak ditemukan." });

            try
            {
                using var stream = new MemoryStream();
                await importExcelFile.CopyToAsync(stream);
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                var ws = workbook.Worksheets.FirstOrDefault();
                if (ws == null)
                    return Json(new { success = false, message = "Worksheet tidak ditemukan." });

                var allRows = ws.RangeUsed().RowsUsed().ToList();
                
                // Cari baris header (baris yang mengandung teks 'VIN')
                var headerRow = allRows.FirstOrDefault(r => r.CellsUsed().Any(c => c.GetString().Trim().Equals("VIN", StringComparison.OrdinalIgnoreCase) || c.GetString().Trim().Equals("PART KODE (VIN)", StringComparison.OrdinalIgnoreCase)));
                
                if (headerRow == null)
                {
                    return Json(new { success = false, message = "Kolom VIN tidak ditemukan di file Excel. Pastikan format header benar." });
                }

                var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in headerRow.CellsUsed())
                {
                    var header = cell.GetString().Trim().ToUpper();
                    colMap[header] = cell.Address.ColumnNumber;
                }

                // Fungsi bantu untuk menghindari error evaluasi formula 'References from other files are not yet implemented'
                static string SafeGetString(ClosedXML.Excel.IXLCell cell)
                {
                    try { return cell.GetFormattedString()?.Trim() ?? ""; }
                    catch { try { return cell.Value.ToString()?.Trim() ?? ""; } catch { return ""; } }
                }

                // Fungsi bantu untuk mencari index kolom
                int GetCol(params string[] keys)
                {
                    // Exact match first
                    foreach (var k in keys)
                    {
                        var match = colMap.Keys.FirstOrDefault(x => x.Equals(k, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return colMap[match];
                    }
                    
                    // Fallback for min, rop, max which might have suffixes like " 1D"
                    foreach (var k in keys)
                    {
                        if (k == "MIN" || k == "ROP" || k == "MAX") 
                        {
                            var match = colMap.Keys.FirstOrDefault(x => x.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase));
                            if (match != null) return colMap[match];
                        }
                    }
                    return -1;
                }

                int colVin = GetCol("VIN");
                int colPlant = GetCol("PROD. PLANT", "PLANT");
                int colRack = GetCol("RACK", "RAK");
                int colNoRack = GetCol("NO RACK", "NO RAK");
                int colCust = GetCol("CUST");
                int colStatus = GetCol("STATUS");
                int colProd = GetCol("PROD");
                int colQpc = GetCol("QPC");
                int colMin = GetCol("MIN");
                int colRop = GetCol("ROP");
                int colMax = GetCol("MAX");
                int colAct = GetCol("ACT", "STOCK", "AKTUAL", "ACTUAL", "QTY", "STOCK FG");

                // Fallback: Jika header kolom ACT berisi tanggal/angka serial (bukan teks), 
                // ambil kolom tepat setelah MAX (kolom N, setelah kolom M=MAX)
                if (colAct == -1 && colMax > 0)
                {
                    var maxColPlusOne = colMax + 1;
                    var lastUsedCol = headerRow.CellsUsed().Max(c => c.Address.ColumnNumber);
                    if (maxColPlusOne <= lastUsedCol)
                        colAct = maxColPlusOne;
                    else
                    {
                        var lastCell = headerRow.CellsUsed().LastOrDefault();
                        if (lastCell != null) colAct = lastCell.Address.ColumnNumber;
                    }
                }

                var dataRows = allRows.Where(r => r.RowNumber() > headerRow.RowNumber());

                int updatedItemsCount = 0;
                int createdNewItemsCount = 0;
                var plantSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int notFoundCount = 0;
                int addedPulling = 0;
                int addedPrep = 0;
                var username = HttpContext.Session.GetString("Username") ?? "System";
                var now = DateTime.Now;
                int msOffset = 0;

                Func<Task> saveChangesAsync = async () =>
                {
                    int retries = 5;
                    while(true)
                    {
                        try { await _context.SaveChangesAsync(); break; }
                        catch (Exception ex) when (ex.ToString().Contains("database is locked") && retries-- > 0)
                        {
                            await Task.Delay(1000);
                        }
                    }
                };

                // Load all items to dictionary (include deleted to prevent unique constraint errors)
                var allItems = await _context.Items.ToListAsync();

                // Lookup by composite key "VIN|CUSTOMER" → item (utama untuk VIN yang bisa duplikat antar customer)
                var itemByVinCust = allItems
                    .Where(x => !string.IsNullOrWhiteSpace(x.VIN))
                    .GroupBy(x => $"{x.VIN!.Trim().ToUpper()}|{(x.Customer ?? "").Trim().ToUpper()}")
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).First(),
                        StringComparer.OrdinalIgnoreCase);

                // Fallback lookup by VIN alone (untuk baris Excel yang tidak punya kolom CUST)
                var itemByVin = allItems.Where(x => !string.IsNullOrWhiteSpace(x.VIN))
                                        .GroupBy(x => x.VIN!.Trim().ToUpper())
                                        .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

                var itemByCode = allItems.Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                                         .GroupBy(x => x.ItemCode!.Trim().ToUpper())
                                         .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

                var failedVins = new List<string>();
                var processedItemIds = new HashSet<int>();
                
                // Tambahan: melacak Lokasi Rak & Kolom mana saja yang tertulis di Excel untuk tiap VIN+Customer
                // Key: "VIN|CUSTOMER" → set of (Rack, Column)
                var touchedLocations = new Dictionary<string, HashSet<(string Rack, int Column)>>(StringComparer.OrdinalIgnoreCase);

                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                // ======================================================================
                // PRE-STEP: Kumpulkan semua VIN unik dari Excel, lalu hapus semua record
                // Opname Adjust dan OPNAME-REDUCE lama. Ini memastikan import bersifat
                // IDEMPOTEN: upload file yang sama selalu menghasilkan angka yang sama.
                // Sekarang key = "VIN|CUSTOMER" agar VIN yang sama dari customer berbeda TIDAK dianggap duplikat.
                // ======================================================================
                var excelVinCustSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // key: "VIN|CUST"
                var excelVinSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // key: VIN saja (untuk delete opname lama)
                var duplicateVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // hanya laporan, bukan block
                
                foreach (var row in dataRows)
                {
                    var v = colVin > 0 ? SafeGetString(row.Cell(colVin)).ToUpper() : null;
                    var c = colCust > 0 ? SafeGetString(row.Cell(colCust)).ToUpper() : "";
                    if (!string.IsNullOrEmpty(v))
                    {
                        excelVinSet.Add(v);
                        var combinedKey = $"{v}|{c}";
                        if (!excelVinCustSet.Add(combinedKey))
                        {
                            // Hanya anggap duplikat jika VIN+CUSTOMER sama persis
                            duplicateVins.Add($"{v} ({c})");
                        }
                    }
                }

                // Load & delete stale Opname Adjust + Zero Stock pulling records
                var allOldOpnameAdjust = await _context.PullingRecords
                    .Where(p => p.IsManualAdjust == true
                           && (p.AdjustNote == "Opname Adjust" || p.AdjustNote == "Zero Stock"))
                    .ToListAsync();
                var toDeleteOpnameAdjust = allOldOpnameAdjust
                    .Where(p => excelVinSet.Contains((p.Tag ?? "").Trim().ToUpper()))
                    .ToList();
                _context.PullingRecords.RemoveRange(toDeleteOpnameAdjust);

                // Load & delete stale OPNAME-REDUCE prep records
                var allOldOpnameReduce = await _context.PreparationRecords
                    .Where(p => p.Kanban == "OPNAME-REDUCE")
                    .ToListAsync();
                var toDeleteOpnameReduce = allOldOpnameReduce
                    .Where(p => excelVinSet.Contains((p.Tag ?? "").Trim().ToUpper()))
                    .ToList();
                _context.PreparationRecords.RemoveRange(toDeleteOpnameReduce);

                // Commit deletion sebelum proses opname baru
                await saveChangesAsync();
                // ======================================================================
                // PRE-FETCH BULK DATA (Optimization to avoid N+1 queries)
                // ======================================================================
                var allTagsToFetch = new HashSet<string>(excelVinSet, StringComparer.OrdinalIgnoreCase);
                foreach (var dbItem in allItems)
                {
                    var tag = !string.IsNullOrWhiteSpace(dbItem.VIN) ? dbItem.VIN : dbItem.ItemCode;
                    if (!string.IsNullOrEmpty(tag))
                    {
                        allTagsToFetch.Add(tag.Trim());
                    }
                }

                var pullingDict = new Dictionary<string, List<DeliveryControl.Models.PullingRecord>>(StringComparer.OrdinalIgnoreCase);
                var prepDict = new Dictionary<string, List<DeliveryControl.Models.PreparationRecord>>(StringComparer.OrdinalIgnoreCase);

                foreach (var chunk in allTagsToFetch.Chunk(500))
                {
                    var chunkUpper = chunk.Select(c => c.ToUpper()).ToList();
                    var pullChunk = await _context.PullingRecords
                        .Where(p => p.Tag != null && chunkUpper.Contains(p.Tag.ToUpper()) && p.Remark != "Mismatch")
                        .ToListAsync();
                    foreach (var p in pullChunk)
                    {
                        var t = p.Tag!.Trim();
                        if (!pullingDict.ContainsKey(t)) pullingDict[t] = new List<DeliveryControl.Models.PullingRecord>();
                        pullingDict[t].Add(p);
                    }

                    var prepChunk = await _context.PreparationRecords
                        .Where(p => p.Tag != null && chunkUpper.Contains(p.Tag.ToUpper()) && p.Remark != "Mismatch")
                        .ToListAsync();
                    foreach (var p in prepChunk)
                    {
                        var t = p.Tag!.Trim();
                        if (!prepDict.ContainsKey(t)) prepDict[t] = new List<DeliveryControl.Models.PreparationRecord>();
                        prepDict[t].Add(p);
                    }
                }
                // ======================================================================

                var failedVinsList = new List<string>();
                var errorMessages = new List<string>();
                int successCount = 0;

                foreach (var row in dataRows)
                {
                    var vinStr = colVin > 0 ? SafeGetString(row.Cell(colVin)) : null;
                    if (string.IsNullOrEmpty(vinStr)) continue;

                    try
                    {

                    var prodPlantStr = colPlant > 0 ? SafeGetString(row.Cell(colPlant)) : null;
                    var custStr = colCust > 0 ? SafeGetString(row.Cell(colCust)) : null;
                    var statusStr = colStatus > 0 ? SafeGetString(row.Cell(colStatus)) : null;
                    var prodStr = colProd > 0 ? SafeGetString(row.Cell(colProd)) : null;

                    var qpcStr = colQpc > 0 ? SafeGetString(row.Cell(colQpc)) : null;
                    var minStr = colMin > 0 ? SafeGetString(row.Cell(colMin)) : null;
                    var ropStr = colRop > 0 ? SafeGetString(row.Cell(colRop)) : null;
                    var maxStr = colMax > 0 ? SafeGetString(row.Cell(colMax)) : null;
                    var actStr = colAct > 0 ? SafeGetString(row.Cell(colAct)) : null;

                    bool isNewItem = false;
                    // Cari item dengan composite key VIN+CUSTOMER terlebih dahulu
                    var vinCustKey = $"{vinStr.Trim().ToUpper()}|{(custStr ?? "").Trim().ToUpper()}";
                    if (!itemByVinCust.TryGetValue(vinCustKey, out var item))
                    {
                        if (!string.IsNullOrEmpty(custStr))
                        {
                            // Ada customer di Excel — coba cari di allItems dengan case-insensitive match
                            item = allItems.FirstOrDefault(i =>
                                string.Equals((i.VIN ?? "").Trim(), vinStr.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                string.Equals((i.Customer ?? "").Trim(), custStr.Trim(), StringComparison.OrdinalIgnoreCase));
                            // Jika benar-benar tidak ada → akan buat item baru (item tetap null)
                        }
                        else if (!itemByVin.TryGetValue(vinStr, out item))
                        {
                            if (!itemByCode.TryGetValue(vinStr, out item))
                                item = null;
                        }
                    }

                    if (item == null)
                    {
                        isNewItem = true;
                        // Auto-create item baru jika belum ada di database
                        // ItemCode HARUS unik (UNIQUE constraint di DB).
                        // Jika VIN sudah ada di DB (misal dari customer lain), buat ItemCode = "VIN-CUST" agar tidak tabrakan.
                        string baseCode = vinStr.Trim().ToUpper();
                        string custSuffix = !string.IsNullOrWhiteSpace(custStr) 
                            ? "-" + custStr.Trim().ToUpper().Replace(" ", "") 
                            : "";
                        // Cek apakah baseCode sudah dipakai oleh item lain (VIN berbeda customer)
                        string uniqueItemCode = itemByCode.ContainsKey(baseCode)
                            ? baseCode + custSuffix
                            : baseCode;
                        // Jika masih tabrakan (misal customer suffix juga sama), tambahkan angka
                        int safeguard = 1;
                        while (itemByCode.ContainsKey(uniqueItemCode) && safeguard < 99)
                        {
                            uniqueItemCode = baseCode + custSuffix + safeguard++;
                        }

                        item = new DeliveryControl.Models.Item
                        {
                            VIN          = vinStr.ToUpper(),
                            ItemCode     = uniqueItemCode,
                            ItemName     = vinStr.ToUpper(),
                            Plant        = !string.IsNullOrWhiteSpace(prodPlantStr) ? prodPlantStr : "Hose",
                            Customer     = custStr ?? "",
                            StatusItem   = !string.IsNullOrWhiteSpace(statusStr) ? statusStr : "Reguler",
                            Category     = prodStr ?? "Internal",
                            QtyLot       = int.TryParse(qpcStr, out int q) ? q : 0,
                            RackMin      = int.TryParse(minStr, out int mn) ? mn : 0,
                            ROP          = int.TryParse(ropStr, out int rp) ? rp : 0,
                            RackMax      = int.TryParse(maxStr, out int mx) ? mx : 0,
                            IsActive     = true,
                            IsDeleted    = false,
                            CreatedDate  = now
                        };
                        _context.Items.Add(item);

                        allItems.Add(item);
                        itemByVinCust[vinCustKey] = item;
                        itemByVin[vinStr.ToUpper()] = item;
                        itemByCode[vinStr.ToUpper()] = item;
                        createdNewItemsCount++;
                    }

                    var rackStr = colRack > 0 ? SafeGetString(row.Cell(colRack)) : null;
                    if (string.IsNullOrEmpty(rackStr)) rackStr = "-";
                    
                    var noRackStr = colNoRack > 0 ? SafeGetString(row.Cell(colNoRack)) : null;

                    // Update Master Item
                    bool itemUpdated = false;
                    if (int.TryParse(qpcStr, out int qpc) && item.QtyLot != qpc) { item.QtyLot = qpc; itemUpdated = true; }
                    if (int.TryParse(minStr, out int min) && item.RackMin != min) { item.RackMin = min; itemUpdated = true; }
                    if (int.TryParse(ropStr, out int rop) && item.ROP != rop) { item.ROP = rop; itemUpdated = true; }
                    if (int.TryParse(maxStr, out int max) && item.RackMax != max) { item.RackMax = max; itemUpdated = true; }
                    
                    if (!string.IsNullOrEmpty(custStr) && item.Customer != custStr) { item.Customer = custStr; itemUpdated = true; }
                    // Default blank status to "Reguler" for existing items to prevent them from staying "No Order" if they were previously No Order
                    if (string.IsNullOrWhiteSpace(statusStr)) statusStr = "Reguler";

                    if (!string.Equals(item.StatusItem, statusStr, StringComparison.OrdinalIgnoreCase))
                    {
                        item.StatusItem = statusStr; 
                        itemUpdated = true;
                    }
                    if (!item.IsActive) { item.IsActive = true; itemUpdated = true; }
                    if (item.IsDeleted) { item.IsDeleted = false; itemUpdated = true; }

                    if (!string.IsNullOrEmpty(prodStr) && item.Category != prodStr) { item.Category = prodStr; itemUpdated = true; }
                    if (!string.IsNullOrEmpty(prodPlantStr))
                    {
                        var normPlant = prodPlantStr.Trim().ToUpper();
                        if (normPlant == "MOLDED") prodPlantStr = "Molded";
                        else if (normPlant == "HOSE") prodPlantStr = "Hose";
                        else if (normPlant == "BTR") prodPlantStr = "BTR";
                        else if (normPlant == "RVI") prodPlantStr = "RVI";
                        else prodPlantStr = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(prodPlantStr.ToLower());

                        if (item.Plant != prodPlantStr) { item.Plant = prodPlantStr; itemUpdated = true; }
                    }
                    
                    var matches = System.Text.RegularExpressions.Regex.Matches(noRackStr ?? "", @"\d+");
                    var parsedNoRacks = new List<int>();
                    if (matches.Count > 0)
                    {
                        foreach (System.Text.RegularExpressions.Match m in matches)
                        {
                            parsedNoRacks.Add(int.Parse(m.Value));
                        }
                    }
                    // Fallback: pastikan parsedNoRacks tidak kosong agar opname tetap diproses
                    if (parsedNoRacks.Count == 0)
                    {
                        parsedNoRacks.Add(item.NoRack.HasValue && item.NoRack.Value > 0 ? item.NoRack.Value : 1);
                    }

                    // Tambahan: Daftarkan lokasi ini ke touchedLocations menggunakan key VIN+Customer
                    if (!string.IsNullOrEmpty(item.VIN))
                    {
                        var locKey = $"{item.VIN!.Trim().ToUpper()}|{(item.Customer ?? "").Trim().ToUpper()}";
                        if (!touchedLocations.ContainsKey(locKey))
                            touchedLocations[locKey] = new HashSet<(string Rack, int Column)>();
                        
                        foreach (var nR in parsedNoRacks)
                        {
                            touchedLocations[locKey].Add(((rackStr ?? "-").Trim().ToUpper(), nR));
                        }
                    }

                    int firstNoRack = parsedNoRacks.FirstOrDefault();
                    int? finalNoRack = firstNoRack > 0 ? firstNoRack : null;
                    
                    // Update Rack dan No Rack di master selalu mengikuti lokasi PERTAMA yang ditemukan di Excel (sesuai standar)
                    if (item.Rack != rackStr) { item.Rack = rackStr; itemUpdated = true; }
                    if (item.NoRack != finalNoRack) { item.NoRack = finalNoRack; itemUpdated = true; }

                    if (itemUpdated && !isNewItem) _context.Update(item);
                    processedItemIds.Add(item.ItemId);

                    // Opname Adjust Logic for this Rack
                    // Jika rackStr = "-" tapi ada nilai ACT → anggap stok 0 (item tidak punya lokasi rak)
                    // Jika rackStr != "-" dan parsedNoRacks ada → proses opname normal
                    if (int.TryParse(actStr, out int totalNewQty) && parsedNoRacks.Count > 0)
                    {
                        totalNewQty = Math.Max(0, totalNewQty);
                        
                        // Skip opname jika rak tidak valid ("-") DAN ACT > 0 (tidak mungkin ada stok tanpa rak)
                        if (rackStr == "-" && totalNewQty > 0) { updatedItemsCount++; continue; }
                        // Jika rak "-" dan ACT = 0, lewati saja (tidak perlu Zero Stock marker untuk item tanpa rak)
                        if (rackStr == "-") { updatedItemsCount++; continue; }


                        // 1. Ambil SEMUA Pulling dan Preparation untuk VIN ini dari dictionary memory (menghindari query N+1)
                        var itemVin = (item.VIN ?? "").Trim();
                        var allPullingForVin = pullingDict.ContainsKey(itemVin) ? pullingDict[itemVin] : new List<DeliveryControl.Models.PullingRecord>();
                        var allPrepForVin = prepDict.ContainsKey(itemVin) ? prepDict[itemVin] : new List<DeliveryControl.Models.PreparationRecord>();
                        
                        // Lakukan pairing FIFO standar
                        var consumedIds = new HashSet<int>();
                        var pullingLookup = allPullingForVin.ToLookup(p => (p.Tag ?? "").Trim().ToUpper());
                        
                        foreach (var prep in allPrepForVin)
                        {
                            var prepTag   = (prep.Tag   ?? "").Trim().ToUpper();
                            var prepLabel = (prep.Label ?? "").Trim().ToUpper();
                            var candidates = pullingLookup[prepTag];
                            
                            var match = candidates.FirstOrDefault(p =>
                                !consumedIds.Contains(p.PullingId) &&
                                (p.Label ?? "").Trim().ToUpper() == prepLabel &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                                
                            if (match == null)
                            {
                                match = candidates.FirstOrDefault(p =>
                                    !consumedIds.Contains(p.PullingId) &&
                                    (p.AdjustNote ?? "") != "Zero Stock" &&
                                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                            }
                            if (match != null) consumedIds.Add(match.PullingId);
                        }

                        // Filter hanya PullingRecords yang belum terkonsumsi dan berada di Rak/Kolom ini
                        var activePiecesForRacks = allPullingForVin
                            .Where(p => !consumedIds.Contains(p.PullingId) 
                                     && (p.AdjustNote ?? "") != "Zero Stock"
                                     && (p.Rack ?? "").Trim().ToUpper() == rackStr.ToUpper()
                                     && parsedNoRacks.Contains(p.Column))
                            .ToList();
                            
                        int totalCurrentAct = activePiecesForRacks.Count;

                        // Pastikan record stok yang sudah ada juga diupdate Labelnya jika belum memiliki LB
                        foreach (var p in activePiecesForRacks)
                        {
                            if (p.IsManualAdjust && (p.AdjustNote ?? "") == "Opname Adjust" && !(p.Label ?? "").EndsWith("LB", StringComparison.OrdinalIgnoreCase))
                            {
                                p.Label = (p.Label ?? "") + "LB";
                                _context.Update(p);
                            }
                        }

                        // Jika total SAMA → tidak ada perubahan qty, skip penambahan/pengurangan
                        if (totalCurrentAct != totalNewQty)
                        {
                            // Bagi rata ke setiap No Rak: base = floor(total/n), sisa ke rak pertama
                            int baseQty    = totalNewQty / parsedNoRacks.Count;
                            int remainder  = totalNewQty % parsedNoRacks.Count;

                            for (int rackIndex = 0; rackIndex < parsedNoRacks.Count; rackIndex++)
                            {
                                int currentNoRack  = parsedNoRacks[rackIndex];
                                if (currentNoRack <= 0) continue;

                                // Target qty untuk rak ini (remainder selalu ke rak PERTAMA supaya total tetap sama)
                                int rackTargetQty  = baseQty + (rackIndex == 0 ? remainder : 0);

                                // Stok aktif di rak ini saja (FIFO-accurate)
                                var activePiecesThisRack = activePiecesForRacks
                                    .Where(p => p.Column == currentNoRack)
                                    .ToList();
                                int currentRackAct = activePiecesThisRack.Count;

                                int diff = rackTargetQty - currentRackAct;
                                if (diff == 0) continue;

                                if (diff > 0)
                                {
                                    // Tentukan label yang akan dipakai: prioritas VIN, lalu ItemCode
                                    string generatedLabel = !string.IsNullOrWhiteSpace(item.VIN) ? item.VIN : (item.ItemCode ?? "-");

                                    // Tambah stok di rak ini
                                    for (int i = 0; i < diff; i++)
                                    {
                                        _context.PullingRecords.Add(new DeliveryControl.Models.PullingRecord
                                        {
                                            Item           = item,
                                            Tag            = generatedLabel,
                                            Label          = generatedLabel + "LB",
                                            Plant          = item.Plant ?? "Unknown",
                                            Rack           = rackStr,
                                            Column         = currentNoRack,
                                            Quantity       = 1,
                                            IsManualAdjust = true,
                                            AdjustNote     = "Opname Adjust",
                                            CreatedDate    = now.AddMilliseconds(msOffset++),
                                            CreatedBy      = username
                                        });
                                    }
                                    addedPulling += diff;
                                }
                                else // diff < 0
                                {
                                    int toRemove = Math.Abs(diff);
                                    var toReduce = activePiecesThisRack
                                        .OrderByDescending(p => p.CreatedDate) // LIFO
                                        .Take(toRemove)
                                        .ToList();

                                    foreach (var rec in toReduce)
                                    {
                                        _context.PreparationRecords.Add(new DeliveryControl.Models.PreparationRecord
                                        {
                                            Tag            = rec.Tag,
                                            Label          = DeliveryControl.Helpers.VinHelper.FormatLabel(rec.Label, rec.Tag),
                                            Kanban         = "OPNAME-REDUCE",
                                            Plant          = rec.Plant ?? "Unknown",
                                            Rack           = rec.Rack,
                                            Column         = rec.Column,
                                            CreatedDate    = now.AddMilliseconds(msOffset++),
                                            CreatedBy      = username
                                        });
                                    }
                                    addedPrep += toReduce.Count;

                                    // Zero Stock marker jika rak ini habis
                                    if (rackTargetQty == 0 && currentRackAct > 0)
                                    {
                                        _context.PullingRecords.Add(new DeliveryControl.Models.PullingRecord
                                        {
                                            Item           = item,
                                            Tag            = item.VIN,
                                            Label          = item.VIN + "LB",
                                            Plant          = item.Plant ?? "Unknown",
                                            Rack           = rackStr,
                                            Column         = currentNoRack,
                                            Quantity       = 0,
                                            IsManualAdjust = true,
                                            AdjustNote     = "Zero Stock",
                                            CreatedDate    = now.AddMilliseconds(msOffset++),
                                            CreatedBy      = username
                                        });
                                    }
                                }
                            }
                        }
                    }

                    updatedItemsCount++;
                    var currentPlant = item.Plant ?? "Unknown";
                    if (!plantSummary.ContainsKey(currentPlant)) plantSummary[currentPlant] = 0;
                    plantSummary[currentPlant]++;
                    }
                    catch (Exception rowEx)
                    {
                        if (!failedVinsList.Contains(vinStr))
                        {
                            failedVinsList.Add(vinStr);
                            errorMessages.Add($"VIN {vinStr}: {rowEx.Message}");
                        }
                    }
                }

                // Tambahan fitur untuk mendata ghost stock.
                // FIX ROOT CAUSE: Gunakan excelVinCustSet sebagai sumber kebenaran,
                // bukan processedItemIds (ItemId=0 untuk item baru sebelum SaveChanges → tidak akurat!).
                // Setiap item di DB yang VIN|Customer-nya TIDAK ada di Excel → No Order.
                int noOrderCount = 0;
                var extraActiveItems = new List<string>(); // untuk diagnostic
                foreach (var dbItem in allItems)
                {
                    // Tentukan apakah item ini ada di Excel berdasarkan VIN|Customer
                    var itemVinUpper  = (dbItem.VIN ?? "").Trim().ToUpper();
                    var itemCustUpper = (dbItem.Customer ?? "").Trim().ToUpper();
                    bool isInExcel;

                    if (!string.IsNullOrEmpty(itemVinUpper))
                    {
                        // Item punya VIN: cocokkan pasangan VIN|Customer secara tepat
                        isInExcel = excelVinCustSet.Contains($"{itemVinUpper}|{itemCustUpper}");
                    }
                    else
                    {
                        // Item tidak punya VIN: gunakan ItemCode sebagai fallback
                        var itemCodeUpper = (dbItem.ItemCode ?? "").Trim().ToUpper();
                        isInExcel = !string.IsNullOrEmpty(itemCodeUpper) && excelVinSet.Contains(itemCodeUpper);
                    }

                    if (!isInExcel)
                    {
                        if (dbItem.IsActive || !string.Equals(dbItem.StatusItem, "No Order", StringComparison.OrdinalIgnoreCase))
                        {
                            if (dbItem.IsActive)
                                extraActiveItems.Add($"VIN:{dbItem.VIN ?? "-"} Cust:{dbItem.Customer ?? "-"} Status:{dbItem.StatusItem}");
                            dbItem.IsActive = false;
                            dbItem.StatusItem = "No Order";
                            _context.Update(dbItem);
                            noOrderCount++;
                        }

                        var itemTag = !string.IsNullOrWhiteSpace(dbItem.VIN) ? dbItem.VIN : dbItem.ItemCode;
                        if (!string.IsNullOrEmpty(itemTag))
                        {
                            // Gunakan pullingDict/prepDict yang sudah di pre-fetch (menghindari query N+1)
                            var rawPulling = pullingDict.ContainsKey(itemTag) ? pullingDict[itemTag] : new List<DeliveryControl.Models.PullingRecord>();
                            var unlistedPulling = rawPulling.Where(p => (p.AdjustNote ?? "") != "Zero Stock").ToList();
                            var unlistedPrep = prepDict.ContainsKey(itemTag) ? prepDict[itemTag] : new List<DeliveryControl.Models.PreparationRecord>();

                            var unlistedConsumed = new HashSet<int>();
                            var unlistedLookup = unlistedPulling.ToLookup(p => (p.Tag ?? "").Trim().ToUpper());

                            foreach (var prep in unlistedPrep)
                            {
                                var pTag = (prep.Tag ?? "").Trim().ToUpper();
                                var pLabel = (prep.Label ?? "").Trim().ToUpper();
                                var match = unlistedLookup[pTag].FirstOrDefault(p =>
                                    !unlistedConsumed.Contains(p.PullingId) &&
                                    (p.Label ?? "").Trim().ToUpper() == pLabel &&
                                    p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                                if (match == null)
                                {
                                    match = unlistedLookup[pTag].FirstOrDefault(p =>
                                        !unlistedConsumed.Contains(p.PullingId) &&
                                        (p.AdjustNote ?? "") != "Zero Stock" &&
                                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                                }
                                if (match != null) unlistedConsumed.Add(match.PullingId);
                            }

                            var activeUnlisted = unlistedPulling
                                .Where(p => !unlistedConsumed.Contains(p.PullingId))
                                .ToList();

                            if (activeUnlisted.Any())
                            {
                                foreach (var rec in activeUnlisted)
                                {
                                    _context.PreparationRecords.Add(new DeliveryControl.Models.PreparationRecord
                                    {
                                        Tag         = rec.Tag,
                                        Label       = DeliveryControl.Helpers.VinHelper.FormatLabel(rec.Label, rec.Tag),
                                        Kanban      = "OPNAME-REDUCE",
                                        Plant       = rec.Plant ?? "Unknown",
                                        Rack        = rec.Rack ?? "-",
                                        Column      = rec.Column,
                                        CreatedDate = now.AddMilliseconds(msOffset++),
                                        CreatedBy   = username
                                    });
                                    addedPrep++;
                                }

                                var locGroups = activeUnlisted.GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), p.Column });
                                foreach (var gLoc in locGroups)
                                {
                                    var sampleRec = gLoc.First();
                                    _context.PullingRecords.Add(new DeliveryControl.Models.PullingRecord
                                    {
                                        ItemId         = sampleRec.ItemId,
                                        Tag            = itemTag,
                                        Label          = itemTag + "LB",
                                        Plant          = sampleRec.Plant ?? "Unknown",
                                        Rack           = gLoc.Key.Rack,
                                        Column         = gLoc.Key.Column,
                                        Quantity       = 0,
                                        IsManualAdjust = true,
                                        AdjustNote     = "Zero Stock",
                                        CreatedDate    = now.AddMilliseconds(msOffset++),
                                        CreatedBy      = username
                                    });
                                }
                            }
                        }
                    }
                }

                // ====================================================================
                // FASE 2: GHOST STOCK CLEANUP (Menonaktifkan sisa stok di lokasi lain)
                // ====================================================================
                int ghostPiecesReduced = 0;
                
                foreach (var locKey in touchedLocations.Keys)
                {
                    var allowedLocations = touchedLocations[locKey];
                    // locKey format: "VIN|CUSTOMER" → ambil VIN saja untuk query Tag di DB
                    var vinPart = locKey.Contains('|') ? locKey.Split('|')[0] : locKey;
                    var custPart = locKey.Contains('|') ? locKey.Split('|')[1] : "";

                    // Cari item yang cocok dengan VIN+Customer ini
                    var targetItem = allItems.FirstOrDefault(i =>
                        (i.VIN ?? "").Trim().ToUpper() == vinPart &&
                        (i.Customer ?? "").Trim().ToUpper() == custPart);
                    
                    var allPullingForVin = pullingDict.ContainsKey(vinPart) 
                        ? pullingDict[vinPart].Where(p => targetItem == null || p.ItemId == targetItem.ItemId).ToList() 
                        : new List<DeliveryControl.Models.PullingRecord>();
                    var allPrepForVin = prepDict.ContainsKey(vinPart) 
                        ? prepDict[vinPart] 
                        : new List<DeliveryControl.Models.PreparationRecord>();

                    var consumedIds = new HashSet<int>();
                    var pullingLookup = allPullingForVin.ToLookup(p => (p.Tag ?? "").Trim().ToUpper());
                    
                    foreach (var prep in allPrepForVin)
                    {
                        var prepTag   = (prep.Tag   ?? "").Trim().ToUpper();
                        var prepLabel = (prep.Label ?? "").Trim().ToUpper();
                        var candidates = pullingLookup[prepTag];
                        
                        var match = candidates.FirstOrDefault(p =>
                            !consumedIds.Contains(p.PullingId) &&
                            (p.Label ?? "").Trim().ToUpper() == prepLabel &&
                            p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                            
                        if (match == null)
                        {
                            match = candidates.FirstOrDefault(p =>
                                !consumedIds.Contains(p.PullingId) &&
                                (p.AdjustNote ?? "") != "Zero Stock" &&
                                p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                        }
                        if (match != null) consumedIds.Add(match.PullingId);
                    }

                    var activePieces = allPullingForVin
                        .Where(p => !consumedIds.Contains(p.PullingId) && (p.AdjustNote ?? "") != "Zero Stock")
                        .ToList();

                    // Cari pieces yang ada di lokasi GHOST (tidak terdaftar di Excel untuk VIN ini)
                    var ghostPieces = activePieces
                        .Where(p => !allowedLocations.Contains(((p.Rack ?? "-").Trim().ToUpper(), p.Column)))
                        .ToList();

                    if (ghostPieces.Any())
                    {
                        // 1. Kurangi sisa stok (OPNAME-REDUCE)
                        foreach (var rec in ghostPieces)
                        {
                            _context.PreparationRecords.Add(new DeliveryControl.Models.PreparationRecord
                            {
                                Tag            = rec.Tag,
                                Label          = DeliveryControl.Helpers.VinHelper.FormatLabel(rec.Label, rec.Tag),
                                Kanban         = "OPNAME-REDUCE",
                                Plant          = rec.Plant ?? "Unknown",
                                Rack           = rec.Rack,
                                Column         = rec.Column,
                                CreatedDate    = now.AddMilliseconds(msOffset++),
                                CreatedBy      = username
                            });
                            addedPrep++;
                            ghostPiecesReduced++;
                        }

                        // 2. Beri marker Zero Stock per lokasi ghost
                        var ghostLocs = ghostPieces.GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), p.Column });
                        foreach (var gLoc in ghostLocs)
                        {
                            var sampleRec = gLoc.First();
                            _context.PullingRecords.Add(new DeliveryControl.Models.PullingRecord
                            {
                                ItemId         = sampleRec.ItemId,
                                Tag            = vinPart,
                                Label          = vinPart + "LB",
                                Plant          = sampleRec.Plant ?? "Unknown",
                                Rack           = gLoc.Key.Rack,
                                Column         = gLoc.Key.Column,
                                Quantity       = 0,
                                IsManualAdjust = true,
                                AdjustNote     = "Zero Stock",
                                CreatedDate    = now.AddMilliseconds(msOffset++),
                                CreatedBy      = username
                            });
                        }
                    }
                }
                // ====================================================================

                await saveChangesAsync();
                await transaction.CommitAsync();
                
                _stockCache.Invalidate();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== HASIL IMPORT EXCEL ===");
                sb.AppendLine($"- Total baris diproses: {dataRows.Count()}");
                sb.AppendLine($"- Berhasil diupload: {updatedItemsCount} baris");
                if (plantSummary.Count > 0)
                {
                    var plantDetails = string.Join(", ", plantSummary.Select(x => $"{x.Key}: {x.Value}"));
                    sb.AppendLine($"  (Detail: {plantDetails})");
                }
                
                if (duplicateVins.Count > 0)
                {
                    sb.AppendLine($"- Item Duplicate: {duplicateVins.Count} VIN");
                    sb.AppendLine($"  (Sample: {string.Join(", ", duplicateVins.Take(5))})");
                }
                
                if (failedVinsList.Count > 0)
                {
                    sb.AppendLine($"- Gagal diupload: {failedVinsList.Count} VIN");
                    foreach (var err in errorMessages.Take(3))
                    {
                        sb.AppendLine($"  > {err}");
                    }
                    if (errorMessages.Count > 3)
                        sb.AppendLine($"  > ...dan {errorMessages.Count - 3} error lainnya");
                }

                sb.AppendLine("--- DETAIL PERUBAHAN ---");
                if (createdNewItemsCount > 0) sb.AppendLine($"> {createdNewItemsCount} item master baru dibuat.");
                if (noOrderCount > 0) sb.AppendLine($"> {noOrderCount} item di DB tidak ada di Excel → ditandai No Order.");
                if (extraActiveItems.Count > 0)
                {
                    sb.AppendLine($"  ⚠ {extraActiveItems.Count} item sebelumnya masih aktif (penyebab selisih):");
                    foreach (var extra in extraActiveItems.Take(20))
                        sb.AppendLine($"    - {extra}");
                    if (extraActiveItems.Count > 20)
                        sb.AppendLine($"    ...dan {extraActiveItems.Count - 20} item lainnya");
                }
                if (addedPulling > 0 || addedPrep > 0) sb.AppendLine($"> Opname Adjust: +{addedPulling} masuk, -{addedPrep} keluar.");
                if (ghostPiecesReduced > 0) sb.AppendLine($"> {ghostPiecesReduced} pcs ghost stock dinonaktifkan.");
                
                return Json(new { success = true, message = sb.ToString() });
                } // end try transaction
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message ?? ex.Message;
                return Json(new { success = false, message = "Terjadi kesalahan saat memproses file Excel: " + innerMsg });
            }
        }

        /// <summary>Model request untuk SaveOpname</summary>
        public class OpnameSaveRequest
        {
            public string? Plant { get; set; }
            public List<OpnameItemRow> Items { get; set; } = new();
        }

        public class OpnameItemRow
        {
            public string? VIN      { get; set; }
            public string? ItemCode { get; set; }
            public int?    ItemId   { get; set; }
            public string? Plant    { get; set; }
            public string? Rack     { get; set; }     // lokasi rak saat ini (dari sistem)
            public int     NoRack   { get; set; }     // nomor kolom rak saat ini
            public string? NewRack  { get; set; }     // lokasi rak baru (null = tidak berubah)
            public int?    NewNoRack { get; set; }    // nomor kolom rak baru (null = tidak berubah)
            public int     NewQty   { get; set; }
            public int     Act      { get; set; }
            public bool    IsDeleted { get; set; }
        }

        /// <summary>
        /// GET /Stock/GetRackList?plant=Molded
        /// Mengembalikan daftar rak (huruf) unik untuk plant tertentu.
        /// </summary>
        [HttpGet]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GetRackList(string plant = "Overall")
        {
            var query = _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted && i.IsActive);

            if (plant != "Overall")
                query = query.Where(i => i.Plant == plant);

            var racks = await query
                .Where(i => i.Rack != null && i.Rack != "")
                .Select(i => i.Rack!)
                .Distinct()
                .OrderBy(r => r)
                .ToListAsync();

            return Json(racks);
        }

        /// <summary>
        /// GET /Stock/GetRackItems?plant=Molded&rack=F
        /// Mengembalikan item per rak + stock aktual + part number dari ItemMapping.
        /// </summary>
        [HttpGet]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GetRackItems(string plant = "Overall", string rack = "")
        {
            var dashboard = await GetStockViewModel(plant, null, "Day", 1, "All", "", -1);

            // ItemMapping untuk Part Number
            // Load semua mapping ke memory; normalisasi VIN dengan strip suffix "LB"
            // karena ItemMappings.VIN pakai format "TA1330LB" sedangkan Items.VIN = "TA1330"
            var allMappings = await _context.ItemMappings.AsNoTracking().ToListAsync();
            var mappingByVin = allMappings
                .GroupBy(m => {
                    var v = (m.VIN ?? "").Trim().ToUpper();
                    return v.EndsWith("LB") ? v[..^2] : v;
                })
                .ToDictionary(g => g.Key, g => g.Select(x => (x.CustomerPartNumber ?? "").Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList());

            var filteredDetails = dashboard.StockDetails
                .Where(detail => string.Equals(rack, "Overall", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(rack)
                    || detail.RackBreakdown.Any(rb => string.Equals(rb.Rack, rack, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.Plant)
                .ThenBy(d => d.RackBreakdown
                    .Where(rb => string.Equals(rack, "Overall", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rack) || string.Equals(rb.Rack, rack, StringComparison.OrdinalIgnoreCase))
                    .Select(rb => rb.Rack)
                    .DefaultIfEmpty(d.RackInfo)
                    .First())
                .ThenBy(d => d.RackBreakdown
                    .Where(rb => string.Equals(rack, "Overall", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rack) || string.Equals(rb.Rack, rack, StringComparison.OrdinalIgnoreCase))
                    .Select(rb => rb.Column)
                    .DefaultIfEmpty(0)
                    .First())
                .ThenBy(d => d.VIN)
                .ToList();

            // Load Remark dari Items table (lebih efisien daripada ubah StockItemDetail)
            var itemCodes = filteredDetails.Select(d => d.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            var remarkMap = await _context.Items
                .Where(i => itemCodes.Contains(i.ItemCode))
                .Select(i => new { i.ItemCode, i.Remark })
                .ToDictionaryAsync(i => i.ItemCode!, i => i.Remark ?? "");

            var result = filteredDetails.Select((detail, idx) =>
            {
                var vin = (detail.VIN ?? "").Trim().ToUpper();
                var matchedRacks = detail.RackBreakdown
                    .Where(rb => string.Equals(rack, "Overall", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(rack)
                        || string.Equals(rb.Rack, rack, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(rb => rb.Rack)
                    .ThenBy(rb => rb.Column)
                    .ToList();

                var act = matchedRacks.Count > 0
                    ? matchedRacks.Sum(rb => rb.Stock)
                    : detail.CurrentStock;

                var lokasi = matchedRacks.Count > 0
                    ? "Rak " + string.Join(", ", matchedRacks.Select(rb => rb.Column > 0 ? $"{rb.Rack}{rb.Column}" : rb.Rack))
                    : $"Rak {detail.RackInfo}";

                var qpc = detail.QtyLot ?? 1;
                var min = detail.Min ?? 0;
                var rop = detail.Rop ?? 0;
                var max = detail.Max ?? 0;

                var partNumber = vin != "" && mappingByVin.ContainsKey(vin)
                    ? mappingByVin[vin]
                        .OrderByDescending(p => p.Count(char.IsLetterOrDigit))
                        .ThenByDescending(p => p.Length)
                        .FirstOrDefault() ?? string.Empty
                    : string.Empty;

                return new
                {
                    no       = idx + 1,
                    lokasi,
                    vin      = detail.VIN ?? "-",
                    partNos  = string.IsNullOrWhiteSpace(partNumber) ? new List<string>() : new List<string> { partNumber },
                    qpc,
                    act,
                    min,
                    rop,
                    max,
                    status   = detail.Status,
                    level    = Math.Round((double)detail.LevelStock, 2),
                    remark   = remarkMap.TryGetValue(detail.Tag ?? "", out var r) ? r : "",
                    itemName = detail.ItemName,
                    itemCode = detail.Tag,
                    plant    = detail.Plant ?? "-",
                    rack     = matchedRacks.Select(rb => rb.Rack).FirstOrDefault() ?? "-",
                    noRack   = matchedRacks.Select(rb => rb.Column.ToString()).FirstOrDefault() ?? "-"
                };
            }).ToList();

            return Json(result);
        }

        // ── Helper: Status yang dikecualikan dari indikator, grafik, dan default dashboard view ──
        // Mencakup: No Order, PMSP SM (Slow Moving), After Market
        // Item dengan status ini TETAP bisa di-scan Pulling/Prepare, tapi tidak dihitung di indikator.
        private static bool IsExcludedStatus(string? s) =>
            string.Equals(s, "No Order", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "PMSP SM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s, "After Market", StringComparison.OrdinalIgnoreCase);

        private async Task<StockDashboardViewModel> GetStockViewModel(string plant = "Overall", DateTime? date = null, string period = "Day", int pageNumber = 1, string status = "All", string search = "", int? pageSizeOverride = null, string customer = "All", string category = "All", string itemStatus = "All", bool forceShowNoOrder = false)
        {
            // Cache key: versioned so every write (ManualAdjust, HideRack, etc.) instantly invalidates all entries.
            var cacheKey = $"svm_{_stockCache.Version}_{plant}_{date?.ToString("yyyyMMdd") ?? ""}_{period}_{pageNumber}_{status}_{search}_{pageSizeOverride}_{customer}_{category}_{itemStatus}";

            StockDashboardViewModel result;
            if (_cache.TryGetValue(cacheKey, out StockDashboardViewModel? cached) && cached != null)
            {
                result = cached;
            }
            else
            {
                result = await GetStockViewModelInternal(plant, date, period, pageNumber, status, search, pageSizeOverride, customer, category, itemStatus, forceShowNoOrder);
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30)); // safety-net expiry
            }

            // IMPORTANT: ViewBag MUST always be set here (in the wrapper), not inside Internal.
            // When data comes from cache, Internal is never called and ViewBag stays null.
            // We read the pagination values stored on the ViewModel during Internal's execution.
            ViewBag.CurrentPage = result.CurrentPage;
            ViewBag.TotalPages  = result.TotalPages;
            ViewBag.TotalItems  = result.TotalItemCount;
            ViewBag.RouteData   = result.PaginationRouteData;
            ViewBag.CurrentStatus = result.CurrentStatus;
            ViewBag.SearchKeyword = search;

            return result;
        }

        private async Task<StockDashboardViewModel> GetStockViewModelInternal(string plant = "Overall", DateTime? date = null, string period = "Day", int pageNumber = 1, string status = "All", string search = "", int? pageSizeOverride = null, string customer = "All", string category = "All", string itemStatus = "All", bool forceShowNoOrder = false)
        {
            var today = DateTime.Today;
            var isFilteredByDate = date.HasValue;
            var filterDate = date ?? today;

            // Fetch Global Saklar: apakah item No Order ditampilkan di semua role
            var noOrderSetting = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "ShowNoOrderInDashboard");
            bool showNoOrder = (noOrderSetting?.Value == "true") || forceShowNoOrder;

            DateTime startDate, endDate;
            if (period == "Week")
            {
                startDate = filterDate.Date.AddDays(-6);
                endDate = filterDate.Date.AddDays(1).AddSeconds(-1);
            }
            else if (period == "Month")
            {
                startDate = new DateTime(filterDate.Year, filterDate.Month, 1);
                endDate = startDate.AddMonths(1).AddSeconds(-1);
            }
            else if (period == "Year")
            {
                startDate = new DateTime(filterDate.Year, 1, 1);
                endDate = startDate.AddYears(1).AddSeconds(-1);
            }
            else // Day
            {
                startDate = filterDate.Date;
                endDate = startDate.AddDays(1).AddSeconds(-1);
            }

            var rawCacheKey = $"raw_{_stockCache.Version}_{plant}_{filterDate:yyyyMMdd}_{period}";
            if (!_cache.TryGetValue(rawCacheKey, out RawStockCacheData? rawData) || rawData == null)
            {
                // STOCK CALCULATION LOGIC (FIFO)
            // DEFAULT: Ambil SEMUA data pulling & preparation (tanpa filter tanggal)
            //          Ini menampilkan total stok aktual yang ada di FG saat ini.
            // JIKA USER PILIH TANGGAL: Filter hanya data dari tanggal tersebut ke atas.
            //    EXCLUDE Mismatch records — they are log-only and don't affect stock
            //    EXCLUDE Opname Reduce markers — these are rack-specific qty reductions, handled outside FIFO
            // PERF: No .Include(r => r.Item) here — eliminates expensive JOIN on ALL pulling records.
            // p.Item is only needed in ResolvePullingPlant; we use allItemsByVin lookup instead (loaded below).
            var pullingQueryAll = _context.PullingRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch" && (r.AdjustNote ?? "") != "Opname Reduce").AsQueryable();
            // Untuk kalkulasi stok: ambil juga record dengan Plant kosong/Unknown (in-memory filter setelah load)
            if (plant != "Overall") 
                pullingQueryAll = pullingQueryAll.Where(r => 
                    r.Plant == plant || r.Plant == null || r.Plant == "" || r.Plant == "Unknown" || r.Plant == "-");
            
            // 2. Preparation: Sama — default semua
            //    EXCLUDE Mismatch records — they are log-only and don't affect stock
            //    NOTE: Plant = "MADJUST" (Manual Adjust reduce/zero) selalu ikut FIFO tanpa filter plant,
            //    karena preparation ini dibuat lintas plant untuk mengurangi stok pulling yang ada.
            var preparationQueryAll = _context.PreparationRecords.AsNoTracking()
                .Where(r => r.Remark != "Mismatch").AsQueryable();
            var shoppingQueryAll = _context.ShoppingRecords.AsNoTracking().AsQueryable();
            // 3. Preparation & Pulling (DISPLAY/ACTIVITY): Filter by date for the "Recent Activity" list and "Counts"
            //    NOTE: Display includes ONLY valid Match records so Mismatch/Invalid scans do not pollute normal transaction log
            var pullingQueryDisplay = _context.PullingRecords.AsNoTracking().Include(r => r.Item)
                .Where(r => (r.AdjustNote ?? "") != "Opname Reduce" && r.Remark != "Mismatch").AsQueryable();

            // Preparation display: fetch valid records only — plant filter dilakukan in-memory setelah Tag-lookup
            var preparationQueryDisplay = _context.PreparationRecords.AsNoTracking().Where(r => r.Remark != "Mismatch").AsQueryable();

            var pullingQueryInRange = pullingQueryDisplay.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);
            var preparationQueryFiltered = preparationQueryDisplay.Where(r => r.CreatedDate >= startDate && r.CreatedDate <= endDate);

            // Execute queries
            // OPTIMIZATION: Select only necessary columns from PullingRecords to save massive amount of memory
            var allPullingPotentialRaw = await pullingQueryAll
                .Select(r => new {
                    r.PullingId,
                    r.Tag,
                    r.Label,
                    r.CreatedDate,
                    r.Plant,
                    r.AdjustNote,
                    r.Rack,
                    r.Column,
                    r.ItemId,
                    r.CreatedBy,
                    r.IsManualAdjust
                })
                .OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId).ToListAsync();
            
            // Map back to PullingRecord entity for existing logic
            var allPullingPotential = allPullingPotentialRaw.Select(r => new PullingRecord {
                    PullingId = r.PullingId,
                    Tag = r.Tag,
                    Label = r.Label,
                    CreatedDate = r.CreatedDate,
                    Plant = r.Plant,
                    AdjustNote = r.AdjustNote,
                    Rack = r.Rack,
                    Column = r.Column,
                    ItemId = r.ItemId,
                    CreatedBy = r.CreatedBy,
                    IsManualAdjust = r.IsManualAdjust
            }).ToList();

            // Critical Change: Fetch ALL preparations for calculation (select only required columns for performance)
            var allPreparationAllTime = await preparationQueryAll
                .Select(r => new {
                    Id = r.PreparationId,
                    r.Tag,
                    r.Label,
                    r.CreatedDate,
                    r.Plant
                })
                .ToListAsync(); 
            var allShoppingAllTime = await shoppingQueryAll
                .Select(r => new {
                    Id = r.ShoppingId,
                    r.Tag,
                    r.Label,
                    r.CreatedDate,
                    r.Plant
                })
                .ToListAsync(); 
            var allConsumptionAllTime = allPreparationAllTime
                .Concat(allShoppingAllTime)
                .OrderBy(r => r.CreatedDate)
                .ThenBy(r => r.Id)
                .ToList();
            // Fetch filtered for display (no plant filter here — applied below in-memory)
            var allPullingInRange = await pullingQueryInRange.OrderBy(r => r.CreatedDate).ThenBy(r => r.PullingId).ToListAsync();
            var allPreparationInRangeRaw = await preparationQueryFiltered.OrderBy(r => r.CreatedDate).ThenBy(r => r.PreparationId).ToListAsync();

                        // Load Item master early — needed by ResolvePullingPlant below AND by ResolveItem later.
            var allItems = await _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted)
                .Include(i => i.RackLocations)
                .ToListAsync();
            var allItemsByVin = allItems
                .Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                .GroupBy(i => i.VIN!.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

            var activeItemsCountByVin = allItems
                .Where(i => i.IsActive && !i.IsDeleted && !string.IsNullOrWhiteSpace(i.VIN))
                .GroupBy(i => DeliveryControl.Helpers.VinHelper.Normalize(i.VIN))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // In-memory plant resolution: filter records dengan Plant kosong/Unknown menggunakan Item.Plant
            // Ini memastikan record lama (sebelum Plant field ada) tetap terhitung di plant yang benar
            if (plant != "Overall")
            {
                // Item.Plant adalah otoritatif: jika Item.Plant valid (Hose/Molded/RVI), selalu gunakan itu.
                // allPullingPotential tidak punya .Include(r=>r.Item) → p.Item null → fallback ke allItemsByVin.
                // allPullingInRange masih punya Include → p.Item tersedia → path cepat tetap berjalan.
                string ResolvePullingPlant(PullingRecord p)
                {
                    var tag = (p.Tag ?? "").Trim().ToUpper();
                    var item = p.Item ?? allItemsByVin.GetValueOrDefault(tag);
                    if (item != null)
                    {
                        var rawPlant = (item.Plant ?? "").Trim();
                        if (!string.IsNullOrEmpty(rawPlant) && rawPlant != "-")
                            return rawPlant;
                        
                        var rawItemName = (item.ItemName ?? "").Trim();
                        var knownAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Molded", "Hose", "RVI", "BTR" };
                        if (knownAreas.Contains(rawItemName))
                            return rawItemName;
                    }
                    return (p.Plant ?? "").Trim(); // Fallback
                }

                allPullingPotential = allPullingPotential
                    .Where(p => ResolvePullingPlant(p).Equals(plant, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                allPullingInRange = allPullingInRange
                    .Where(p => ResolvePullingPlant(p).Equals(plant, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // 1. Pre-normalize pulling candidates to bypass expensive string normalization in the nested loop
            var pullingWrapperList = allPullingPotential.Select(p => (
                Record: p,
                NormalizedLabel: DeliveryControl.Helpers.VinHelper.Normalize(p.Label),
                UpperLabel: (p.Label ?? "").Trim().ToUpper()
            )).ToList();

            var pullingLookup = pullingWrapperList.ToLookup(p => (p.Record.Tag ?? "").Trim().ToUpper());
            var consumedPullingIds = new HashSet<int>();

            // Pencocokan FIFO: Untuk setiap Preparation dan Shopping (ALL TIME), cari Pulling tertua yang belum terpakai
            foreach (var prep in allConsumptionAllTime)
            {
                var prepTag = (prep.Tag ?? "").Trim().ToUpper();
                var prepLabel = (prep.Label ?? "").Trim().ToUpper();

                var candidates = pullingLookup[prepTag];

                // Prioritas 1: Cari Pulling dengan Tag + Label SAMA PERSIS (FIFO normal scan)
                // Batasan waktu: Pulling harus ada sebelum Preparation (dengan toleransi 10 detik)
                var match = candidates.FirstOrDefault(p => 
                    !consumedPullingIds.Contains(p.Record.PullingId) && 
                    p.UpperLabel == prepLabel && 
                    p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10));

                // Prioritas 2: Fallback label compatible (menggunakan VinHelper.Normalize)
                var normPrepLabel = DeliveryControl.Helpers.VinHelper.Normalize(prepLabel);
                bool labelCompatibleMatch = !string.IsNullOrEmpty(prepLabel) && candidates.Any(p =>
                    !consumedPullingIds.Contains(p.Record.PullingId) &&
                    (p.Record.AdjustNote ?? "") != "Zero Stock" &&
                    p.Record.CreatedDate <= prep.CreatedDate.AddSeconds(10) &&
                    (p.NormalizedLabel == normPrepLabel ||
                     p.NormalizedLabel.StartsWith(normPrepLabel) ||
                     normPrepLabel.StartsWith(p.NormalizedLabel)));

                // Cek apakah VIN unik
                bool isUniqueVin = activeItemsCountByVin.TryGetValue(DeliveryControl.Helpers.VinHelper.Normalize(prepTag), out int activeCount) && activeCount == 1;

                if (match.Record == null)
                {
                    // Jika labelCompatibleMatch: prioritaskan pulling yang label-nya compatible
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

                if (match.Record != null) consumedPullingIds.Add(match.Record.PullingId);
            }

            // Helper: resolve Item untuk sebuah PullingRecord (pakai Item navigation, atau cari by Tag=VIN)
            Item? ResolveItem(PullingRecord p)
            {
                if (p.Item != null && !p.Item.IsDeleted) return p.Item;
                if (p.ItemId.HasValue) 
                {
                    var itemById = allItems.FirstOrDefault(i => i.ItemId == p.ItemId.Value && !i.IsDeleted);
                    if (itemById != null) return itemById;
                }
                var tag = (p.Tag ?? "").Trim().ToUpper();
                if (tag == "") return null;
                // Fallback pencarian by VIN historis (jika ItemId belum ada di PullingRecord)
                // Mengambil item pertama yang aktif dan cocok VIN-nya
                return allItems.FirstOrDefault(i => !i.IsDeleted && i.IsActive && (i.VIN ?? "").Trim().ToUpper() == tag)
                    ?? allItems.FirstOrDefault(i => !i.IsDeleted && (i.VIN ?? "").Trim().ToUpper() == tag);
            }

            // Stok yang MASIH ADA = Semua Pulling historis - Pulling yang sudah dikonsumsi oleh Preparation mana pun
            // EXCLUDE "Zero Stock" markers dari perhitungan fisik (hanya sebagai penanda lokasi saja)
            // HANYA sertakan stok yang memiliki Master Item valid (tidak deleted)
            var inStockPieces = allPullingPotential.Where(p => 
                !consumedPullingIds.Contains(p.PullingId) && 
                (p.AdjustNote ?? "") != "Zero Stock" &&
                ResolveItem(p) != null
            ).ToList();
            // allItems + allItemsByVin already declared above (hoisted for ResolvePullingPlant).

            // Filter allPreparationInRange by plant in-memory:
            // STRICT: jika plant item diketahui → hanya tampil di tab yang sesuai, TIDAK bocor ke tab lain
            List<PreparationRecord> allPreparationInRange;
            if (plant == "Overall")
            {
                allPreparationInRange = allPreparationInRangeRaw;
            }
            else
            {
                allPreparationInRange = allPreparationInRangeRaw.Where(r =>
                {
                    var tag = (r.Tag ?? "").Trim().ToUpper();
                    if (allItemsByVin.TryGetValue(tag, out var resolvedItem))
                    {
                        var resolvedPlant = (resolvedItem.Plant ?? "").Trim(); // Use Plant for plant filter
                        if (!string.IsNullOrEmpty(resolvedPlant) && resolvedPlant != "-")
                            return resolvedPlant.Equals(plant, StringComparison.OrdinalIgnoreCase);
                        return false;
                    }
                    // Item tidak ditemukan di DB → JANGAN tampil di tab plant manapun, hanya di Overall
                    return false;
                }).ToList();
            }

            var itemStatuses = new Dictionary<int, string>();

            // piecesByItem: Tetap per ItemId untuk perhitungan status (Shortage/Over)
            var piecesByItem = inStockPieces.Where(p => p.ItemId.HasValue)
                .GroupBy(p => p.ItemId!.Value)
                .ToDictionary(g => g.Key, g => (decimal)g.Count());

            foreach (var item in allItems)
            {
                var count = piecesByItem.GetValueOrDefault(item.ItemId, 0);
                var min = (decimal)(item.RackMin ?? 5);
                var level = min > 0 ? count / min : 0;

                if (level < 1.5m) itemStatuses[item.ItemId] = "Shortage";
                else if (count > (item.RackMax ?? 20)) itemStatuses[item.ItemId] = "Over";
                else itemStatuses[item.ItemId] = "Normal";
            }

            // --- COUNT FOR INDICATORS: Hitung per VIN (konsisten dengan tampilan tabel) ---
            // Status dihitung berdasarkan LevelStock per VIN, sama persis dengan logika di foreach bawah
            var groupedForCount = inStockPieces.GroupBy(p =>
            {
                var resolved = ResolveItem(p);
                var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                if (vin != "") return $"{cust}|{vin}";
                if (p.ItemId.HasValue) return $"__ITEM_{p.ItemId}";
                return $"__TAG_{(p.Tag ?? "UNKNOWN").Trim().ToUpper()}";
            });
            int shortageCount = 0, normalCount = 0, overCount = 0;
            foreach (var grp in groupedForCount)
            {
                var first = grp.First();
                var resolvedFirst = ResolveItem(first);
                var vinStock  = (decimal)grp.Count();
                var vinMin    = (decimal)(resolvedFirst?.RackMin ?? 5);
                var vinMax    = (decimal)(resolvedFirst?.RackMax ?? 20);
                var vinLevel = vinMin > 0 ? vinStock / vinMin : 0;
                if (vinLevel < 1m)           shortageCount++;
                else if (vinLevel > 2m)      overCount++;
                else                         normalCount++;
            }
            // ------------------------------------------------------------------

            var stockDetails = new List<StockItemDetail>();

            // GROUP BY VIN: Agar tampilan di dashboard digabung per VIN (meskipun rak berbeda)
            // Gunakan Item.VIN sebagai key utama; fallback ke ItemId jika VIN kosong
            var groupedByVin = inStockPieces.GroupBy(p =>
            {
                var resolved = ResolveItem(p);
                var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                if (vin != "") return $"{cust}|{vin}";
                if (p.ItemId.HasValue) return $"__ITEM_{p.ItemId}";
                return $"__TAG_{(p.Tag ?? "UNKNOWN").Trim().ToUpper()}";
            });

            // ItemId yang sudah muncul di dashboard (ada stok aktif)
            var vinWithStock = new HashSet<string>(groupedByVin.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

            // Rincian rak dari stok AKTIF saja (inStockPieces) untuk digabungkan ke breakdown.
            // TIDAK menggunakan allPullingPotential (semua historis) agar rak yang sudah dipindahkan/dikosongkan
            // via Opname tidak muncul kembali sebagai ghost rack dengan Stock=0.
            // Rak yang sengaja di-set nol via Zero Stock marker ditangani terpisah oleh BAGIAN 2.
            var historicalRacksByVin = inStockPieces
                .Where(p => p.ItemId.HasValue)
                .GroupBy(p => {
                    var resolved2 = ResolveItem(p);
                    var v = (resolved2?.VIN ?? "").Trim().ToUpper();
                    var c = (resolved2?.Customer ?? "").Trim().ToUpper();
                    if (v != "") return $"{c}|{v}";
                    if (p.ItemId.HasValue) return $"__ITEM_{p.ItemId}";
                    return $"__TAG_{(p.Tag ?? "UNKNOWN").Trim().ToUpper()}";
                })
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), Col = p.Column })
                          .Select(rg => new { rg.Key.Rack, rg.Key.Col, MaxDate = rg.Max(p => p.CreatedDate), Plant = rg.OrderByDescending(p => p.CreatedDate).First().Plant ?? "" })
                          .ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (var group in groupedByVin)
            {
                var vinKey = group.Key;
                var latestPiece = group.OrderByDescending(p => p.CreatedDate).First();
                // Untuk display metadata (Label, Time, Operator): prefer record NON-opname-adjust
                // supaya label scan terakhir tidak tertimpa oleh Opname Adjust yang tidak punya Label
                var latestDisplayPiece = group
                    .Where(p => (p.AdjustNote ?? "") != "Opname Adjust")
                    .OrderByDescending(p => p.CreatedDate)
                    .FirstOrDefault() ?? latestPiece;
                // Resolve Item: pakai navigation property jika ada, fallback cari by Tag=VIN di allItems
                var resolvedItem = ResolveItem(latestPiece);

                // Total stock untuk VIN ini (semua lokasi rak digabung)
                var totalVinStock = (decimal)group.Count();

                // LevelStock: gunakan totalVinStock (stock VIN ini) vs Min item
                // Ini akurat karena 1 VIN = 1 item, dan stock VIN sudah FIFO-correct
                // isNoOrder: mencakup semua status yang dikecualikan (No Order + PMSP SM + After Market)
                bool isNoOrder = IsExcludedStatus(resolvedItem?.StatusItem);
                var minStock  = isNoOrder ? 0m : (decimal)(resolvedItem?.RackMin ?? 5);
                var maxStock  = isNoOrder ? 0m : (decimal)(resolvedItem?.RackMax ?? 20);

                decimal itemLevelStock;
                string calculatedStatus;

                if (isNoOrder)
                {
                    itemLevelStock = totalVinStock > 0 ? 999m : 1.5m;
                    calculatedStatus = totalVinStock > 0 ? "Over" : "Normal";
                }
                else
                {
                    itemLevelStock = minStock > 0 ? totalVinStock / minStock : 0;
                    if (itemLevelStock < 1m)           calculatedStatus = "Shortage";
                    else if (itemLevelStock > 2m)      calculatedStatus = "Over";
                    else                               calculatedStatus = "Normal";
                }

                // RackBreakdown: group per Rack+Column untuk popup detail
                var rackBreakdown = group
                    .GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), Col = p.Column })
                    .Select(rg => new RackLocationDetail
                    {
                        Rack = rg.Key.Rack,
                        Column = rg.Key.Col,
                        Stock = rg.Count(),
                        LastActivity = rg.OrderByDescending(p => p.CreatedDate).First().CreatedDate.ToString("dd/MM HH:mm"),
                        Plant = rg.OrderByDescending(p => p.CreatedDate).First().Plant ?? ""
                    }).ToList();
                // ── [FIX] Gabungkan Rak Historis / Zero Stock yang sedang kosong ke breakdown ──
                if (historicalRacksByVin.TryGetValue(vinKey, out var hRacks))
                {
                    foreach (var hr in hRacks)
                    {
                        // Jika rak ini belum ada di breakdown (berarti stoknya 0)
                        if (!rackBreakdown.Any(rb => rb.Rack == hr.Rack && rb.Column == hr.Col))
                        {
                            rackBreakdown.Add(new RackLocationDetail
                            {
                                Rack = hr.Rack,
                                Column = hr.Col,
                                Stock = 0,
                                LastActivity = hr.MaxDate.ToString("dd/MM HH:mm"),
                                Plant = hr.Plant
                            });
                        }
                    }
                }

                rackBreakdown = rackBreakdown.OrderBy(r => r.Rack).ThenBy(r => r.Column).ToList();

                // RackInfo: tampilkan "A.1" jika 1 lokasi, atau "N lok." jika banyak
                var rackInfo = rackBreakdown.Count == 1
                    ? $"{rackBreakdown[0].Rack}.{rackBreakdown[0].Column}"
                    : $"{rackBreakdown.Count} lok.";

                stockDetails.Add(new StockItemDetail
                {
                    Tag = latestPiece.Tag,
                    Label = DeliveryControl.Helpers.VinHelper.FormatLabel(latestDisplayPiece.Label, resolvedItem?.VIN ?? latestPiece.Tag),
                    ItemName = (!string.IsNullOrWhiteSpace(resolvedItem?.ItemName) && !string.Equals(resolvedItem?.ItemName?.Trim(), resolvedItem?.VIN?.Trim(), StringComparison.OrdinalIgnoreCase)) 
                                ? resolvedItem.ItemName 
                                : (resolvedItem?.Plant ?? latestPiece.Plant ?? "N/A"),
                    ItemCode = resolvedItem?.ItemCode ?? "-",
                    Time = latestDisplayPiece.CreatedDate.ToString("HH:mm:ss"),
                    Date = latestDisplayPiece.CreatedDate.ToString("dd-MM-yyyy"),
                    Location = rackInfo,
                    Plant = resolvedItem?.Plant ?? latestPiece.Plant ?? string.Empty,
                    Rak = resolvedItem?.Rack ?? "-",
                    NoRak = resolvedItem?.NoRack,
                    QtyLot = resolvedItem?.QtyLot,
                    RackInfo = rackInfo,
                    Customer = resolvedItem?.Customer ?? "-",
                    Category = resolvedItem?.Category ?? "-",
                    VIN              = resolvedItem?.VIN ?? latestPiece.Tag ?? "-",
                    IsActive         = resolvedItem?.IsActive ?? false,
                    StatusItem       = resolvedItem?.StatusItem,

                    Min = IsExcludedStatus(resolvedItem?.StatusItem) ? 0 : (resolvedItem?.RackMin ?? 5),
                    Rop = IsExcludedStatus(resolvedItem?.StatusItem) ? 0 : ((resolvedItem?.ROP ?? 0) > 0 ? resolvedItem!.ROP!.Value : (int)Math.Ceiling((resolvedItem?.RackMin ?? 5) * 1.5)),
                    Max = IsExcludedStatus(resolvedItem?.StatusItem) ? 0 : (resolvedItem?.RackMax ?? 20),
                    CurrentStock = totalVinStock,
                    LevelStock = itemLevelStock,
                    Operator = latestDisplayPiece.CreatedBy ?? "-",
                    Status = calculatedStatus,
                    LastActivityDate = latestPiece.CreatedDate,
                    IsManualAdjust = latestPiece.IsManualAdjust,
                    AdjustNote = latestPiece.AdjustNote,
                    ItemId = resolvedItem?.ItemId ?? latestPiece.ItemId,
                    RackBreakdown = rackBreakdown
                });
            }

            // --- BAGIAN 2: Tambahkan Rak yang di-set ke NOL (Zero Stock Marker) Lintas VIN ---
            var zeroStockMarkers = allPullingPotential
                .Where(p => p.AdjustNote == "Zero Stock")
                .GroupBy(p => {
                    var resolved = ResolveItem(p);
                    var vin = (resolved?.VIN ?? p.Tag ?? "").Trim().ToUpper();
                    var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                    return new {
                        Vin = vin != "" ? $"{cust}|{vin}" : "",
                        Rack = (p.Rack ?? "-").Trim().ToUpper(),
                        Col = p.Column
                    };
                })
                .Select(g => g.OrderByDescending(p => p.CreatedDate).First())
                .ToList();

            foreach (var marker in zeroStockMarkers)
            {
                var resolved = ResolveItem(marker);
                if (resolved == null) continue;
                if (plant != "Overall" && (resolved.Plant ?? "") != plant) continue;

                var vinKey = (resolved.VIN ?? "").Trim().ToUpper();
                var custKey = (resolved.Customer ?? "").Trim().ToUpper();
                var combinedKey = vinKey != "" ? $"{custKey}|{vinKey}" : "";
                if (string.IsNullOrEmpty(combinedKey)) continue;

                var rackName = (marker.Rack ?? "-").Trim().ToUpper();
                var colNum   = marker.Column;

                var existing = stockDetails.FirstOrDefault(s => s.VIN == vinKey && s.Customer == custKey);
                if (existing != null)
                {
                    // Update tanggal aktivitas jika marker ini lebih baru
                    if (marker.CreatedDate > existing.LastActivityDate)
                    {
                        existing.LastActivityDate = marker.CreatedDate;
                        existing.Time = marker.CreatedDate.ToString("HH:mm:ss");
                        existing.Date = marker.CreatedDate.ToString("dd-MM-yyyy");
                    }

                    // Tambahkan ke breakdown rincian rak jika belum ada
                    if (!existing.RackBreakdown.Any(rb => rb.Rack == rackName && rb.Column == colNum))
                    {
                        existing.RackBreakdown.Add(new RackLocationDetail {
                            Rack = rackName,
                            Column = colNum,
                            Stock = 0,
                            LastActivity = marker.CreatedDate.ToString("dd/MM HH:mm")
                        });
                        
                        // Re-sync metadata lokasi
                        existing.RackBreakdown = existing.RackBreakdown.OrderBy(b => b.Rack).ThenBy(b => b.Column).ToList();
                        existing.Location = existing.RackBreakdown.Count == 1 
                            ? $"{existing.RackBreakdown[0].Rack}.{existing.RackBreakdown[0].Column}" 
                            : $"{existing.RackBreakdown.Count} lok.";
                        existing.RackInfo = existing.Location;
                    }
                }
                else
                {
                    // Item ini sama sekali tidak punya stok aktif, buat baris baru
                    stockDetails.Add(new StockItemDetail
                    {
                        Tag              = marker.Tag,
                        Label            = DeliveryControl.Helpers.VinHelper.FormatLabel(marker.Label, resolved?.VIN ?? marker.Tag),
                        ItemName         = (!string.IsNullOrWhiteSpace(resolved.ItemName) && !string.Equals(resolved.ItemName?.Trim(), resolved.VIN?.Trim(), StringComparison.OrdinalIgnoreCase)) ? resolved.ItemName : (resolved.Plant ?? "N/A"),
                        ItemCode         = resolved.ItemCode ?? "-",
                        Time             = marker.CreatedDate.ToString("HH:mm:ss"),
                        Date             = marker.CreatedDate.ToString("dd-MM-yyyy"),
                        Location         = $"{rackName}.{colNum}",
                        Plant            = resolved.Plant ?? string.Empty,
                        Rak              = resolved.Rack ?? "-",
                        NoRak            = resolved.NoRack,
                        QtyLot           = resolved.QtyLot,
                        RackInfo         = $"{rackName}.{colNum}",
                        Customer         = resolved.Customer ?? "-",
                        Category         = resolved.Category ?? "-",
                        VIN              = resolved.VIN ?? "-",
                        IsActive         = resolved.IsActive,
                        StatusItem       = resolved.StatusItem,
                        Min              = IsExcludedStatus(resolved.StatusItem) ? 0 : (resolved.RackMin ?? 5),
                        Rop              = IsExcludedStatus(resolved.StatusItem) ? 0 : ((resolved.ROP ?? 0) > 0 ? resolved.ROP!.Value : (int)Math.Ceiling((resolved.RackMin ?? 5) * 1.5)),
                        Max              = IsExcludedStatus(resolved.StatusItem) ? 0 : (resolved.RackMax ?? 20),
                        CurrentStock     = 0,
                        LevelStock       = IsExcludedStatus(resolved.StatusItem) ? 1.5m : 0,
                        Operator         = marker.CreatedBy ?? "-",
                        Status           = IsExcludedStatus(resolved.StatusItem) ? "Normal" : "Shortage",
                        LastActivityDate = marker.CreatedDate,
                        IsManualAdjust   = true,
                        AdjustNote       = "Zero Stock",
                        ItemId           = resolved.ItemId,
                        RackBreakdown    = new List<RackLocationDetail> {
                            new RackLocationDetail {
                                Rack = rackName,
                                Column = colNum,
                                Stock = 0,
                                LastActivity = marker.CreatedDate.ToString("dd/MM HH:mm")
                            }
                        }
                    });
                    vinWithStock.Add(combinedKey);
                }
            }


            // --- BAGIAN 3: Tambahkan item yang habis alami (Depleted) tanpa marker ---
            var depletedGroups = allPullingPotential
                .Where(p => p.ItemId.HasValue && (p.AdjustNote ?? "") != "Zero Stock" && !(p.AdjustNote ?? "").Contains("Rack Hidden"))
                .GroupBy(p => {
                    var resolved = ResolveItem(p);
                    var vin = (resolved?.VIN ?? "").Trim().ToUpper();
                    var cust = (resolved?.Customer ?? "").Trim().ToUpper();
                    return vin != "" ? $"{cust}|{vin}" : $"__NOVIH_{(p.ItemId.HasValue ? p.ItemId.Value.ToString() : "UNK")}";
                });

            foreach (var group in depletedGroups)
            {
                var vinKey = group.Key;
                if (vinWithStock.Contains(vinKey)) continue;

                var latestRecord = group.OrderByDescending(p => p.CreatedDate).First();
                var itemObj = ResolveItem(latestRecord);
                if (itemObj == null) continue;
                if (plant != "Overall" && (itemObj.Plant ?? "") != plant) continue;


                var breakdown = group
                    .GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), Col = p.Column })
                    .Select(rg => new RackLocationDetail {
                        Rack = rg.Key.Rack,
                        Column = rg.Key.Col,
                        Stock = 0,
                        LastActivity = rg.Max(r => r.CreatedDate).ToString("dd/MM HH:mm")
                    })
                    .OrderBy(b => b.Rack).ThenBy(b => b.Column).ToList();

                stockDetails.Add(new StockItemDetail
                {
                    Tag              = latestRecord.Tag,
                    Label            = latestRecord.Label,
                    ItemName         = (!string.IsNullOrWhiteSpace(itemObj.ItemName) && !string.Equals(itemObj.ItemName?.Trim(), itemObj.VIN?.Trim(), StringComparison.OrdinalIgnoreCase)) ? itemObj.ItemName : (itemObj.Plant ?? "N/A"),
                    ItemCode         = itemObj.ItemCode ?? "-",
                    Time             = latestRecord.CreatedDate.ToString("HH:mm:ss"),
                    Date             = latestRecord.CreatedDate.ToString("dd-MM-yyyy"),
                    Location         = breakdown.Count == 1 ? $"{breakdown[0].Rack}.{breakdown[0].Column}" : $"{breakdown.Count} lok.",
                    Plant            = itemObj.Plant ?? string.Empty,
                    Rak              = itemObj.Rack ?? "-",
                    NoRak            = itemObj.NoRack,
                    QtyLot           = itemObj.QtyLot,
                    RackInfo         = breakdown.Count == 1 ? $"{breakdown[0].Rack}.{breakdown[0].Column}" : $"{breakdown.Count} lok.",
                    Customer         = itemObj.Customer ?? "-",
                    Category         = itemObj.Category ?? "-",
                    VIN              = itemObj.VIN ?? "-",
                    IsActive         = itemObj.IsActive,
                    StatusItem       = itemObj.StatusItem,
                    Min              = IsExcludedStatus(itemObj.StatusItem) ? 0 : (itemObj.RackMin ?? 5),
                    Rop              = IsExcludedStatus(itemObj.StatusItem) ? 0 : ((itemObj.ROP ?? 0) > 0 ? itemObj.ROP!.Value : (int)Math.Ceiling((itemObj.RackMin ?? 5) * 1.5)),
                    Max              = IsExcludedStatus(itemObj.StatusItem) ? 0 : (itemObj.RackMax ?? 20),
                    CurrentStock     = 0,
                    LevelStock       = IsExcludedStatus(itemObj.StatusItem) ? 1.5m : 0,
                    Operator         = latestRecord.CreatedBy ?? "-",
                    Status           = IsExcludedStatus(itemObj.StatusItem) ? "Normal" : "Shortage",
                    LastActivityDate = latestRecord.CreatedDate,
                    AdjustNote       = latestRecord.AdjustNote ?? "",
                    ItemId           = latestRecord.ItemId,
                    RackBreakdown    = breakdown
                });
                vinWithStock.Add(vinKey);
            }

            // --- BAGIAN 4: Tambahkan sisa Master Item yang BELUM muncul sama sekali ---
            // Ambil semua item (termasuk item lama/IsActive=false). 
            // Item lama akan difilter dari hitungan indikator dan disembunyikan jika saklar Tampilkan No Order OFF.
            var itemsNotYetInStock = allItems.ToList();
            foreach (var itemObj in itemsNotYetInStock)
            {
                var vinKey = (itemObj.VIN ?? "").Trim().ToUpper();
                var custKey = (itemObj.Customer ?? "").Trim().ToUpper();
                var combinedKey = vinKey != "" ? $"{custKey}|{vinKey}" : $"__NOVIH_{itemObj.ItemId}";
                
                if (vinWithStock.Contains(combinedKey)) continue;
                if (plant != "Overall" && (itemObj.Plant ?? "") != plant) continue;

                // Tampilkan semua item aktif termasuk yang belum memiliki rak
                // agar total dashboard selalu sesuai dengan jumlah master item aktif

                var defaultRack = itemObj.Rack ?? "-";
                var defaultCol = itemObj.NoRack ?? 0;

                var breakdown = new List<RackLocationDetail> {
                    new RackLocationDetail {
                        Rack = defaultRack,
                        Column = defaultCol,
                        Stock = 0,
                        LastActivity = "-",
                        Plant = itemObj.Plant ?? string.Empty
                    }
                };

                stockDetails.Add(new StockItemDetail
                {
                    Tag              = itemObj.VIN ?? "-",
                    Label            = "-",
                    ItemName         = (!string.IsNullOrWhiteSpace(itemObj.ItemName) && !string.Equals(itemObj.ItemName?.Trim(), itemObj.VIN?.Trim(), StringComparison.OrdinalIgnoreCase)) ? itemObj.ItemName : (itemObj.Plant ?? "N/A"),
                    ItemCode         = itemObj.ItemCode ?? "-",
                    Time             = "-",
                    Date             = "-",
                    Location         = $"{defaultRack}.{defaultCol}",
                    Plant            = itemObj.Plant ?? string.Empty,
                    Rak              = itemObj.Rack ?? "-",
                    NoRak            = itemObj.NoRack,
                    QtyLot           = itemObj.QtyLot,
                    RackInfo         = $"{defaultRack}.{defaultCol}",
                    Customer         = itemObj.Customer ?? "-",
                    Category         = itemObj.Category ?? "-",
                    VIN              = itemObj.VIN ?? "-",
                    IsActive         = itemObj.IsActive,
                    StatusItem       = itemObj.StatusItem,
                    Min              = IsExcludedStatus(itemObj.StatusItem) ? 0 : (itemObj.RackMin ?? 5),
                    Rop              = IsExcludedStatus(itemObj.StatusItem) ? 0 : ((itemObj.ROP ?? 0) > 0 ? itemObj.ROP!.Value : (int)Math.Ceiling((itemObj.RackMin ?? 5) * 1.5)),
                    Max              = IsExcludedStatus(itemObj.StatusItem) ? 0 : (itemObj.RackMax ?? 20),
                    CurrentStock     = 0,
                    LevelStock       = 0,
                    Operator         = "-",
                    Status           = "Shortage",
                    LastActivityDate = DateTime.MinValue,
                    AdjustNote       = "No History",
                    ItemId           = itemObj.ItemId,
                    RackBreakdown    = breakdown
                });
                vinWithStock.Add(combinedKey);
            }

            // ────────────────────────────────────────────────────────────────────────
                rawData = new RawStockCacheData
                {
                    StockDetails = stockDetails,
                    AllItems = allItems,
                    AllPullingInRange = allPullingInRange,
                    AllPreparationInRange = allPreparationInRange
                };
                _cache.Set(rawCacheKey, rawData, TimeSpan.FromMinutes(10));
            }

            var cachedStockDetails = new List<StockItemDetail>(rawData.StockDetails);
            var cachedAllItems = rawData.AllItems;
            var cachedPullingInRange = rawData.AllPullingInRange;
            var cachedPreparationInRange = rawData.AllPreparationInRange;
            
            // Filter by customer if specified (dilakukan SEBELUM hitung indikator agar indikator menyesuaikan)
            if (!string.IsNullOrEmpty(customer) && customer != "All")
            {
                if (customer.Equals("ADM", StringComparison.OrdinalIgnoreCase))
                {
                    cachedStockDetails = cachedStockDetails.Where(s => (s.Customer ?? "").StartsWith("ADM", StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else
                {
                    cachedStockDetails = cachedStockDetails.Where(s => string.Equals(s.Customer, customer, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            // Filter by category if specified (SEBELUM hitung indikator)
            if (!string.IsNullOrEmpty(category) && category != "All")
            {
                cachedStockDetails = cachedStockDetails.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Filter by itemStatus (Reguler, PMSP, No Order)
            if (!string.IsNullOrEmpty(itemStatus) && itemStatus != "All")
            {
                if (itemStatus.Equals("No Order", StringComparison.OrdinalIgnoreCase))
                {
                    cachedStockDetails = cachedStockDetails.Where(s => string.IsNullOrEmpty(s.StatusItem) || s.StatusItem.Equals("No Order", StringComparison.OrdinalIgnoreCase) || s.StatusItem.Equals("NO ORDER", StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else
                {
                    cachedStockDetails = cachedStockDetails.Where(s => string.Equals(s.StatusItem, itemStatus, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            // Sort by LastActivityDate DESC (Newest Scan first) -> Then stability sorts
            cachedStockDetails = cachedStockDetails
                .OrderByDescending(s => s.LastActivityDate)
                .ThenBy(s => s.Plant)
                .ThenBy(s => s.Location)
                .ToList();

            // Filter by search keyword (server-side, cross-page)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var sq = search.Trim().ToLower();
                cachedStockDetails = cachedStockDetails.Where(s =>
                    (s.ItemName ?? "").ToLower().Contains(sq) ||
                    (s.ItemCode ?? "").ToLower().Contains(sq) ||
                    (s.VIN ?? "").ToLower().Contains(sq) ||
                    (s.Label ?? "").ToLower().Contains(sq) ||
                    (s.Customer ?? "").ToLower().Contains(sq) ||
                    (s.Plant ?? "").ToLower().Contains(sq)
                ).ToList();
            }

            // Sembunyikan Ghost Stock (IsActive = false) dari tabel Dashboard. 
            // Item lama akan TAMPIL jika saklar "Tampilkan No Order" (showNoOrder) AKTIF atau di halaman Opname (forceShowNoOrder)
            if (!forceShowNoOrder && !showNoOrder)
            {
                cachedStockDetails = cachedStockDetails.Where(s => s.IsActive).ToList();
            }

            // Sembunyikan No Order (+ PMSP SM + After Market) dari tabel jika Global Saklar = OFF
            if (!showNoOrder)
            {
                cachedStockDetails = cachedStockDetails
                    .Where(s => !IsExcludedStatus(s.StatusItem))
                    .ToList();
            }

            // Hitung indikator DASHBOARD (Total per Status) setelah difilter oleh search, tapi sebelum difilter oleh status
            // Item tidak aktif (IsActive=false) dan status excluded (No Order, PMSP SM, After Market) SELALU dikecualikan dari indikator
            int finalShortageCount = cachedStockDetails.Count(s => s.Status == "Shortage" && s.IsActive
                && !IsExcludedStatus(s.StatusItem));
            int finalNormalCount   = cachedStockDetails.Count(s => s.Status == "Normal"   && s.IsActive
                && !IsExcludedStatus(s.StatusItem));
            int finalOverCount     = cachedStockDetails.Count(s => s.Status == "Over"     && s.IsActive
                && !IsExcludedStatus(s.StatusItem));

            // Filter by status if specified
            if (!string.IsNullOrEmpty(status) && status != "All")
            {
                cachedStockDetails = cachedStockDetails.Where(s => s.Status == status).ToList();
            }
            // INDIKATOR "TOTAL ITEM": hanya item dari Excel (IsActive=true)
            // Item lama (IsActive=false) TAMPIL di tabel tapi TIDAK dihitung di indikator
            int totalItems = cachedStockDetails.Count(s => s.IsActive);
            
            // Untuk keperluan PAGINASI: hitung semua baris yang tampil di tabel (termasuk item lama)
            int paginationTotal = cachedStockDetails.Count;
            
            // Hitung item lama yang benar-benar tampil di tabel (IsActive=false)
            int ghostStockItems = cachedStockDetails.Count(s => !s.IsActive);
            int displayTotalItems = totalItems;
            int pageSize = pageSizeOverride ?? 20;

            // Jika pageSizeOverride <= 0, berarti ambil semua (untuk EXCEL)
            bool isExport = pageSizeOverride.HasValue && pageSizeOverride.Value <= 0;
            if (isExport) pageSize = Math.Max(1, paginationTotal);

            // IF IN TRANSACTION LOG VIEW (Plant != Overall), apply search filter on transaction data.
            // NOTE: totalItems remains cachedStockDetails.Count (for stock table pagination);
            //       transaction log pagination is handled client-side.
            if (plant != "Overall")
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var sq = search.Trim().ToLower();
                    cachedPullingInRange = cachedPullingInRange.Where(r =>
                        (r.Tag     ?? "").ToLower().Contains(sq) ||
                        (r.Label   ?? "").ToLower().Contains(sq) ||
                        (r.CreatedBy ?? "").ToLower().Contains(sq)).ToList();
                    cachedPreparationInRange = cachedPreparationInRange.Where(r =>
                        (r.Tag     ?? "").ToLower().Contains(sq) ||
                        (r.Label   ?? "").ToLower().Contains(sq) ||
                        (r.Kanban  ?? "").ToLower().Contains(sq) ||
                        (r.CreatedBy ?? "").ToLower().Contains(sq)).ToList();
                }
                // DO NOT overwrite totalItems here — stock table count must remain as-is
            }

            var pagedStockDetails = cachedStockDetails
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Assign row numbers (single assignment, no duplicate)
            for (int i = 0; i < pagedStockDetails.Count; i++) pagedStockDetails[i].No = ((pageNumber - 1) * pageSize) + i + 1;

            int totalPages = (int)Math.Ceiling(paginationTotal / (double)pageSize);
            var routeData = new Dictionary<string, string> {
                { "plant", plant },
                { "date", filterDate.ToString("yyyy-MM-dd") },
                { "period", period },
                { "status", status },
                { "search", search },
                { "customer", customer },
                { "category", category }
            };

            var filteredCategoriesItems = cachedAllItems;
            if (!string.IsNullOrEmpty(customer) && customer != "All")
            {
                if (customer.Equals("ADM", StringComparison.OrdinalIgnoreCase))
                {
                    filteredCategoriesItems = filteredCategoriesItems.Where(i => (i.Customer ?? "").StartsWith("ADM", StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else
                {
                    filteredCategoriesItems = filteredCategoriesItems.Where(i => string.Equals(i.Customer, customer, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            // --- TREND DATA CALCULATION ---
            var trendStartDate = filterDate.AddDays(-29); // Default 30 days history
            var trendSnapshots = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date >= trendStartDate && s.SnapshotDate.Date <= filterDate)
                .ToListAsync();

            if (plant != "Overall")
            {
                trendSnapshots = trendSnapshots.Where(s => 
                    s.Plant.Equals(plant, StringComparison.OrdinalIgnoreCase) || 
                    s.ItemName.Equals(plant, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Untuk grafik, kita filter berdasarkan filter demografis (Customer, Category, Search)
            // TETAPI kita TIDAK filter berdasarkan IsActive atau StatusItem (No Order, dll).
            // Tujuannya agar jika item aktif di masa lalu, datanya tetap muncul di grafik hari itu.
            var filteredMasterForChart = cachedAllItems.AsEnumerable();
            if (!string.IsNullOrEmpty(customer) && customer != "All")
            {
                if (customer.Equals("ADM", StringComparison.OrdinalIgnoreCase))
                    filteredMasterForChart = filteredMasterForChart.Where(i => (i.Customer ?? "").StartsWith("ADM", StringComparison.OrdinalIgnoreCase));
                else
                    filteredMasterForChart = filteredMasterForChart.Where(i => string.Equals(i.Customer, customer, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(category) && category != "All")
            {
                filteredMasterForChart = filteredMasterForChart.Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var sq = search.Trim().ToLower();
                filteredMasterForChart = filteredMasterForChart.Where(i =>
                    (i.ItemName ?? "").ToLower().Contains(sq) ||
                    (i.ItemCode ?? "").ToLower().Contains(sq) ||
                    (i.VIN ?? "").ToLower().Contains(sq) ||
                    (i.Customer ?? "").ToLower().Contains(sq) ||
                    (i.Plant ?? "").ToLower().Contains(sq)
                );
            }
            
            var allowedVinsForChart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in filteredMasterForChart)
            {
                var cust = (i.Customer ?? "").Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(i.VIN))
                {
                    var vin = i.VIN.Trim().ToUpper();
                    allowedVinsForChart.Add(vin);
                    if (!string.IsNullOrEmpty(cust)) allowedVinsForChart.Add($"{cust}|{vin}");
                }
                if (!string.IsNullOrWhiteSpace(i.ItemCode))
                {
                    var code = i.ItemCode.Trim().ToUpper();
                    allowedVinsForChart.Add(code);
                    if (!string.IsNullOrEmpty(cust)) allowedVinsForChart.Add($"{cust}|{code}");
                    var codeNoSuffix = code.Split('_')[0];
                    allowedVinsForChart.Add(codeNoSuffix);
                    if (!string.IsNullOrEmpty(cust)) allowedVinsForChart.Add($"{cust}|{codeNoSuffix}");
                }
            }

            var dailyHistory = trendSnapshots
                .GroupBy(s => s.SnapshotDate.Date)
                .Select(g => 
                {
                    // Ambil snapshot terakhir per item pada hari tersebut agar tidak terduplikasi jika snapshot diambil lebih dari 1 kali
                    var latestSnapsPerItem = g
                        .GroupBy(s => 
                        {
                            var code = (s.ItemCode ?? "").Trim().ToUpper();
                            if (code.Contains('|'))
                            {
                                var parts = code.Split('|');
                                if (parts.Length > 1) return parts[1].Trim();
                            }
                            return code;
                        })
                        .Select(grp => grp.OrderByDescending(x => x.CreatedAt).First());

                    var activeSnaps = latestSnapsPerItem.Where(s => 
                    {
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        if (string.IsNullOrEmpty(rawCode)) return false;
                        if (allowedVinsForChart.Contains(rawCode)) return true;

                        if (rawCode.Contains('|'))
                        {
                            var parts = rawCode.Split('|');
                            if (parts.Length > 1)
                            {
                                var cleanCode = parts[1].Trim();
                                if (allowedVinsForChart.Contains(cleanCode)) return true;
                                var cleanNoSuffix = cleanCode.Split('_')[0];
                                if (allowedVinsForChart.Contains(cleanNoSuffix)) return true;
                            }
                        }
                        return false;
                    }).ToList();

                    return new DailyCategoryHistory
                    {
                        Date = g.Key,
                        DateLabel = g.Key.ToString("dd/MM"),
                        // Convert legacy DaysCoverage (which is total/min) to new scale. 
                        // <1.0 means <1D, 1.0 to 2.0 means 1D to 2D
                        Shortage = activeSnaps.Count(s => s.DaysCoverage < 1.0m),
                        Normal = activeSnaps.Count(s => s.DaysCoverage >= 1.0m && s.DaysCoverage <= 4.0m),
                        Over = activeSnaps.Count(s => s.DaysCoverage > 4.0m)
                    };
                })
                .OrderBy(h => h.Date)
                .ToList();

            // Fill missing days
            var fullDailyHistory = new List<DailyCategoryHistory>();
            for (var d = trendStartDate.Date; d <= filterDate.Date; d = d.AddDays(1))
            {
                var existing = dailyHistory.FirstOrDefault(h => h.Date == d);
                if (existing != null) fullDailyHistory.Add(existing);
                else fullDailyHistory.Add(new DailyCategoryHistory { Date = d, DateLabel = d.ToString("dd/MM"), Shortage = 0, Normal = 0, Over = 0 }); 
            }

            var yesterdayHist = fullDailyHistory.FirstOrDefault(h => h.Date == filterDate.Date.AddDays(-1)) ?? new DailyCategoryHistory();
            var yesterdaySum = new CategorySummary { Shortage = yesterdayHist.Shortage, Normal = yesterdayHist.Normal, Over = yesterdayHist.Over };

            // For today, completely use Real-Time logic to match indicators perfectly
            var todaySum = new CategorySummary { Shortage = finalShortageCount, Normal = finalNormalCount, Over = finalOverCount };
            
            // Override the last day in fullDailyHistory (Today) with Real-Time data
            var todayInList = fullDailyHistory.FirstOrDefault(h => h.Date == filterDate.Date);
            if (todayInList != null)
            {
                todayInList.Shortage = finalShortageCount;
                todayInList.Normal = finalNormalCount;
                todayInList.Over = finalOverCount;
            }

            var trendData = new[]
            {
                new { name = "Shortage", data = fullDailyHistory.Select(h => h.Shortage).ToList() },
                new { name = "Normal", data = fullDailyHistory.Select(h => h.Normal).ToList() },
                new { name = "Over", data = fullDailyHistory.Select(h => h.Over).ToList() }
            };

            var trendHistoryJson = System.Text.Json.JsonSerializer.Serialize(trendData, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            var fullDatesJson = System.Text.Json.JsonSerializer.Serialize(fullDailyHistory.Select(h => h.Date.ToString("yyyy-MM-dd")).ToList());

            // --- GENERATE DYNAMIC DEPENDENT DROPDOWNS ---
            var baseForDropdowns = rawData.StockDetails.ToList();

            // 1. Sembunyikan No Order (+ PMSP SM + After Market) global switch
            if (!showNoOrder)
            {
                baseForDropdowns = baseForDropdowns.Where(s => !IsExcludedStatus(s.StatusItem)).ToList();
            }

            // 2. Filter pencarian teks global
            if (!string.IsNullOrWhiteSpace(search))
            {
                var sq = search.Trim().ToLower();
                baseForDropdowns = baseForDropdowns.Where(s =>
                    (s.ItemName ?? "").ToLower().Contains(sq) ||
                    (s.ItemCode ?? "").ToLower().Contains(sq) ||
                    (s.VIN ?? "").ToLower().Contains(sq) ||
                    (s.Label ?? "").ToLower().Contains(sq) ||
                    (s.Customer ?? "").ToLower().Contains(sq) ||
                    (s.Category ?? "").ToLower().Contains(sq)
                ).ToList();
            }

            // Cross-filtering Customer
            var forCust = baseForDropdowns.AsEnumerable();
            if (!string.IsNullOrEmpty(category) && category != "All")
                forCust = forCust.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(itemStatus) && itemStatus != "All")
                forCust = forCust.Where(s => itemStatus.Equals("No Order", StringComparison.OrdinalIgnoreCase) ? (string.IsNullOrEmpty(s.StatusItem) || s.StatusItem.Equals("No Order", StringComparison.OrdinalIgnoreCase)) : string.Equals(s.StatusItem, itemStatus, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(status) && status != "All")
                forCust = forCust.Where(s => s.Status == status);

            var dynamicCustomers = forCust.Where(i => i.IsActive).Select(i => i.Customer ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.StartsWith("ADM", StringComparison.OrdinalIgnoreCase) ? "ADM" : c)
                .Distinct().OrderBy(c => c).ToList();

            // Cross-filtering Category (PROD)
            var forCat = baseForDropdowns.AsEnumerable();
            if (!string.IsNullOrEmpty(customer) && customer != "All")
                forCat = forCat.Where(s => customer.Equals("ADM", StringComparison.OrdinalIgnoreCase) ? (s.Customer ?? "").StartsWith("ADM", StringComparison.OrdinalIgnoreCase) : string.Equals(s.Customer, customer, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(itemStatus) && itemStatus != "All")
                forCat = forCat.Where(s => itemStatus.Equals("No Order", StringComparison.OrdinalIgnoreCase) ? (string.IsNullOrEmpty(s.StatusItem) || s.StatusItem.Equals("No Order", StringComparison.OrdinalIgnoreCase)) : string.Equals(s.StatusItem, itemStatus, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(status) && status != "All")
                forCat = forCat.Where(s => s.Status == status);

            var dynamicCategories = forCat.Where(i => i.IsActive).Select(i => i.Category ?? "")
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct().OrderBy(c => c).ToList();

            // Cross-filtering ItemStatus (Reguler/PMSP)
            var forStatusItem = baseForDropdowns.AsEnumerable();
            if (!string.IsNullOrEmpty(customer) && customer != "All")
                forStatusItem = forStatusItem.Where(s => customer.Equals("ADM", StringComparison.OrdinalIgnoreCase) ? (s.Customer ?? "").StartsWith("ADM", StringComparison.OrdinalIgnoreCase) : string.Equals(s.Customer, customer, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(category) && category != "All")
                forStatusItem = forStatusItem.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(status) && status != "All")
                forStatusItem = forStatusItem.Where(s => s.Status == status);

            var dynamicStatuses = forStatusItem.Where(i => i.IsActive).Select(i => i.StatusItem ?? "Reguler")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Equals("NO ORDER", StringComparison.OrdinalIgnoreCase) ? "No Order" : s)
                .Distinct().OrderBy(s => s).ToList();

            return new StockDashboardViewModel
            {
                TrendHistoryJson = trendHistoryJson,
                FullDatesJson = fullDatesJson,
                DailyHistory = fullDailyHistory,
                TodaySummary = todaySum,
                YesterdaySummary = yesterdaySum,
                PlantName            = plant,
                SelectedItemStatus   = itemStatus,
                ShowNoOrder          = showNoOrder,
                StockDetails         = pagedStockDetails,
                ShortageCount        = finalShortageCount,
                NormalCount          = finalNormalCount,
                OverCount            = finalOverCount,
                SearchDate           = filterDate,
                RecentPulling        = cachedPullingInRange.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.PullingId).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                RecentPreparation    = cachedPreparationInRange.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.PreparationId).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                LogUsers             = cachedPullingInRange.Select(r => r.CreatedBy).Where(u => !string.IsNullOrWhiteSpace(u)).Union(cachedPreparationInRange.Select(r => r.CreatedBy).Where(u => !string.IsNullOrWhiteSpace(u))).Distinct().OrderBy(u => u).ToList(),
                TotalPullingToday    = cachedPullingInRange.Count,
                TotalPreparationToday = cachedPreparationInRange.Count,
                Period               = period,
                // Pagination metadata — stored on ViewModel so cache wrapper can re-assign ViewBag on cache hits
                CurrentPage          = pageNumber,
                TotalPages           = totalPages,
                TotalItemCount       = displayTotalItems,
                NoOrderCount         = ghostStockItems,
                PaginationRouteData  = routeData,
                CurrentStatus        = status,
                AvailableCustomers   = dynamicCustomers,
                SelectedCustomer     = customer,
                AvailableCategories  = dynamicCategories,
                SelectedCategory     = category,
                AvailablePlants      = GetAvailablePlants(rawData, filterDate, period),
                AvailableStatuses    = dynamicStatuses
            };
        }

        private List<string> GetAvailablePlants(RawStockCacheData rawData, DateTime filterDate, string period)
        {
            var overallCacheKey = $"raw_{_stockCache.Version}_Overall_{filterDate:yyyyMMdd}_{period}";
            if (_cache.TryGetValue(overallCacheKey, out RawStockCacheData? overallData) && overallData != null)
            {
                return overallData.StockDetails.Where(i => i.IsActive).Select(i => i.Plant ?? "").Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList();
            }
            
            var fallbackPlants = rawData.AllItems.Where(i => i.IsActive).Select(i => i.Plant ?? "").ToList();
            fallbackPlants.AddRange(rawData.StockDetails.Where(i => i.IsActive).Select(i => i.Plant ?? ""));
            return fallbackPlants.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().OrderBy(p => p).ToList();
        }

        public async Task<IActionResult> ExportTrendToExcel()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            var endOfMonth = new DateTime(now.Year, now.Month, daysInMonth, 23, 59, 59);

            var snapshots = await GetHistoricalCategorySnapshots(startOfMonth, endOfMonth);

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Trend Level Stock");
                string[] categories = { "< 1 D", "< 1.5 D", "1.5 - 2 D", "2 - 3 D", "> 3 D" };
                int currentRow = 1;

                foreach (var cat in categories)
                {
                    // Block Heading
                    var titleRange = worksheet.Range(currentRow, 1, currentRow, daysInMonth + 1);
                    titleRange.Merge().Value = $"SUMMARY STOCK FG VIN {cat} DAY";
                    titleRange.Style.Font.Bold = true;
                    titleRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                    titleRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.AliceBlue;
                    currentRow++;

                    // Table Header
                    worksheet.Cell(currentRow, 1).Value = cat;
                    worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.Red;
                    worksheet.Cell(currentRow, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;

                    for (int d = 1; d <= daysInMonth; d++)
                    {
                        var cell = worksheet.Cell(currentRow, d + 1);
                        cell.Value = $"{d:D2}-{now:MMM}";
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
                        cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                    }
                    currentRow++;

                    // Rows: Plan Total, Act Hose, Act Mold, Act RVI, Act BTR, Act Total
                    string[] rows = { "Plan Total", "Act Hose", "Act Mold", "Act RVI", "Act BTR", "Act Total" };
                    foreach (var rowName in rows)
                    {
                        worksheet.Cell(currentRow, 1).Value = rowName;
                        if (rowName == "Act Total") worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGreen;
                        worksheet.Cell(currentRow, 1).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

                        for (int d = 1; d <= daysInMonth; d++)
                        {
                            var dayData = snapshots.FirstOrDefault(s => s.Date.Day == d);
                            int val = 0;
                            if (dayData != null)
                            {
                                if (rowName == "Plan Total") val = 0; // Static context, can be updated if Plan data exists
                                else if (rowName == "Act Hose") val = dayData.PlantStats.GetValueOrDefault("HOSE")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act Mold") val = dayData.PlantStats.GetValueOrDefault("MOLDED")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act RVI") val = dayData.PlantStats.GetValueOrDefault("RVI")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act BTR") val = dayData.PlantStats.GetValueOrDefault("BTR")?.GetCount(cat) ?? 0;
                                else if (rowName == "Act Total") val = dayData.TotalStats.GetCount(cat);
                            }
                            var dataCell = worksheet.Cell(currentRow, d + 1);
                            dataCell.Value = val;
                            dataCell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                        }
                        currentRow++;
                    }
                    currentRow += 2; // Spacer
                }

                worksheet.Columns().AdjustToContents();

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"TrendStockReport_{now:MMM_yyyy}.xlsx");
                }
            }
        }

        private async Task<List<CategoryTrendPoint>> GetHistoricalCategoryTrends(DateTime start, DateTime end, string period)
        {
            var snapshots = await GetHistoricalCategorySnapshots(start, end, period);
            return snapshots.Select(s => new CategoryTrendPoint
            {
                Label = s.Label,
                CatLess1 = s.TotalStats.Less1,
                CatLess1_5 = s.TotalStats.Less1_5,
                CatRange1_5_2 = s.TotalStats.Range1_5_2,
                CatRange2_3 = s.TotalStats.Range2_3,
                CatMore3 = s.TotalStats.More3,
                
                HoseShortage = (s.PlantStats.GetValueOrDefault("HOSE")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("HOSE")?.Less1_5 ?? 0),
                MoldedShortage = (s.PlantStats.GetValueOrDefault("MOLDED")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("MOLDED")?.Less1_5 ?? 0),
                RviShortage = (s.PlantStats.GetValueOrDefault("RVI")?.Less1 ?? 0) + (s.PlantStats.GetValueOrDefault("RVI")?.Less1_5 ?? 0)
            }).ToList();
        }

        private async Task<List<DaySnapshot>> GetHistoricalCategorySnapshots(DateTime start, DateTime end, string period = "Month")
        {
            var itemsRaw = await _context.Items.ToListAsync();
            var items = itemsRaw.Where(i => !string.Equals(i.StatusItem, "No Order", StringComparison.OrdinalIgnoreCase)).ToList();
            var pullings = await _context.PullingRecords.Where(p => p.CreatedDate <= end && p.Remark != "Mismatch").OrderBy(p => p.CreatedDate).ToListAsync();
            var preps = await _context.PreparationRecords.Where(p => p.CreatedDate <= end && p.Remark != "Mismatch").OrderBy(p => p.CreatedDate).ToListAsync();

            var snapshots = new List<DaySnapshot>();
            var availablePullings = new List<PullingRecord>();
            int pIdx = 0, rIdx = 0;

            var intervalEnds = new List<DateTime>();
            if (period == "Day") {
                for (int i = 1; i <= 24; i++) intervalEnds.Add(start.Date.AddHours(i));
            } else if (period == "Month") {
                for (int i = 1; i <= DateTime.DaysInMonth(start.Year, start.Month); i++) 
                    intervalEnds.Add(new DateTime(start.Year, start.Month, i, 23, 59, 59));
            } else {
                for (int i = 1; i <= 12; i++) 
                    intervalEnds.Add(new DateTime(start.Year, i, DateTime.DaysInMonth(start.Year, i), 23, 59, 59));
            }

            foreach (var tEnd in intervalEnds)
            {
                while (pIdx < pullings.Count && pullings[pIdx].CreatedDate <= tEnd) { availablePullings.Add(pullings[pIdx]); pIdx++; }
                while (rIdx < preps.Count && preps[rIdx].CreatedDate <= tEnd)
                {
                    var prep = preps[rIdx];
                    var tag = (prep.Tag ?? "").Trim().ToUpper();
                    var lbl = (prep.Label ?? "").Trim().ToUpper();
                    // Prioritas 1: match Tag + Label persis (FIFO normal scan)
                    var match = availablePullings.FirstOrDefault(p => 
                        (p.Tag ?? "").Trim().ToUpper() == tag && (p.Label ?? "").Trim().ToUpper() == lbl && p.CreatedDate <= prep.CreatedDate.AddSeconds(5));
                    // Prioritas 2: match Tag saja tanpa batasan waktu (untuk Manual Adjust — label bisa berbeda)
                    if (match == null)
                        match = availablePullings.FirstOrDefault(p => 
                            (p.Tag ?? "").Trim().ToUpper() == tag);
                    if (match != null) availablePullings.Remove(match);
                    rIdx++;
                }

                var snapshot = new DaySnapshot { Date = tEnd, Label = period == "Day" ? $"{tEnd.Hour-1:D2}:00" : period == "Month" ? tEnd.Day.ToString() : tEnd.ToString("MMM") };
                var stockByItem = availablePullings.Where(p => p.ItemId.HasValue).GroupBy(p => p.ItemId!.Value).ToDictionary(g => g.Key, g => (decimal)g.Count());
                
                foreach (var item in items)
                {
                    var stock = stockByItem.GetValueOrDefault(item.ItemId, 0);
                    var min = (decimal)(item.RackMin ?? 5);
                    var level = min > 0 ? stock / min : 0;
                    var plant = (availablePullings.FirstOrDefault(p => p.ItemId == item.ItemId)?.Plant ?? "Unknown").ToUpper();

                    var stats = snapshot.PlantStats.GetOrAdd(plant, () => new CategoryStats());
                    UpdateStats(stats, level);
                    UpdateStats(snapshot.TotalStats, level);
                }
                snapshots.Add(snapshot);
            }
            return snapshots;
        }

        private void UpdateStats(CategoryStats s, decimal level)
        {
            if (level < 1m) s.Less1++;
            else if (level < 1.0m) s.Less1_5++;
            else if (level >= 1.0m && level <= 2.0m) s.Range1_5_2++;
            else if (level > 2m && level <= 3m) s.Range2_3++;
            else if (level > 3m) s.More3++;
        }

        private class CategoryStats {
            public int Less1 { get; set; }
            public int Less1_5 { get; set; }
            public int Range1_5_2 { get; set; }
            public int Range2_3 { get; set; }
            public int More3 { get; set; }
            public int GetCount(string cat) => cat switch { "< 1 D" => Less1, "< 1.5 D" => Less1_5, "1.5 - 2 D" => Range1_5_2, "2 - 3 D" => Range2_3, "> 3 D" => More3, _ => 0 };
        }

        private class DaySnapshot {
            public DateTime Date { get; set; }
            public string Label { get; set; } = "";
            public Dictionary<string, CategoryStats> PlantStats { get; set; } = new();
            public CategoryStats TotalStats { get; set; } = new();
        }

        public class CustomerSummaryRow
        {
            public int No { get; set; }
            public string Customer { get; set; } = string.Empty;

            public int ActiveAll { get; set; }
            public int ActiveHose { get; set; }
            public int ActiveMold { get; set; }
            public int ActiveRvi { get; set; }

            public int Stock05Hose { get; set; }
            public int Stock05Mold { get; set; }
            public int Stock05Rvi { get; set; }
            public int Stock05Total => Stock05Hose + Stock05Mold + Stock05Rvi;

            public int Stock1Hose { get; set; }
            public int Stock1Mold { get; set; }
            public int Stock1Rvi { get; set; }
            public int Stock1Total => Stock1Hose + Stock1Mold + Stock1Rvi;

            public int Stock2Hose { get; set; }
            public int Stock2Mold { get; set; }
            public int Stock2Rvi { get; set; }
            public int Stock2Total => Stock2Hose + Stock2Mold + Stock2Rvi;
        }

        public class CustomerSummaryViewModel
        {
            public DateTime SelectedDate { get; set; }
            public List<CustomerSummaryRow> Rows { get; set; } = new();
        }

        public async Task<IActionResult> SummaryCustomer(DateTime? date)
        {
            var selectedDate = date ?? DateTime.Today;

            // Dapatkan data live stock yang sama persis dengan Dashboard FG utama
            // Penggunaan pageSizeOverride = -1 memastikan seluruh data ditarik tanpa pagination
            var viewModel = await GetStockViewModelInternal(date: selectedDate, pageSizeOverride: -1);
            
            // Ambil semua item yang aktif saja
            var activeItems = viewModel.StockDetails.Where(i => i.IsActive).ToList();

            static string NormalizePlant(string? raw)
            {
                var p = (raw ?? "").Trim();
                if (string.Equals(p, "Hose", StringComparison.OrdinalIgnoreCase)) return "Hose";
                if (string.Equals(p, "Molded", StringComparison.OrdinalIgnoreCase)) return "Molded";
                if (string.Equals(p, "RVI", StringComparison.OrdinalIgnoreCase)) return "RVI";
                return p;
            }

            var rows = new List<CustomerSummaryRow>();
            var groupedByCustomer = activeItems.GroupBy(i => (i.Customer ?? "-").Trim().ToUpper()).ToList();

            int no = 1;
            foreach (var group in groupedByCustomer.OrderBy(g => g.Key))
            {
                var row = new CustomerSummaryRow { No = no++, Customer = group.Key };

                row.ActiveHose = group.Count(i => NormalizePlant(i.Plant) == "Hose");
                row.ActiveMold = group.Count(i => NormalizePlant(i.Plant) == "Molded");
                row.ActiveRvi = group.Count(i => NormalizePlant(i.Plant) == "RVI");
                row.ActiveAll = row.ActiveHose + row.ActiveMold + row.ActiveRvi;

                foreach (var item in group)
                {
                    var p = NormalizePlant(item.Plant);
                    
                    if (item.LevelStock < 2.0m)
                    {
                        if (p == "Hose") row.Stock2Hose++;
                        else if (p == "Molded") row.Stock2Mold++;
                        else if (p == "RVI") row.Stock2Rvi++;
                    }
                    if (item.LevelStock < 1.0m)
                    {
                        if (p == "Hose") row.Stock1Hose++;
                        else if (p == "Molded") row.Stock1Mold++;
                        else if (p == "RVI") row.Stock1Rvi++;
                    }
                    if (item.LevelStock < 0.5m)
                    {
                        if (p == "Hose") row.Stock05Hose++;
                        else if (p == "Molded") row.Stock05Mold++;
                        else if (p == "RVI") row.Stock05Rvi++;
                    }
                }

                rows.Add(row);
            }

            var vm = new CustomerSummaryViewModel { SelectedDate = selectedDate, Rows = rows };
            return View(vm);
        }

        // [ResetStockData removed for safety]
        /// <summary>Halaman Critical Stock Trend — membaca StockSnapshot</summary>
        public async Task<IActionResult> TrendCriticalStock(string level = "1D", string period = "30d", string statusFilter = "All", string? month = null)
        {
            ViewBag.StatusFilter = statusFilter;
            var data = await BuildTrendCriticalStockDataAsync(level, period, statusFilter, month);

            ViewBag.AllPlants            = data.AllPlants;
            ViewBag.SelectedLevel        = level;
            ViewBag.SelectedPeriod       = period;
            ViewBag.SelectedMonth        = data.SelectedMonth;
            ViewBag.AvailableMonthsList  = data.AvailableMonthsList;
            ViewBag.ShortageCount        = data.ShortageCount;
            ViewBag.NormalCount          = data.NormalCount;
            ViewBag.OverCount            = data.OverCount;
            ViewBag.TodayTotal           = data.TodayTotal;
            ViewBag.TodayBelow1D         = data.ShortageCount;
            ViewBag.TodayBelow1_5D       = data.TodayBelow1_5D;
            ViewBag.LastSnapshot         = data.LastSnapshot;

            var jsonOpts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            ViewBag.PlantBreakdown       = System.Text.Json.JsonSerializer.Serialize(data.PlantBreakdownFiltered, jsonOpts);
            ViewBag.DateLabels           = System.Text.Json.JsonSerializer.Serialize(data.ChartCategories, jsonOpts);
            ViewBag.DailyDateLabels      = System.Text.Json.JsonSerializer.Serialize(data.ChartCategories, jsonOpts);
            ViewBag.FullDates            = System.Text.Json.JsonSerializer.Serialize(data.ChartCategories, jsonOpts);

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetTrendCriticalStockData(string level = "1D", string period = "30d", string statusFilter = "All", string? month = null)
        {
            var data = await BuildTrendCriticalStockDataAsync(level, period, statusFilter, month);
            return Json(new
            {
                shortageCount = data.ShortageCount,
                normalCount = data.NormalCount,
                overCount = data.OverCount,
                todayTotal = data.TodayTotal,
                todayBelow1_5D = data.TodayBelow1_5D,
                lastSnapshotFormatted = data.LastSnapshot.HasValue ? data.LastSnapshot.Value.ToString("dd/MM/yyyy HH:mm") : "-",
                plantBreakdown = data.PlantBreakdownFiltered,
                dateLabels = data.ChartCategories,
                dailyDateLabels = data.ChartCategories,
                fullDates = data.ChartCategories
            });
        }

        private async Task<(
            List<string> AllPlants,
            string SelectedMonth,
            List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> AvailableMonthsList,
            int ShortageCount,
            int NormalCount,
            int OverCount,
            int TodayTotal,
            int TodayBelow1_5D,
            DateTime? LastSnapshot,
            List<object> PlantBreakdownFiltered,
            List<string> ChartCategories
        )> BuildTrendCriticalStockDataAsync(string level, string period, string statusFilter, string? month)
        {
            var now = DateTime.Now;
            var todayOpDate = StockSnapshotService.GetOperationalDate(now);

            // List 12 bulan terakhir untuk dropdown filter (misal: "2026-09", "2026-08", ...)
            var availableMonths = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
            var ci = new System.Globalization.CultureInfo("id-ID");
            for (int i = 0; i < 12; i++)
            {
                var mDate = todayOpDate.AddMonths(-i);
                var mVal = mDate.ToString("yyyy-MM");
                var mLbl = mDate.ToString("MMMM yyyy", ci);
                if (i == 0) mLbl += " (Bulan Ini)";
                availableMonths.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = mVal, Text = mLbl });
            }

            DateTime startDate;
            DateTime targetOpDate;
            bool isCurrentMonth = false;
            string selectedMonth;

            if (!string.IsNullOrWhiteSpace(month) && DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedMonth))
            {
                startDate = new DateTime(parsedMonth.Year, parsedMonth.Month, 1);
                int daysInMonth = DateTime.DaysInMonth(parsedMonth.Year, parsedMonth.Month);
                var monthEnd = new DateTime(parsedMonth.Year, parsedMonth.Month, daysInMonth);

                if (parsedMonth.Year == todayOpDate.Year && parsedMonth.Month == todayOpDate.Month)
                {
                    targetOpDate = todayOpDate;
                    isCurrentMonth = true;
                }
                else
                {
                    targetOpDate = monthEnd;
                    isCurrentMonth = false;
                }
                selectedMonth = month;
            }
            else
            {
                startDate = period switch
                {
                    "7d"  => todayOpDate.AddDays(-6),
                    "14d" => todayOpDate.AddDays(-13),
                    "30d" => todayOpDate.AddDays(-29),
                    "90d" => todayOpDate.AddDays(-89),
                    _     => todayOpDate.AddDays(-29)
                };
                targetOpDate = todayOpDate;
                isCurrentMonth = true;
                selectedMonth = todayOpDate.ToString("yyyy-MM");
            }

            // Hanya tampilkan plant yang valid + bucket Unknown jika ada data tanpa plant
            var validPlants = new[] { "Hose", "Molded", "RVI", "BTR" };
            var allPlants = new List<string>(validPlants);

            // Normalisasi Plant ke salah satu dari Hose/Molded/RVI/BTR (case-insensitive + trim).
            static string NormalizePlant(string? raw)
            {
                var p = (raw ?? "").Trim();
                if (string.Equals(p, "Hose",   StringComparison.OrdinalIgnoreCase)) return "Hose";
                if (string.Equals(p, "Molded", StringComparison.OrdinalIgnoreCase)) return "Molded";
                if (string.Equals(p, "RVI",    StringComparison.OrdinalIgnoreCase)) return "RVI";
                if (string.Equals(p, "BTR",    StringComparison.OrdinalIgnoreCase)) return "BTR";
                return p;
            }

            // Ambil semua snapshot historis dalam range tanggal operasional
            var allSnapshotsRaw = await _context.StockSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate.Date >= startDate && s.SnapshotDate.Date <= targetOpDate)
                .ToListAsync();
            
            var allSnapshots = allSnapshotsRaw;
            foreach (var s in allSnapshots) s.Plant = NormalizePlant(s.Plant);

            var validPlantsSet = new HashSet<string>(validPlants);
            var hasUnknownPlant = allSnapshots.Any(s => !validPlantsSet.Contains(s.Plant));
            if (hasUnknownPlant) allPlants.Add("");

            var activeItemRowsRaw = await _context.Items.AsNoTracking()
                .Where(i => !i.IsDeleted && i.IsActive)
                .Select(i => new { i.VIN, i.ItemCode, i.Plant, i.StatusItem })
                .ToListAsync();

            var activeItemRows = activeItemRowsRaw.Where(i => !IsExcludedStatus(i.StatusItem)).ToList();
            
            bool isSpecificFilter = statusFilter != "All";
            if (isSpecificFilter)
            {
                activeItemRows = activeItemRows
                    .Where(i => string.Equals(i.StatusItem, statusFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var itemPlantLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in activeItemRows)
            {
                var np = NormalizePlant(r.Plant);
                if (string.IsNullOrEmpty(np)) continue;
                if (!string.IsNullOrWhiteSpace(r.VIN))      itemPlantLookup.TryAdd(r.VIN.Trim().ToUpper(),      np);
                if (!string.IsNullOrWhiteSpace(r.ItemCode)) itemPlantLookup.TryAdd(r.ItemCode.Trim().ToUpper(), np);
            }

            foreach (var s in allSnapshots)
            {
                if (!string.IsNullOrEmpty(s.Plant)) continue;
                var rawKey = (s.ItemCode ?? "").Trim().ToUpper();
                var key = rawKey.Contains("|") ? rawKey.Split('|').Last() : rawKey;
                if (itemPlantLookup.TryGetValue(key, out var resolved))
                    s.Plant = resolved;
            }

            hasUnknownPlant = allSnapshots.Any(s => !validPlantsSet.Contains(s.Plant));
            if (hasUnknownPlant && !allPlants.Contains("")) allPlants.Add("");
            else if (!hasUnknownPlant) allPlants.Remove("");

            var activeVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var itemVinLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in activeItemRows)
            {
                if (!string.IsNullOrWhiteSpace(r.VIN))      activeVins.Add(r.VIN.Trim().ToUpper());
                if (!string.IsNullOrWhiteSpace(r.ItemCode)) 
                {
                    activeVins.Add(r.ItemCode.Trim().ToUpper());
                    itemVinLookup.TryAdd(r.ItemCode.Trim().ToUpper(), !string.IsNullOrWhiteSpace(r.VIN) ? r.VIN.Trim() : r.ItemCode.Trim());
                }
            }

            List<DateTime> pastDates;
            List<int> capturedHours = new List<int>();
            List<string> chartCategories = new List<string>();

            if (isCurrentMonth)
            {
                pastDates = Enumerable.Range(0, (targetOpDate - startDate).Days)
                    .Select(i => startDate.AddDays(i))
                    .ToList();

                var currentHour = now.Hour;
                var opHours = new List<int>();
                if (targetOpDate == now.Date)
                {
                    int endH = Math.Max(8, currentHour);
                    for (int h = 8; h <= endH; h++) opHours.Add(h);
                }
                else
                {
                    for (int h = 8; h <= 23; h++) opHours.Add(h);
                    for (int h = 0; h <= currentHour; h++) opHours.Add(h);
                }

                var todaySnapsTemp = allSnapshots.Where(s => s.SnapshotDate.Date == targetOpDate).ToList();
                var actualHours = todaySnapsTemp.Select(s => s.SnapshotTime.Hours).ToList();
                capturedHours = opHours.Union(actualHours)
                    .Distinct()
                    .OrderBy(h => h < 8 ? h + 24 : h)
                    .ToList();

                foreach (var d in pastDates) chartCategories.Add(d.ToString("dd/MM"));
                for (int i = 0; i < capturedHours.Count; i++)
                {
                    var h = capturedHours[i];
                    var hourStr = $"{h:D2}:00";
                    chartCategories.Add(i == 0 ? $"{targetOpDate:dd/MM} {hourStr}" : hourStr);
                }
            }
            else
            {
                pastDates = Enumerable.Range(0, (targetOpDate - startDate).Days + 1)
                    .Select(i => startDate.AddDays(i))
                    .ToList();

                foreach (var d in pastDates) chartCategories.Add(d.ToString("dd/MM"));
            }

            var todaySnapshotsAll = isCurrentMonth
                ? allSnapshots.Where(s => s.SnapshotDate.Date == targetOpDate).ToList()
                : new List<StockSnapshot>();

            // 1. Past dates snapshots: take LAST snapshot of each past operational date per item
            var pastSnapshotsGrouped = isCurrentMonth
                ? allSnapshots
                    .Where(s => s.SnapshotDate.Date < targetOpDate)
                    .GroupBy(s => new { s.SnapshotDate.Date, s.ItemCode })
                    .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                    .ToList()
                : allSnapshots
                    .GroupBy(s => new { s.SnapshotDate.Date, s.ItemCode })
                    .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                    .ToList();

            // 2. Today hourly snapshots: group by hour & itemCode -> take LAST snapshot of that hour
            var todayHourlySnapshots = todaySnapshotsAll
                .GroupBy(s => new { Hour = s.SnapshotTime.Hours, s.ItemCode })
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            // 3. Today latest snapshot per item for today's overall metrics and plant cards
            var todayLatestPsAll = isCurrentMonth
                ? todaySnapshotsAll
                    .GroupBy(s => s.ItemCode)
                    .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                    .ToList()
                : allSnapshots
                    .Where(s => s.SnapshotDate.Date == targetOpDate)
                    .GroupBy(s => s.ItemCode)
                    .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                    .ToList();

            // Breakdown per plant (untuk chart, history table & cards)
            var plantBreakdown = allPlants.Select(p =>
            {
                var pastPs = p == ""
                    ? pastSnapshotsGrouped.Where(s => !validPlantsSet.Contains(s.Plant)).ToList()
                    : pastSnapshotsGrouped.Where(s => s.Plant == p).ToList();

                var todayPsAll = p == ""
                    ? todayHourlySnapshots.Where(s => !validPlantsSet.Contains(s.Plant)).ToList()
                    : todayHourlySnapshots.Where(s => s.Plant == p).ToList();

                var todayLatestPs = p == ""
                    ? todayLatestPsAll.Where(s => !validPlantsSet.Contains(s.Plant)).ToList()
                    : todayLatestPsAll.Where(s => s.Plant == p).ToList();

                var chartTrendList = new List<object>();
                var dailyTrendList = new List<object>();

                // Add past daily points to chart and daily trend lists
                foreach (var date in pastDates)
                {
                    var daySnaps = pastPs.Where(s => {
                        if (s.SnapshotDate.Date != date) return false;
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                        return !isSpecificFilter || activeVins.Contains(searchCode);
                    }).ToList();

                    var ptObj = new
                    {
                        Date        = date.ToString("dd/MM"),
                        DateFull    = date.ToString("dd/MM/yyyy"),
                        HasSnapshot = daySnaps.Any(),
                        Below0_5D   = daySnaps.Count(s => s.DaysCoverage < 0.5m),
                        Below1D     = daySnaps.Count(s => s.DaysCoverage < 1.0m),
                        Below1_5D   = daySnaps.Count(s => s.DaysCoverage < 1.5m),
                    };

                    chartTrendList.Add(ptObj);
                    dailyTrendList.Add(ptObj);
                }

                if (isCurrentMonth)
                {
                    // Add today hourly points to chartTrendList
                    for (int i = 0; i < capturedHours.Count; i++)
                    {
                        var h = capturedHours[i];
                        var hourSnapsPerItem = todaySnapshotsAll
                            .Where(s => s.SnapshotTime.Hours <= h)
                            .GroupBy(s => s.ItemCode)
                            .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                            .ToList();

                        var hourSnaps = p == ""
                            ? hourSnapsPerItem.Where(s => !validPlantsSet.Contains(s.Plant)).ToList()
                            : hourSnapsPerItem.Where(s => s.Plant == p).ToList();

                        var hourSnapsFiltered = hourSnaps.Where(s => {
                            var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                            var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                            return activeVins.Contains(searchCode);
                        }).ToList();

                        var hourLabel = i == 0 ? $"{targetOpDate:dd/MM} {h:D2}:00" : $"{h:D2}:00";
                        chartTrendList.Add(new
                        {
                            Date        = hourLabel,
                            DateFull    = $"{targetOpDate:dd/MM/yyyy} {h:D2}:00",
                            HasSnapshot = hourSnapsFiltered.Any(),
                            Below0_5D   = hourSnapsFiltered.Count(s => s.DaysCoverage < 0.5m),
                            Below1D     = hourSnapsFiltered.Count(s => s.DaysCoverage < 1.0m),
                            Below1_5D   = hourSnapsFiltered.Count(s => s.DaysCoverage < 1.5m),
                        });
                    }

                    // Add today daily point (latest snapshot) to dailyTrendList for history table
                    var todayDailySnaps = todayLatestPs.Where(s => {
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                        return activeVins.Contains(searchCode);
                    }).ToList();

                    dailyTrendList.Add(new
                    {
                        Date        = targetOpDate.ToString("dd/MM"),
                        DateFull    = targetOpDate.ToString("dd/MM/yyyy"),
                        HasSnapshot = todayDailySnaps.Any(),
                        Below0_5D   = todayDailySnaps.Count(s => s.DaysCoverage < 0.5m),
                        Below1D     = todayDailySnaps.Count(s => s.DaysCoverage < 1.0m),
                        Below1_5D   = todayDailySnaps.Count(s => s.DaysCoverage < 1.5m),
                    });
                }

                // Item kritis berdasarkan snapshot terbaru (hari ini jika current month, atau akhir bulan jika past month)
                var todayLatestFilteredPs = todayLatestPs
                    .Where(s => {
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                        return activeVins.Contains(searchCode);
                    })
                    .ToList();

                var criticalItems = todayLatestFilteredPs
                    .Where(s => level == "0.5D" ? s.DaysCoverage < 0.5m
                              : level == "1D"   ? s.DaysCoverage < 1.0m
                              : s.DaysCoverage < 1.5m)
                    .Select(s => {
                        var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                        var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                        var vinCode = itemVinLookup.TryGetValue(searchCode, out var v) ? v : searchCode;
                        var critDays = pastPs.Count(x => x.ItemCode == s.ItemCode && (level == "0.5D" ? x.DaysCoverage < 0.5m : (level == "1D" ? x.DaysCoverage < 1.0m : x.DaysCoverage < 1.5m)));
                        return new
                        {
                            ItemCode = searchCode,
                            s.ItemName,
                            VIN = vinCode,
                            CriticalDays = critDays,
                            CurrentStockLevel = (double)s.DaysCoverage
                        };
                    })
                    .OrderBy(x => x.CurrentStockLevel)
                    .ThenByDescending(x => x.CriticalDays)
                    .ToList();

                return new
                {
                    Plant          = p,
                    Trend          = chartTrendList,
                    DailyTrend     = dailyTrendList,
                    CriticalItems  = criticalItems,
                    TotalBelow0_5D = todayLatestFilteredPs.Count(s => s.DaysCoverage < 0.5m),
                    TotalBelow1D   = todayLatestFilteredPs.Count(s => s.DaysCoverage < 1.0m),
                    TotalBelow1_5D = todayLatestFilteredPs.Count(s => s.DaysCoverage < 1.5m),
                };
            }).ToList();

            var todayAllActive = todayLatestPsAll
                .Where(s => {
                    var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                    var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    return activeVins.Contains(searchCode);
                }).ToList();

            int shortageCount = todayAllActive.Count(s => s.DaysCoverage < 1.0m);
            int normalCount   = todayAllActive.Count(s => s.DaysCoverage >= 1.0m && s.DaysCoverage <= 2.0m);
            int overCount     = todayAllActive.Count(s => s.DaysCoverage > 2.0m);
            int todayTotal    = todayAllActive.Count;
            int todayBelow1_5D = todayAllActive.Count(s => s.DaysCoverage < 1.5m);

            var lastSnap = await _context.StockSnapshots
                .OrderByDescending(s => s.SnapshotDate)
                .ThenByDescending(s => s.CreatedAt)
                .Select(s => new { s.SnapshotDate, s.SnapshotTime })
                .FirstOrDefaultAsync();
            
            DateTime? lastSnapshot = lastSnap != null 
                ? (DateTime?)(lastSnap.SnapshotDate.Date.Add(lastSnap.SnapshotTime))
                : null;

            var plantBreakdownFiltered = plantBreakdown
                .Where(p => !string.IsNullOrEmpty(p.Plant) || p.CriticalItems.Count > 0)
                .Cast<object>()
                .ToList();

            return (
                allPlants,
                selectedMonth,
                availableMonths,
                shortageCount,
                normalCount,
                overCount,
                todayTotal,
                todayBelow1_5D,
                lastSnapshot,
                plantBreakdownFiltered,
                chartCategories
            );
        }

        [HttpGet]
        public async Task<IActionResult> ExportCriticalStockToExcel(string level = "1.5D")
        {
            var endDate = DateTime.Today;
            // Get today's snapshot
            var allSnapshotsRaw = await _context.StockSnapshots
                .Where(s => s.SnapshotDate.Date == endDate)
                .ToListAsync();

            // Normalisasi Plant ke salah satu dari Hose/Molded/RVI
            static string NormalizePlant(string? raw)
            {
                var p = (raw ?? "").Trim();
                if (string.Equals(p, "Hose",   StringComparison.OrdinalIgnoreCase)) return "Hose";
                if (string.Equals(p, "Molded", StringComparison.OrdinalIgnoreCase)) return "Molded";
                if (string.Equals(p, "RVI",    StringComparison.OrdinalIgnoreCase)) return "RVI";
                return p;
            }
            var allSnapshots = allSnapshotsRaw;
            foreach (var s in allSnapshots) s.Plant = NormalizePlant(s.Plant);

            var snapshots = allSnapshots
                .GroupBy(s => new { s.SnapshotDate.Date, s.ItemCode })
                .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
                .ToList();

            var activeItemRows = await _context.Items
                .Where(i => !i.IsDeleted && i.IsActive && (i.StatusItem == null || i.StatusItem.Trim().ToLower() != "no order"))
                .Select(i => new { i.VIN, i.ItemCode, i.Plant, i.ItemName, i.Rack })
                .ToListAsync();

            var itemPlantLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var itemVinLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var itemNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var itemRackLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var activeVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in activeItemRows)
            {
                var np = NormalizePlant(r.Plant);
                if (!string.IsNullOrEmpty(np))
                {
                    if (!string.IsNullOrWhiteSpace(r.VIN))      itemPlantLookup.TryAdd(r.VIN.Trim().ToUpper(),      np);
                    if (!string.IsNullOrWhiteSpace(r.ItemCode)) itemPlantLookup.TryAdd(r.ItemCode.Trim().ToUpper(), np);
                }

                if (!string.IsNullOrWhiteSpace(r.ItemCode))
                {
                    itemVinLookup[r.ItemCode] = !string.IsNullOrWhiteSpace(r.VIN) ? r.VIN : r.ItemCode;
                    itemNameLookup[r.ItemCode] = r.ItemName ?? "";
                    itemRackLookup[r.ItemCode] = r.Rack ?? "";
                    activeVins.Add(r.ItemCode.Trim().ToUpper());
                }
                if (!string.IsNullOrWhiteSpace(r.VIN))
                {
                    itemNameLookup[r.VIN] = r.ItemName ?? "";
                    itemRackLookup[r.VIN] = r.Rack ?? "";
                    activeVins.Add(r.VIN.Trim().ToUpper());
                }
            }

            foreach (var s in allSnapshots)
            {
                if (!string.IsNullOrEmpty(s.Plant)) continue;
                var rawKey = (s.ItemCode ?? "").Trim().ToUpper();
                var key = rawKey.Contains("|") ? rawKey.Split('|').Last() : rawKey;
                if (itemPlantLookup.TryGetValue(key, out var resolved))
                    s.Plant = resolved;
            }

            var todayPs = snapshots
                .Where(s => {
                    var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                    var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    return activeVins.Contains(searchCode);
                })
                .ToList();

            // Filter critical items
            var criticalItems = todayPs
                .Where(s => level == "0.5D" ? s.DaysCoverage < 0.5m
                          : level == "1D" ? s.DaysCoverage < 1.0m
                          : s.DaysCoverage < 1.5m)
                .Select(s => {
                    var rawCode = s.ItemCode ?? "";
                    var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    var critDays = snapshots.Count(x => x.ItemCode == s.ItemCode && x.DaysCoverage < 1.0m && x.SnapshotDate.Date != endDate);
                    var finalName = itemNameLookup.TryGetValue(searchCode, out var n) ? n : s.ItemName;
                    var rack = itemRackLookup.TryGetValue(searchCode, out var rk) ? rk : "";
                    return new
                    {
                        s.Plant,
                        VIN = itemVinLookup.TryGetValue(searchCode, out var v) ? v : searchCode,
                        ItemCode = searchCode,
                        ItemName = finalName,
                        Rack = rack,
                        CurrentStockLevel = (double)s.DaysCoverage,
                        CriticalDays = critDays
                    };
                })
                .OrderBy(x => x.Plant)
                .ThenBy(x => x.CurrentStockLevel)
                .ThenByDescending(x => x.CriticalDays)
                .ToList();

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Critical Stock");
                var headers = new string[] { "No", "Prod. Plant", "Lokasi Rack", "Part Kode (VIN)", "Stock Level (Days)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(1, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
                }

                int row = 2;
                int no = 1;
                foreach (var item in criticalItems)
                {
                    worksheet.Cell(row, 1).Value = no++;
                    worksheet.Cell(row, 2).Value = string.IsNullOrEmpty(item.Plant) ? "Unknown" : item.Plant;
                    worksheet.Cell(row, 3).Value = item.ItemName;
                    worksheet.Cell(row, 4).Value = item.VIN;
                    worksheet.Cell(row, 5).Value = item.CurrentStockLevel;
                    worksheet.Cell(row, 5).Style.NumberFormat.Format = "0.0"; // Format as 1 decimal place
                    row++;
                }

                worksheet.Columns().AdjustToContents();
                using (var stream = new System.IO.MemoryStream())
                {
                    workbook.SaveAs(stream);
                    return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Critical_Stock_Level_{level}_{endDate:yyyyMMdd}.xlsx");
                }
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCriticalItemsByDay(DateTime date, string level = "1.5D")
        {
            var snapshotsQuery = _context.StockSnapshots.AsNoTracking()
                .Where(s => s.SnapshotDate.Date == date.Date);

            if (date.TimeOfDay.TotalSeconds > 0)
            {
                int targetHour = date.Hour;
                snapshotsQuery = snapshotsQuery.Where(s => s.SnapshotTime.Hours <= targetHour);
            }

            var snapshots = await snapshotsQuery.ToListAsync();

            // Kumpulkan kode item aktif untuk filter snapshot (item tidak aktif tidak ditampilkan)
            var activeItemRows = await _context.Items
                .Where(i => i.IsActive && !i.IsDeleted && (i.StatusItem == null || i.StatusItem.ToLower() != "no order"))
                .Select(i => new { i.VIN, i.ItemCode })
                .ToListAsync();
            var activeCodeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in activeItemRows)
            {
                if (!string.IsNullOrWhiteSpace(r.VIN))      activeCodeSet.Add(r.VIN.Trim().ToUpper());
                if (!string.IsNullOrWhiteSpace(r.ItemCode)) activeCodeSet.Add(r.ItemCode.Trim().ToUpper());
            }

            // Filter data per item: ambil snapshot terbaru di/sebelum date.Hour
            var daySnaps = snapshots
                .GroupBy(s => s.ItemCode)
                .Select(g => g.OrderByDescending(x => x.SnapshotTime).ThenByDescending(x => x.CreatedAt).First())
                .Where(s => {
                    var rawCode = (s.ItemCode ?? "").Trim().ToUpper();
                    var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    return activeCodeSet.Contains(searchCode);
                })
                .ToList();

            // Tampilkan 4 plant yang valid: Hose, Molded, RVI, BTR
            var validPlantNames = new[] { "Hose", "Molded", "RVI", "BTR" };
            var validPlantSet2  = new HashSet<string>(validPlantNames);
            var plants = new List<string>(validPlantNames);
            if (daySnaps.Any(s => !validPlantSet2.Contains(s.Plant)))
                plants.Add("");

            // Pre-load VIN map untuk item yang ada di snapshot
            var itemCodes = daySnaps.Select(s => {
                var raw = s.ItemCode ?? "";
                return raw.Contains("|") ? raw.Split('|').Last() : raw;
            }).Distinct().ToList();
            
            var vinMap = await _context.Items
                .Where(it => it.IsActive && !it.IsDeleted && itemCodes.Contains(it.ItemCode))
                .Select(it => new { it.ItemCode, it.VIN })
                .ToDictionaryAsync(it => it.ItemCode, it => it.VIN ?? it.ItemCode);

            var result = plants.Select(p => {
                var ps = p == ""
                    ? daySnaps.Where(s => !validPlantSet2.Contains(s.Plant)).ToList()
                    : daySnaps.Where(s => s.Plant == p).ToList();
                
                // Filter items berdasarkan selected level
                var items = ps.Where(s => {
                    if (level == "0.5D") return s.DaysCoverage < 0.5m;
                    if (level == "1D") return s.DaysCoverage < 1.0m;
                    return s.DaysCoverage < 1.5m;
                })
                .Select(s => {
                    var rawCode = s.ItemCode ?? "";
                    var searchCode = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    return new {
                        ItemCode = searchCode,
                        ItemName = s.ItemName,
                        VIN = vinMap.TryGetValue(searchCode, out var v) ? v : searchCode,
                        CurrentStockLevel = (double)s.DaysCoverage
                    };
                })
                .OrderBy(s => s.CurrentStockLevel)
                .ToList();

                return new {
                    Plant = p,
                    Items = items,
                    Count = items.Count
                };
            }).ToList();

            var nonEmpty = result.Where(r => !string.IsNullOrEmpty(r.Plant) || r.Count > 0).ToList();

            return Json(nonEmpty);
        }

        /// <summary>Repair Plant di StockSnapshot — ganti plant kosong/salah berdasarkan Item master</summary>
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> RepairSnapshotPlant()
        {
            try
            {
                var validPl = new HashSet<string> { "Hose", "Molded", "RVI" };

                // Load Item master: VIN → Plant, ItemCode → Plant
                var allItems = await _context.Items.AsNoTracking().ToListAsync();
                var byVin  = allItems.Where(i => !string.IsNullOrWhiteSpace(i.VIN))
                    .GroupBy(i => i.VIN!.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
                var byCode = allItems.Where(i => !string.IsNullOrWhiteSpace(i.ItemCode))
                    .GroupBy(i => i.ItemCode!.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.IsActive && !i.IsDeleted ? 1 : 0).ThenByDescending(i => (i.StatusItem ?? string.Empty).Trim().ToLower() == "no order" ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);

                // Ambil snapshot dengan plant tidak valid
                var badSnaps = await _context.StockSnapshots
                    .Where(s => !validPl.Contains(s.Plant))
                    .ToListAsync();

                int fixed_ = 0;
                foreach (var sn in badSnaps)
                {
                    string resolvedPlant = "";
                    var rawCode = (sn.ItemCode ?? "").Trim().ToUpper();
                    var key = rawCode.Contains("|") ? rawCode.Split('|').Last() : rawCode;
                    // Normalize plant dari master agar case-insensitive ("molded" → "Molded")
                    static string NormalizeMasterPlant(string? raw)
                    {
                        var v = (raw ?? "").Trim();
                        if (string.Equals(v, "Hose",   StringComparison.OrdinalIgnoreCase)) return "Hose";
                        if (string.Equals(v, "Molded", StringComparison.OrdinalIgnoreCase)) return "Molded";
                        if (string.Equals(v, "RVI",    StringComparison.OrdinalIgnoreCase)) return "RVI";
                        return "";
                    }
                    if (byVin.TryGetValue(key, out var iv))
                    {
                        var np = NormalizeMasterPlant(iv.Plant);
                        if (!string.IsNullOrWhiteSpace(np)) resolvedPlant = np;
                    }
                    if (string.IsNullOrWhiteSpace(resolvedPlant) && byCode.TryGetValue(key, out var ic))
                    {
                        var np = NormalizeMasterPlant(ic.Plant);
                        if (!string.IsNullOrWhiteSpace(np)) resolvedPlant = np;
                    }

                    if (!string.IsNullOrWhiteSpace(resolvedPlant))
                    {
                        sn.Plant = resolvedPlant;
                        fixed_++;
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"✅ Repair snapshot selesai! {fixed_} snapshot diperbaiki dari {badSnaps.Count} yang salah.",
                    fixed_,
                    skipped = badSnaps.Count - fixed_
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Manual trigger snapshot — hanya Admin</summary>
        [HttpPost]
        [AuthorizeAdmin]
        public async Task<IActionResult> TriggerSnapshot()
        {
            try
            {
                var (count, message) = await _snapshotService.TakeSnapshotAsync(isManual: true);
                return Json(new { success = count > 0, message, itemCount = count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, itemCount = 0 });
            }
        }

        /// <summary>Delete all in-stock pulling records for a specific item + rack location</summary>
        [HttpPost]
        [AuthorizeAdmin]
        public async Task<IActionResult> DeleteRackStock([FromBody] DeleteRackStockRequest req)
        {
            if (req == null || req.ItemId <= 0)
                return Json(new { success = false, message = "Data tidak valid." });

            try
            {
                // Load all pullings & preps (FIFO, same as GetRackDetail/GetStockViewModel)
                var allPullings = await _context.PullingRecords
                    .Where(p => p.Remark != "Mismatch")
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PullingId)
                    .ToListAsync();

                var allPreps = await _context.PreparationRecords
                    .Where(p => p.Remark != "Mismatch")
                    .OrderBy(p => p.CreatedDate).ThenBy(p => p.PreparationId)
                    .ToListAsync();

                // FIFO matching global
                var consumedIds = new HashSet<int>();
                foreach (var prep in allPreps)
                {
                    var pTag = (prep.Tag ?? "").Trim().ToUpper();
                    var pLabel = (prep.Label ?? "").Trim().ToUpper();
                    var match = allPullings.FirstOrDefault(p =>
                        !consumedIds.Contains(p.PullingId) &&
                        (p.Tag ?? "").Trim().ToUpper() == pTag &&
                        (p.Label ?? "").Trim().ToUpper() == pLabel &&
                        p.CreatedDate <= prep.CreatedDate.AddSeconds(10));
                    if (match == null)
                        match = allPullings.FirstOrDefault(p =>
                            !consumedIds.Contains(p.PullingId) &&
                            (p.Tag ?? "").Trim().ToUpper() == pTag);
                    if (match != null) consumedIds.Add(match.PullingId);
                }

                // Find in-stock pieces for this item at this rack
                var rackNorm = (req.Rack ?? "").Trim().ToUpper();
                var toDelete = allPullings
                    .Where(p => !consumedIds.Contains(p.PullingId)
                             && p.ItemId == req.ItemId
                             && (p.Rack ?? "").Trim().ToUpper() == rackNorm
                             && p.Column == req.Column)
                    .ToList();

                if (!toDelete.Any())
                    return Json(new { success = false, message = "Tidak ada stok aktif di lokasi ini." });

                _context.PullingRecords.RemoveRange(toDelete);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = $"{toDelete.Count} record pulling dihapus dari rak {req.Rack}.{req.Column}.", deleted = toDelete.Count });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class DeleteRackStockRequest
        {
            public int ItemId { get; set; }
            public string? Rack { get; set; }
            public int Column { get; set; }
        }

        /// <summary>
        /// Lightweight endpoint: ambil daftar lokasi rak untuk dropdown modal edit.
        /// TIDAK melakukan FIFO global — langsung query riwayat PullingRecords item ini.
        /// Menampilkan SEMUA lokasi yang pernah digunakan (termasuk item stok 0).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRackOptions(int itemId)
        {
            if (itemId <= 0) return Json(new { success = false, message = "ItemId tidak valid." });

            var item = await _context.Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.ItemId == itemId);
            if (item == null) return Json(new { success = false, message = "Item tidak ditemukan." });

            // Ambil semua kombinasi Rack+Column yang pernah dipakai item ini
            // Group by Rack+Column untuk menghitung stok aktif (pulling - non mismatch)
            // Note: ini bukan FIFO sempurna, tapi cukup untuk menampilkan pilihan lokasi di dropdown
            var racks = await _context.PullingRecords.AsNoTracking()
                .Where(p => p.ItemId == itemId && p.Remark != "Mismatch")
                .GroupBy(p => new { Rack = (p.Rack ?? "-").Trim().ToUpper(), Col = p.Column })
                .Select(g => new {
                    rack   = g.Key.Rack,
                    column = g.Key.Col,
                    stock  = g.Count(p => (p.AdjustNote ?? "") != "Zero Stock"),
                    lastDate = g.Max(p => p.CreatedDate)
                })
                .OrderBy(r => r.rack).ThenBy(r => r.column)
                .ToListAsync();

            return Json(new {
                success  = true,
                itemName = item.ItemName ?? "-",
                vin      = item.VIN ?? "-",
                racks
            });
        }

        /// <summary>Get rack location breakdown for a specific item (for popup modal)</summary>
        [HttpGet]
        public async Task<IActionResult> GetRackDetail(int itemId, DateTime? date = null)

        {
            if (itemId <= 0) return Json(new { success = false, message = "ItemId tidak valid." });

            var item = await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == itemId);
            if (item == null) return Json(new { success = false, message = "Item tidak ditemukan." });

            // ── FIFO OPTIMIZED (Gunakan Cache Dashboard) ─────────────────────────
            // Daripada menghitung ulang FIFO global yang sangat lambat,
            // kita ambil hasil perhitungan yang sudah di-cache oleh GetStockViewModel.
            var vm = await GetStockViewModel(plant: "Overall", date: date, period: "Day", pageNumber: 1, status: "All", search: "", pageSizeOverride: -1, customer: "All", category: "All", itemStatus: "All");

            var detail = vm.StockDetails.FirstOrDefault(s => s.ItemId == itemId);
            var breakdownList = detail?.RackBreakdown ?? new List<RackLocationDetail>();

            var breakdown = breakdownList.Select(r => new {
                rack         = r.Rack,
                column       = r.Column,
                label        = $"{r.Rack}.{r.Column}",
                stock        = r.Stock,
                lastActivity = r.LastActivity
            }).ToList();

            var totalStock = detail?.CurrentStock ?? 0;
            var min = item.RackMin ?? 5;
            var level = min > 0 ? Math.Round((double)totalStock / (min * 2), 1) : 0;

            return Json(new {
                success = true,
                vin = item.VIN ?? "-",
                itemName = item.ItemName ?? "-",
                plant = item.Plant ?? "-",
                customer = item.Customer ?? "-",
                totalStock,
                min,
                rop = item.ROP ?? 0,
                max = item.RackMax ?? 20,
                levelStock = level,
                breakdown
            });
        }

        // ── Clear FG Data (DISABLED - endpoint dinonaktifkan untuk mencegah penghapusan data produksi) ──────
        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public Task<IActionResult> ClearFGData()
        {
            // ENDPOINT INI DINONAKTIFKAN PERMANEN.
            // Fungsi ini sebelumnya menghapus semua PullingRecords dan StockSnapshots.
            // Untuk keamanan data produksi, endpoint ini tidak lagi melakukan apapun.
            return Task.FromResult<IActionResult>(Json(new { 
                success = false, 
                message = "Fitur Clear Data telah dinonaktifkan untuk melindungi data produksi. Hubungi DBA jika membutuhkan penghapusan data." 
            }));
        }

        /// <summary>
        /// Pulihkan label kosong akibat bug HideRack lama yang salah me-Mismatch record.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> HideRack(int itemId, string rack, int noRack)
        {
            if (itemId <= 0 || string.IsNullOrWhiteSpace(rack))
                return Json(new { success = false, message = "Parameter tidak valid." });

            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var now = DateTime.Now;

            var records = await _context.PullingRecords
                .Where(r => r.ItemId == itemId && r.Rack == rack && r.Column == noRack && r.Remark != "Mismatch")
                .ToListAsync();

            if (!records.Any())
                return Json(new { success = false, message = "Tidak ada riwayat aktif untuk rak ini." });

            // Kumpulkan label yang sudah dikonsumsi PreparationRecord — jangan dihapus,
            // tapi ditandai agar tidak muncul di breakdown lokasi
            var rawConsumedLabels = await _context.PreparationRecords
                .Where(p => p.Remark != "Mismatch")
                .Select(p => p.Label)
                .Distinct()
                .ToListAsync();

            var consumedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in rawConsumedLabels)
                if (!string.IsNullOrEmpty(l)) consumedSet.Add(l);

            var toDelete = new List<PullingRecord>();
            int deletedCount = 0;
            int hiddenConsumedCount = 0;
            foreach (var r in records)
            {
                if (!string.IsNullOrEmpty(r.Label) && consumedSet.Contains(r.Label))
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
                _context.PullingRecords.RemoveRange(toDelete);
                
            await _context.SaveChangesAsync();
            _stockCache.Invalidate();

            return Json(new { success = true, message = $"Rak {rack}.{noRack} berhasil dihapus/disembunyikan ({deletedCount} dihapus, {hiddenConsumedCount} disembunyikan)." });
        }

        public class HideRackRequest
        {
            public int ItemId { get; set; }
            public string Rack { get; set; } = string.Empty;
            public int NoRack { get; set; }
        }

        [HttpPost]
        [Produces("application/json")]
        public async Task<IActionResult> DeleteOpnameRackDirect([FromBody] HideRackRequest req)
        {
            if (req.ItemId <= 0 || string.IsNullOrWhiteSpace(req.Rack))
                return Json(new { success = false, message = "Parameter tidak valid." });

            var createdBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var now = DateTime.Now;

            // Dapatkan VIN dari ItemId untuk matching yang lebih luas
            var item = await _context.Items.FindAsync(req.ItemId);
            var tag = (item?.VIN ?? "").Trim().ToUpper();

            // 1. Delete/Hide dari PullingRecords
            var reqNoRackPR = req.NoRack;
            var records = await _context.PullingRecords
                .Where(r => 
                    (r.ItemId == req.ItemId || (tag != "" && r.Tag != null && r.Tag.Trim().ToUpper() == tag))
                    && r.Rack == req.Rack 
                    && r.Column == reqNoRackPR 
                    && r.Remark != "Mismatch")
                .ToListAsync();

            var rawConsumedLabels = await _context.PreparationRecords
                .Where(p => p.Remark != "Mismatch")
                .Select(p => p.Label)
                .Distinct()
                .ToListAsync();

            var consumedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in rawConsumedLabels)
                if (!string.IsNullOrEmpty(l)) consumedSet.Add(l);

            int hiddenCount = 0;
            foreach (var r in records)
            {
                if (r.Remark != "Mismatch")
                {
                    r.Remark = "Mismatch";
                    if (!(r.AdjustNote ?? "").Contains("Rack Removed (Opname)"))
                    {
                        r.AdjustNote = (r.AdjustNote ?? "") + " | Rack Removed (Opname)";
                    }
                    hiddenCount++;
                }
            }

            // 2. Hapus permanen dari ItemRackLocations
            var reqNoRackLoc = req.NoRack;
            var masterLocations = await _context.ItemRackLocations
                .Where(l => 
                    (l.ItemId == req.ItemId || (tag != "" && _context.Items.Any(i => i.ItemId == l.ItemId && i.VIN != null && i.VIN.Trim().ToUpper() == tag)))
                    && l.Rack == req.Rack 
                    && l.NoRack == reqNoRackLoc)
                .ToListAsync();
            int masterDeleted = 0;
            if (masterLocations.Any())
            {
                _context.ItemRackLocations.RemoveRange(masterLocations);
                masterDeleted = masterLocations.Count();
            }

            // 3. Hapus juga lokasi default di tabel Items jika cocok (untuk mencegah reappearing di BAGIAN 4 GetStockViewModel)
            var reqRackClean = (req.Rack ?? "").Trim();
            var itemNoRack = item?.NoRack ?? 0;
            var reqNoRackValue = req.NoRack;
            if (item != null && (item.Rack ?? "").Trim().Equals(reqRackClean, StringComparison.OrdinalIgnoreCase) && itemNoRack == reqNoRackValue)
            {
                item.Rack = null;
                item.NoRack = null;
                item.IsActive = false; // Nonaktifkan item jika lokasi utamanya dihapus agar tidak muncul di FG
                _context.Update(item);
            }
            
            // Juga cari Item lain yang VIN-nya sama dan punya default rack yang sama
            if (tag != "")
            {
                var relatedItems = await _context.Items
                    .Where(i => i.VIN != null && i.VIN.Trim().ToUpper() == tag && i.ItemId != req.ItemId && (i.NoRack ?? 0) == reqNoRackValue)
                    .ToListAsync();
                foreach (var ri in relatedItems)
                {
                    if ((ri.Rack ?? "").Trim().Equals(reqRackClean, StringComparison.OrdinalIgnoreCase))
                    {
                        ri.Rack = null;
                        ri.NoRack = null;
                        _context.Update(ri);
                    }
                }
            }

            await _context.SaveChangesAsync();
            _stockCache.Invalidate();

            return Json(new { 
                success = true, 
                message = $"Lokasi {req.Rack}.{req.NoRack} berhasil dihapus ({hiddenCount} riwayat disembunyikan, {masterDeleted} master data dihapus)." 
            });
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<IActionResult> RepairHideRackLabels()
        {
            try
            {
                var allMismatched = await _context.PullingRecords
                    .Where(r => r.Remark == "Mismatch")
                    .ToListAsync();

                if (!allMismatched.Any())
                    return Json(new { success = true, repaired = 0, message = "Tidak ada record Mismatch di database." });

                var rawPrepLabels = await _context.PreparationRecords
                    .Select(p => p.Label)
                    .Distinct()
                    .ToListAsync();

                var prepLabelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in rawPrepLabels)
                    if (!string.IsNullOrEmpty(l)) prepLabelSet.Add(l);

                int repaired = 0;
                foreach (var r in allMismatched)
                {
                    bool fromHideRack   = r.AdjustNote != null && r.AdjustNote.Contains("Rack Removed");
                    bool labelInPrep    = !string.IsNullOrEmpty(r.Label) && prepLabelSet.Contains(r.Label);
                    bool isLegitimate   = r.AdjustNote != null &&
                                         (r.AdjustNote.Contains("Opname Reduce") ||
                                          r.AdjustNote.Contains("Zero Stock") ||
                                          r.AdjustNote.Contains("ManualAdjust"));

                    if ((fromHideRack || labelInPrep) && !isLegitimate)
                    {
                        r.Remark = "Match";
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

                if (repaired > 0)
                {
                    await _context.SaveChangesAsync();
                    _stockCache.Invalidate();
                }

                // ── Bersihkan Zero Stock marker orphan di DB ──────────────────────────────
                // Zero Stock marker yang rak-nya sudah tidak relevan (karena stock dipindah ke rak lain
                // atau semua stock sudah dikonsumsi dan rak baru sudah ada) harus di-Mismatch.
                // Caranya: cari semua Zero Stock marker per item, lalu cek apakah item tersebut
                // punya pulling record non-Mismatch non-ZeroStock di rak yang BERBEDA yang lebih baru
                // dari Zero Stock marker — jika ya, marker itu obsolete.
                var zeroMarkers = await _context.PullingRecords
                    .Where(r => r.Remark != "Mismatch" && r.AdjustNote == "Zero Stock")
                    .ToListAsync();

                int zeroFixed = 0;
                if (zeroMarkers.Any())
                {
                    // Group by ItemId
                    var zeroByItem = zeroMarkers.GroupBy(r => r.ItemId).ToList();
                    foreach (var grp in zeroByItem)
                    {
                        var itemId2 = grp.Key;
                        // Cari semua pulling non-Mismatch non-ZeroStock untuk item ini
                        var otherRecords = await _context.PullingRecords
                            .Where(p => p.ItemId == itemId2 && p.Remark != "Mismatch"
                                     && (p.AdjustNote ?? "") != "Zero Stock")
                            .Select(p => new { p.Rack, p.Column, p.CreatedDate })
                            .ToListAsync();

                        foreach (var zm in grp)
                        {
                            var zmRack = (zm.Rack ?? "-").Trim().ToUpper();
                            // Cari apakah ada record aktif di rak BERBEDA yang dibuat SETELAH marker ini
                            bool hasNewerStockElsewhere = otherRecords.Any(p =>
                                (p.Rack ?? "-").Trim().ToUpper() != zmRack && p.CreatedDate > zm.CreatedDate);
                            if (hasNewerStockElsewhere)
                            {
                                zm.Remark = "Mismatch";
                                zeroFixed++;
                            }
                        }
                    }
                    if (zeroFixed > 0)
                    {
                        await _context.SaveChangesAsync();
                        _stockCache.Invalidate();
                    }
                }

                var totalFixed = repaired + zeroFixed;
                return Json(new { success = true, repaired = totalFixed, message = totalFixed == 0
                    ? "Tidak ada data yang perlu dipulihkan."
                    : $"{repaired} label dipulihkan, {zeroFixed} Zero Stock marker obsolet dibersihkan." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, repaired = 0, message = $"Error: {ex.Message}" });
            }
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Super Admin")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ToggleNoOrderVisibility()
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "ShowNoOrderInDashboard");
            if (setting == null)
            {
                setting = new DeliveryControl.Models.SystemSetting
                {
                    Key = "ShowNoOrderInDashboard",
                    Value = "true",
                    Description = "Global Saklar Tampilkan/Sembunyikan No Order di FG Dashboard"
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = setting.Value == "true" ? "false" : "true";
                _context.SystemSettings.Update(setting);
            }
            await _context.SaveChangesAsync();
            
            // Invalidate cache since we are updating global state that affects data
            _stockCache.Invalidate();
            
            return Json(new { success = true, newValue = setting.Value == "true" });
        }

        [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin", "User", "Preparation", "Pulling")]
        public async Task<IActionResult> ShoppingLog(int pageNumber = 1, string search = "", string date = "")
        {
            int pageSize = 50;
            var query = _context.ShoppingRecords.AsQueryable();

            if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out DateTime parsedDate))
            {
                query = query.Where(r => r.CreatedDate.Date == parsedDate.Date);
            }

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(r => r.Tag.ToLower().Contains(s) || r.Label.ToLower().Contains(s) || (r.Kanban ?? "").ToLower().Contains(s));
            }

            var totalItems = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.CreatedDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.Search = search;
            ViewBag.Date = date;

            return View(items);
        }
    }


    public static class DictExtensions {
        public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TValue> factory) where TKey : notnull
        {
            if (!dict.TryGetValue(key, out var val)) { val = factory(); dict[key] = val; }
            return val;
        }
    }

    public class NGLogRequest
    {
        public string Module { get; set; } = "Preparation";
        public string? Tag { get; set; }
        public string? Label { get; set; }
        public string? Kanban { get; set; }
        public string? Reason { get; set; }
    }

    public class RackBreakdownEntry
    {
        public string Rack         { get; set; } = "-";
        public int    Column       { get; set; }
        public int    Stock        { get; set; }
        public string LastActivity { get; set; } = "";
    }
}






