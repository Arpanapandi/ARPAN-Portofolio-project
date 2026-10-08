using ClosedXML.Excel;
using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Smart Import Service — auto-detect customer & extract (Manifest, PartNo, Qty)
    /// from various file formats (PDF, Excel, CSV).
    /// Supports AHM, ADM, TMMIN, Sanoh, AWI and generic formats.
    public class SmartImportService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SmartImportService> _logger;
        private List<ScheduleTemplate>? _dynamicTemplates;

        public SmartImportService(ApplicationDbContext db, ILogger<SmartImportService> logger)
        {
            _db = db;
            _logger = logger;
        }

        private List<ScheduleTemplate> GetDynamicTemplates()
        {
            if (_dynamicTemplates == null)
            {
                try
                {
                    _dynamicTemplates = _db.ScheduleTemplates.Include(t => t.Mappings).Where(t => t.IsActive).ToList();
                }
                catch
                {
                    _dynamicTemplates = new List<ScheduleTemplate>();
                }
            }
            return _dynamicTemplates;
        }

        #region === Result Models ===

        public class SmartImportResult
        {
            public bool Success { get; set; }
            public string? DetectedCustomerName { get; set; }
            public string? DetectedCustomerCode { get; set; }
            public int? CustomerId { get; set; }
            public string? DetectedDock { get; set; }
            public string? DetectedManifest { get; set; }
            /// <summary>Tanggal jadwal yang terdeteksi dari file (DEL. DATE / Order Date / kolom tanggal)</summary>
            public DateTime? DetectedScheduledDate { get; set; }
            /// <summary>Route yang terdeteksi dari file (e.g. "IKP", "MR5SU-K1")</summary>
            public string? DetectedRoute { get; set; }
            /// <summary>Cycle yang terdeteksi dari file (e.g. "C1", "203")</summary>
            public string? DetectedCycle { get; set; }
            public string? FileFormat { get; set; }
            public string? ParserUsed { get; set; }
            public List<ExtractedItem> Items { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string? ErrorMessage { get; set; }
            /// <summary>
            /// All matching docks (Customer rows) for the detected customer name.
            /// Admin picks which dock to assign for the delivery schedule.
            /// </summary>
            public List<MatchedDock> MatchedDocks { get; set; } = new();
            /// <summary>
            /// TMMIN multi-dock mode: items sudah punya MatchedCustomerId masing-masing
            /// berdasarkan matching jam pickup â†’ dock. Dock selection jadi opsional di UI.
            /// </summary>
            public bool IsTmminMultiDock { get; set; }
        }

        public class ExtractedItem
        {
            public string Manifesting { get; set; } = "";
            public string PartNo { get; set; } = "";
            public string PartName { get; set; } = "";
            public int Qty { get; set; }
            public int ExcelKanban { get; set; }
            /// <summary>Master QtyLot (SNP) calculated from the raw Excel rows to perfectly preserve kanban counts in the preview.</summary>
            public int? QtyLot { get; set; }
            /// <summary>Tanggal jadwal per-item (dari kolom tanggal di Excel / DEL. DATE di PDF). Null = pakai DetectedScheduledDate dari header.</summary>
            public DateTime? ScheduledDate { get; set; }
            /// <summary>Jam pickup yang diextract dari file (TMMIN: dari kolom Y datetime). Digunakan untuk matching dock.</summary>
            public TimeSpan? TimeOfDay { get; set; }
            /// <summary>CustomerId yang auto-matched berdasarkan TimeOfDay â†’ Pickup di master Customer (TMMIN).</summary>
            public int MatchedCustomerId { get; set; }
            /// <summary>Nama dock yang auto-matched (untuk display di preview).</summary>
            public string? MatchedDockName { get; set; }
            /// <summary>Route dari dock yang auto-matched.</summary>
            public string? MatchedRoute { get; set; }
            /// <summary>Cycle dari dock yang auto-matched.</summary>
            public string? MatchedCycle { get; set; }
            /// <summary>Kode Dock mentah dari kolom G Excel (misal "6I", "43")</summary>
            public string? DetectedDockCode { get; set; }
            /// <summary>Route mentah dari kolom AD Excel</summary>
            public string? DetectedRouteCode { get; set; }
            /// <summary>Cycle mentah dari kolom M/C Excel</summary>
            public string? DetectedCycleCode { get; set; }
            /// <summary>Area (internal) untuk grouping cycle TMMIN</summary>
            public string? Area { get; set; }
        }

        public class MatchedDock
        {
            public int CustomerId { get; set; }
            public string CustomerCode { get; set; } = "";
            public string DockName { get; set; } = "";   // CustomerName = dock
            public string? Route { get; set; }
            public string? Cycle { get; set; }
            public string? Docking { get; set; }
            public string? Area { get; set; }
        }

        #endregion

        #region === Customer Detection Profiles ===

        private class DetectionProfile
        {
            public string Name { get; set; } = "";
            public string[] Keywords { get; set; } = Array.Empty<string>();
            public int MinKeywordMatch { get; set; } = 2;
        }

        private static readonly DetectionProfile[] Profiles = new[]
        {
            new DetectionProfile
            {
                Name = "AHM",
                Keywords = new[] { "ASTRA HONDA MOTOR", "AHM/VIN", "AHM/CKD", "GATE :", "Plant I" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "ADM",
                // PDF Delivery Note ADM + Excel SAP ADM (format "ADM KAP", "ADM ASSY3", dll)
                // PDF: "DAIHATSU MOTOR", "CYCLE ISSUE", "DELIVERY NOTE", dll
                // Excel SAP: Row1="PT. Astra Daihatsu Motor", Row2="[L-A030] Ordering Delivery Check"
                //            Header: Vendor Code, Vendor Alias, Order No, Del. Date, Del. Cycle, Doc No
                Keywords = new[] {
                    // PDF keywords
                    "DAIHATSU MOTOR", "DAIHATSU", "CYCLE ISSUE", "GROUP ROUTE", "E/G KARAWANG",
                    "DELIVERY NOTE", "MATERIAL No", "TOTAL QTY", "TOTAL KANBAN", "DN NO",
                    "VENDOR NAME", "VENDOR NO", "PICK UP DATE", "DESTINATION",
                    // Excel SAP ADM keywords
                    "Astra Daihatsu", "PT. Astra Daihatsu", "Ordering Delivery Check",
                    "Vendor Code", "Vendor Alias", "Del. Cycle", "Doc No"
                },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "TMMIN",
                Keywords = new[] { "SUPPLIER MANIFEST", "TMMIN", "DOCK CODE", "P-LANE CODE", "QTY OF SKID", "MANIFEST NO", "CS ROUTE", "1st TMMIN", "807D", "807B", "8078", "AP1", "RC25", "MN43" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "SANOH",
                Keywords = new[] { "SANOH INDONESIA", "Planned Receipt Date", "DN Number", "PT. SANOH" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "AWI",
                Keywords = new[] { "Parts Center Awi", "Receiving AWI", "Ware House", "ASTRA WHEEL" },
                MinKeywordMatch = 1
            },
            new DetectionProfile
            {
                Name = "KAYABA",
                Keywords = new[] { "KAYABA INDONESIA", "KYB", "DELIVERY ORDER" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "DENSO",
                Keywords = new[] { "DENSO INDONESIA", "DENSO", "DELIVERY NOTE" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                Name = "HINO",
                Keywords = new[] { "HINO MOTORS", "HINO MOTORS MANUFACTURING", "SUPP. COD", "Uniq No", "PROCESS NO", "S. PLACE", "PAD/FR" },
                MinKeywordMatch = 2
            },
            new DetectionProfile
            {
                // TMMIN TXT/ASCII Format: Mengandung baris dengan banyak tanggal dan angka 10-digit di awal
                Name = "TMMIN_TXT",
                Keywords = new[] { "807B", "8078", "RM13", "807D", "AP1", "RC25", "MN43", "2026-" }, 
                MinKeywordMatch = 2
            }
        };

        #endregion

        #region === Main Entry Point ===

        public async Task<SmartImportResult> ProcessFileAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();
            _logger.LogInformation("SmartImport: Processing '{FileName}' ({Length} bytes, {Ext})",
                file.FileName, file.Length, ext);

            SmartImportResult result;

            try
            {
                // For Excel: pre-buffer bytes into MemoryStream so format probe + parser
                // can each read from position 0 without stream-consumed errors.
                if (ext == ".xlsx" || ext == ".xls")
                {
                    var excelBuf = new MemoryStream((int)file.Length);
                    await file.CopyToAsync(excelBuf);
                    excelBuf.Position = 0;

                    // Probe format — always reset stream to 0 after, regardless of FormFile seek behaviour
                    bool isAdmKepNew = IsAdmKepNewFormat(excelBuf);
                    excelBuf.Position = 0;
                    
                    bool isTmminExcel = IsTmminExcelFormat(excelBuf);
                    excelBuf.Position = 0;

                    var rf = new Microsoft.AspNetCore.Http.FormFile(excelBuf, 0, excelBuf.Length, file.Name, file.FileName);

                    if (isTmminExcel)
                    {
                        result = await ParseTmminExcelAsync(rf);
                    }
                    else if (isAdmKepNew)
                    {
                        result = await ParseAdmKepExcelAsync(rf);
                    }
                    else
                    {
                        try
                        {
                            result = ProcessExcel(rf);
                        }
                        catch (Exception closedXmlEx)
                        {
                            // ClosedXML cannot open files that contain embedded images/shapes.
                            // Fall back to the OpenXML-based parser which tolerates such files.
                            _logger.LogWarning(closedXmlEx, "ProcessExcel (ClosedXML) failed — falling back to ParseAdmKepExcelAsync (OpenXML)");
                            excelBuf.Position = 0;
                            var rf2 = new Microsoft.AspNetCore.Http.FormFile(
                                excelBuf, 0, excelBuf.Length, file.Name, file.FileName);
                            result = await ParseAdmKepExcelAsync(rf2);
                        }
                    }
                }
                else
                {
                    result = ext switch
                    {
                        ".pdf" => await ProcessPdfAsync(file),
                        ".csv" or ".txt" => await ProcessTextAsync(file),
                        _ => new SmartImportResult
                        {
                            Success = false,
                            ErrorMessage = $"Format file '{ext}' tidak didukung. Gunakan PDF, Excel (.xlsx/.xls), CSV, atau TXT."
                        }
                    };
                }

                if (result.Success && result.Items.Any())
                {
                    // Match detected customer to database (for dock matching)
                    await MatchCustomerToDatabase(result);
                    // Aggressive Grouping Logic: Gabungkan Part No yang sama dalam manifest yang sama secara universal
                    // UPDATE: Tambahkan ScheduledDate, TimeOfDay, DetectedDockCode, DetectedRouteCode dalam key
                    var originalCount = result.Items.Count;
                    result.Items = result.Items
                        .GroupBy(i => (
                            i.Manifesting?.ToUpper() ?? "", 
                            i.DetectedDockCode?.ToUpper() ?? "", 
                            i.DetectedRouteCode?.ToUpper() ?? "", 
                            i.DetectedCycleCode?.ToUpper() ?? "",
                            i.PartNo?.ToUpper() ?? "", 
                            i.ScheduledDate?.Date,
                            // Abaikan TimeOfDay untuk TMMIN agar Area 1 & 2 digabung
                            (result.DetectedCustomerName == "TMMIN" || result.DetectedCustomerName == "TOYOTA" || result.DetectedCustomerName == "TMMIN_TXT") ? null : i.TimeOfDay
                        ))
                        .Select(g =>
                        {
                            var first = g.First();
                            first.Qty = g.Sum(x => x.Qty);
                            first.ExcelKanban = g.Sum(x => x.ExcelKanban);
                            first.TimeOfDay = g.Where(x => x.TimeOfDay.HasValue).OrderBy(x => x.TimeOfDay).FirstOrDefault()?.TimeOfDay ?? first.TimeOfDay;
                            return first;
                        })
                        .ToList();
 
                    if (result.Items.Count < originalCount)
                    {
                        _logger.LogInformation("SmartImport: Grouped {Original} items into {Grouped} unique records.", 
                            originalCount, result.Items.Count);
                    }

                    // â”€â”€ TMMIN Multi-Dock Matching: match jam/dock/route ke master Customer â”€â”€
                    // Dipanggil jika ada salah satu sinyal: TimeOfDay, DetectedDockCode, atau DetectedRouteCode
                    if ((result.DetectedCustomerName == "TMMIN" || result.DetectedCustomerName == "TMMIN_TXT")
                        && result.Items.Any(i => i.TimeOfDay.HasValue
                            || !string.IsNullOrEmpty(i.DetectedDockCode)
                            || !string.IsNullOrEmpty(i.DetectedRouteCode)))
                    {
                        await MatchTmminDocksByTime(result);
                    }

                    // AHM Multi-Dock Matching: match plant location per halaman ke master Customer
                    if (result.DetectedCustomerName == "AHM"
                        && result.Items.Any(i => !string.IsNullOrEmpty(i.DetectedDockCode)))
                    {
                        await MatchAhmDocksByPlant(result);

                        // Re-group setelah dock matching: gabungkan item dari DN berbeda yang
                        // punya PartNo + Dock + ScheduledDate + TimeOfDay sama → satu delivery item
                        var preRegroup = result.Items.Count;
                        result.Items = result.Items
                            .GroupBy(i => (
                                PN:    i.PartNo?.ToUpper() ?? "",
                                CustId: i.MatchedCustomerId,
                                Dock:  i.DetectedDockCode?.ToUpper() ?? "",
                                Date:  i.ScheduledDate?.Date,
                                TOD:   i.TimeOfDay
                            ))
                            .Select(g =>
                            {
                                var first = g.OrderBy(i => i.Manifesting).First();
                                return new ExtractedItem
                                {
                                    Manifesting       = first.Manifesting,
                                    PartNo            = first.PartNo,
                                    PartName          = first.PartName,
                                    Qty               = g.Sum(x => x.Qty),
                                    ScheduledDate     = first.ScheduledDate,
                                    TimeOfDay         = first.TimeOfDay,
                                    MatchedCustomerId = first.MatchedCustomerId,
                                    MatchedDockName   = first.MatchedDockName,
                                    MatchedRoute      = first.MatchedRoute,
                                    MatchedCycle      = first.MatchedCycle,
                                    DetectedDockCode  = first.DetectedDockCode,
                                    DetectedRouteCode = first.DetectedRouteCode,
                                };
                            })
                            .ToList();
                        if (result.Items.Count < preRegroup)
                            _logger.LogInformation("SmartImport AHM: Re-grouped {Before} → {After} items setelah dock matching (DN berbeda, part+dock+date sama).",
                                preRegroup, result.Items.Count);
                    }
                }


                // TMMIN Deduplication (untuk PDF, TXT, Excel)
                if (result.DetectedCustomerName == "TMMIN" || result.DetectedCustomerName == "TOYOTA")
                {
                    var preRegroupTmmin = result.Items.Count;
                    result.Items = result.Items
                        .GroupBy(i => (
                            Manifest: i.Manifesting?.ToUpper() ?? "",
                            PN:       i.PartNo?.ToUpper() ?? "",
                            CustId:   i.MatchedCustomerId,
                            Date:     i.ScheduledDate?.Date,
                            Cycle:    i.DetectedCycleCode?.ToUpper() ?? "",
                            Dock:     i.DetectedDockCode?.ToUpper() ?? ""
                        ))
                        .Select(g =>
                        {
                            var first = g.First();
                            first.Qty = g.Sum(x => x.Qty);
                            first.ExcelKanban = g.Sum(x => x.ExcelKanban);
                            first.TimeOfDay = g.Where(x => x.TimeOfDay.HasValue).OrderBy(x => x.TimeOfDay).FirstOrDefault()?.TimeOfDay;
                            return first;
                        })
                        .ToList();
                    
                    if (result.Items.Count < preRegroupTmmin)
                        _logger.LogInformation("SmartImport TMMIN: Re-grouped {Before} → {After} items (merging duplicate parts per manifest/dock/cycle).",
                            preRegroupTmmin, result.Items.Count);
                }

                result.FileFormat = ext.TrimStart('.');
                _logger.LogInformation("SmartImport: Done — Customer={Customer}, Items={Count}, Success={Success}",
                    result.DetectedCustomerName, result.Items.Count, result.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmartImport: Error processing '{FileName}'", file.FileName);
                result = new SmartImportResult
                {
                    Success = false,
                    ErrorMessage = $"Error memproses file: {ex.Message}"
                };
            }

            return result;
        }

        #endregion

        #region === PDF Processing ===

        private async Task<SmartImportResult> ProcessPdfAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            string allText;
            var allLines = new List<string>();
            // Per-page data untuk extractor yang butuh konteks per-halaman (TMMIN, ADM multi-page)
            var pagesData = new List<(string PageText, List<string> PageLines)>();

            using (var pdf = PdfDocument.Open(stream))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    var pageText = page.Text ?? "";
                    sb.AppendLine(pageText);

                    // Extract word-by-word grouped into lines by Y position (per halaman)
                    var words = page.GetWords().ToList();
                    var pageLines = GroupWordsIntoLines(words);

                    // Jika GroupWordsIntoLines menghasilkan sedikit baris padahal ada teks,
                    // fallback: split pageText berdasarkan newline sebagai tambahan
                    if (pageLines.Count < 3 && pageText.Contains('\n'))
                    {
                        var textLines = pageText.Split('\n')
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                            .ToList();
                        // Gabungkan keduanya — pageLines prioritas (lebih akurat posisi)
                        foreach (var tl in textLines)
                        {
                            if (!pageLines.Any(pl => pl.Contains(tl.Substring(0, Math.Min(tl.Length, 10)))))
                                pageLines.Add(tl);
                        }
                    }

                    allLines.AddRange(pageLines);
                    pagesData.Add((pageText, pageLines));
                }
                allText = sb.ToString();
            }

            _logger.LogInformation("SmartImport PDF: {Pages} halaman, {Lines} lines total, {Chars} chars",
                pagesData.Count, allLines.Count, allText.Length);

            // Detect customer
            // page.Text (allText) menggunakan content-stream order — bisa tidak berurutan untuk PDF kompleks.
            // allLines menggunakan GroupWordsIntoLines (berdasar koordinat Y) — lebih reliable untuk reading order.
            // Coba deteksi dari allText dulu; fallback ke allLines jika gagal (Hino, ADM complex layout, dll).
            var allLinesText = string.Join("\n", allLines);
            string? detectedCustomer = DetectCustomer(allText) ?? DetectCustomer(allLinesText);

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN"
            };

            // Extract manifest number — coba allText dulu, fallback ke allLinesText
            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);
            if (string.IsNullOrWhiteSpace(result.DetectedManifest))
                result.DetectedManifest = ExtractManifestNumber(allLinesText, detectedCustomer);

            // Extract dock info + route/cycle (untuk filter dock yang lebih akurat)
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer)
                ?? ExtractDockInfo(allLinesText, detectedCustomer);
            var (detectedRoute, detectedCycle) = ExtractRouteCycle(allText, detectedCustomer);
            if (detectedRoute == null && detectedCycle == null)
                (detectedRoute, detectedCycle) = ExtractRouteCycle(allLinesText, detectedCustomer);
            result.DetectedRoute = detectedRoute;
            result.DetectedCycle = detectedCycle;

            // Extract items — customer dengan format multi-halaman pakai extractor per-halaman
            // agar manifest di setiap halaman terbaca dan di-assign ke item halaman tersebut
            // AHM juga menggunakan multi-page agar Plant (dock) per-halaman bisa di-detect
            if (detectedCustomer == "TMMIN" || detectedCustomer == "ADM" || detectedCustomer == "HINO" || detectedCustomer == "AHM")
            {
                result.Items = ExtractItemsMultiPage(pagesData, detectedCustomer, result.DetectedManifest);
            }
            else
            {
                result.Items = ExtractItemsFromText(allText, allLines, detectedCustomer, result.DetectedManifest);
            }

            // If items have manifest from per-line extraction (better than header detection), use first item's manifest
            if (result.Items.Any() && !string.IsNullOrWhiteSpace(result.Items.First().Manifesting))
            {
                var itemManifest = result.Items.First().Manifesting;
                // Only override if DetectedManifest is empty/null or non-numeric (e.g. "SUPPLIER")
                if (string.IsNullOrWhiteSpace(result.DetectedManifest) ||
                    !Regex.IsMatch(result.DetectedManifest, @"^\d+$"))
                {
                    result.DetectedManifest = itemManifest;
                }
            }

            // Ambil DetectedScheduledDate dari item pertama yang punya ScheduledDate (Hino: DEL. DATE)
            if (result.DetectedScheduledDate == null)
            {
                result.DetectedScheduledDate = result.Items.FirstOrDefault(i => i.ScheduledDate.HasValue)?.ScheduledDate;
            }

            // Jika masih null, coba extract tanggal dari teks PDF (ADM: ORDER DATE, ISSUE DATE, dll)
            if (result.DetectedScheduledDate == null)
            {
                result.DetectedScheduledDate = ExtractScheduledDateFromPdfText(allText, detectedCustomer)
                    ?? ExtractScheduledDateFromPdfText(allLinesText, detectedCustomer);
                // Propagate ke semua item yang belum punya tanggal
                if (result.DetectedScheduledDate.HasValue)
                {
                    foreach (var item in result.Items.Where(i => !i.ScheduledDate.HasValue))
                        item.ScheduledDate = result.DetectedScheduledDate;
                    _logger.LogInformation("SmartImport PDF {Cust}: DetectedScheduledDate = {Date} (dari teks PDF, dipropagasi ke {N} items)",
                        detectedCustomer, result.DetectedScheduledDate.Value.ToString("yyyy-MM-dd"), result.Items.Count);
                }
            }

            if (!result.Items.Any())
            {
                result.Warnings.Add("Tidak ada item yang berhasil diekstrak dari PDF. Sistem mencoba berbagai pattern Part Number.");
            }

            if (detectedCustomer == null)
            {
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");
            }

            result.ParserUsed = detectedCustomer ?? "GENERIC";
            return result;
        }

        /// <summary>
        /// Ekstrak item dari PDF multi-halaman dengan memproses setiap halaman secara terpisah.
        /// Ini memastikan MANIFEST NO. di header setiap halaman terbaca dan di-assign ke item di halaman itu.
        /// </summary>
        private List<ExtractedItem> ExtractItemsMultiPage(
            List<(string PageText, List<string> PageLines)> pages,
            string? customer,
            string? headerManifest)
        {
            var allItems = new List<ExtractedItem>();

            // Manifest aktif dari halaman sebelumnya — di-carry-over jika halaman baru tidak punya manifest header
            string carryManifest = headerManifest ?? "";

            for (int pageIdx = 0; pageIdx < pages.Count; pageIdx++)
            {
                var (pageText, pageLines) = pages[pageIdx];

                // Jika halaman ini tidak punya baris item sama sekali (misalnya halaman cover/ringkasan), skip
                // tapi tetap cek manifest di halaman ini untuk carry-over
                List<ExtractedItem> pageItems;
                if (customer == "TMMIN")
                {
                    pageItems = ExtractItemsTmmin(pageText, pageLines, carryManifest);
                }
                else if (customer == "ADM")
                {
                    pageItems = ExtractItemsAdm(pageText, pageLines, carryManifest);
                }
                else if (customer == "HINO")
                {
                    pageItems = ExtractItemsHino(pageText, pageLines, carryManifest);
                }
                else if (customer == "AHM")
                {
                    pageItems = ExtractItemsAhm(pageText, pageLines, carryManifest);
                }
                else
                {
                    pageItems = ExtractItemsFromText(pageText, pageLines, customer, carryManifest);
                }

                // Update carry manifest: jika halaman ini menghasilkan item, ambil manifest dari item terakhir
                // sehingga halaman berikutnya yang tidak punya manifest header tetap pakai manifest yang benar
                if (pageItems.Any())
                {
                    var lastManifest = pageItems.Last().Manifesting;
                    if (!string.IsNullOrWhiteSpace(lastManifest))
                        carryManifest = lastManifest;
                }
                else
                {
                    // Tidak ada item di halaman ini — cek apakah ada manifest number di teks halaman ini
                    // untuk di-carry ke halaman berikutnya
                    var manifestOnPage = ExtractManifestNumber(pageText, customer);
                    if (!string.IsNullOrWhiteSpace(manifestOnPage))
                        carryManifest = manifestOnPage;
                }

                // Merge items
                foreach (var item in pageItems)
                {
                    allItems.Add(item);
                }

                _logger.LogInformation("SmartImport {Customer} Page {Page}/{Total}: {Count} items, manifest='{Manifest}'",
                    customer, pageIdx + 1, pages.Count, pageItems.Count, carryManifest);
            }

            // ADM: Part No yang sama di halaman berbeda (manifest sama) â†’ jumlahkan Qty
            // Ini menggabungkan item identik dari semua halaman menjadi 1 baris
            if (customer == "ADM")
            {
                allItems = allItems
                    .GroupBy(i => (i.Manifesting?.ToUpper() ?? "", i.PartNo?.ToUpper() ?? ""))
                    .Select(g => new ExtractedItem
                    {
                        Manifesting = g.First().Manifesting,
                        PartNo = g.First().PartNo,
                        PartName = g.First().PartName,
                        Qty = g.Sum(x => x.Qty),
                        ScheduledDate = g.FirstOrDefault(x => x.ScheduledDate.HasValue)?.ScheduledDate
                    })
                    .ToList();
            }

            // HINO & TMMIN_TXT: Part No yang sama dalam satu manifest â†’ jumlahkan Qty
            if (customer == "HINO" || customer == "TMMIN_TXT")
            {
                allItems = allItems
                    .GroupBy(i => (i.Manifesting?.ToUpper() ?? "", i.PartNo?.ToUpper() ?? ""))
                    .Select(g => new ExtractedItem
                    {
                        Manifesting = g.First().Manifesting,
                        PartNo = g.First().PartNo,
                        PartName = g.First().PartName,
                        Qty = g.Sum(x => x.Qty),
                        ScheduledDate = g.FirstOrDefault(x => x.ScheduledDate.HasValue)?.ScheduledDate
                    })
                    .ToList();
            }

            _logger.LogInformation("SmartImport {Customer} Total: {Count} items dari {Pages} halaman (after merge)",
                customer, allItems.Count, pages.Count);

            return allItems;
        }

        private List<string> GroupWordsIntoLines(List<Word> words)
        {
            if (!words.Any()) return new List<string>();

            var sorted = words.OrderBy(w => -w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left).ToList();
            var lines = new List<List<Word>>();
            var currentLine = new List<Word> { sorted[0] };
            var currentY = sorted[0].BoundingBox.Bottom;

            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i].BoundingBox.Bottom - currentY) < 3)
                {
                    currentLine.Add(sorted[i]);
                }
                else
                {
                    lines.Add(currentLine.OrderBy(w => w.BoundingBox.Left).ToList());
                    currentLine = new List<Word> { sorted[i] };
                    currentY = sorted[i].BoundingBox.Bottom;
                }
            }
            lines.Add(currentLine.OrderBy(w => w.BoundingBox.Left).ToList());

            return lines.Select(line => string.Join(" ", line.Select(w => w.Text))).ToList();
        }



        #endregion

        #region === Excel Processing ===

        private SmartImportResult ProcessExcel(IFormFile file)
        {
            using var stream = new MemoryStream();
            file.CopyTo(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);

            // Find first sheet with data
            IXLWorksheet? ws = null;
            foreach (var w in workbook.Worksheets)
            {
                if ((w.LastRowUsed()?.RowNumber() ?? 0) > 0)
                {
                    ws = w;
                    break;
                }
            }

            if (ws == null)
            {
                return new SmartImportResult { Success = false, ErrorMessage = "File Excel kosong atau tidak memiliki data." };
            }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            // Scan a larger area for customer detection and metadata (100 rows, 100 columns)
            var allTextSb = new System.Text.StringBuilder();
            for (int r = 1; r <= Math.Min(lastRow, 100); r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 100); c++)
                {
                    try {
                        var val = ws.Cell(r, c).Value.ToString();
                        if (!string.IsNullOrWhiteSpace(val)) allTextSb.Append(val + " ");
                    } catch { }
                }
                allTextSb.AppendLine();
            }

            var allText = allTextSb.ToString();
            string? detectedCustomer = DetectCustomer(allText);

            // â”€â”€ Deteksi struktural ADM Excel (SAP) jika keyword-based gagal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // File Excel ADM dari SAP: Row 1 = "PT. Astra Daihatsu Motor"
            //                          Row 2 = "[L-A030] Ordering Delivery Check"
            // Cukup cek row 1-3 pertama untuk string "Astra Daihatsu" atau "Ordering Delivery"
            if (detectedCustomer == null)
            {
                bool hasAdmHeader = false;
                for (int r = 1; r <= Math.Min(3, lastRow); r++)
                {
                    for (int c = 1; c <= Math.Min(lastCol, 5); c++)
                    {
                        try
                        {
                            var cellVal = ws.Cell(r, c).Value.ToString().Trim();
                            if (cellVal.IndexOf("Astra Daihatsu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                cellVal.IndexOf("Ordering Delivery", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hasAdmHeader = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    if (hasAdmHeader) break;
                }

                if (hasAdmHeader)
                {
                    detectedCustomer = "ADM";
                    _logger.LogInformation("SmartImport Excel: ADM terdeteksi via structural check (Astra Daihatsu / Ordering Delivery di baris 1-3)");
                }
                else
                {
                    // Fallback: cek keyword SAP ADM di allText (Vendor Code + Del. Cycle + Doc No)
                    var admSapKeywords = new[] { "Vendor Code", "Vendor Alias", "Del. Cycle", "Doc No", "Order Cycle" };
                    int admMatches = admSapKeywords.Count(kw => allText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0);
                    _logger.LogInformation("SmartImport Excel ADM SAP keyword fallback: {Count}/{Total} matches", admMatches, admSapKeywords.Length);
                    if (admMatches >= 3)
                    {
                        detectedCustomer = "ADM";
                        _logger.LogInformation("SmartImport Excel: ADM terdeteksi via SAP keyword fallback ({Count} keywords matched)", admMatches);
                    }
                }
            }

            // â”€â”€ Deteksi struktural TMMIN Excel jika keyword-based gagal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // File TMMIN Excel tidak punya teks "TMMIN" tapi punya ciri:
            // 1. Baris pertama berisi metadata pipe-separated: "5453|1|202604011200|..."
            // 2. Manifest 10-digit diawali 4 atau 6: "4260210875"
            // 3. Kode lokasi TMMIN: 807D/807B/8078/RM13/AP1/RC25/MN43/BZ2
            if (detectedCustomer == null)
            {
                // Cek baris 1 untuk pola metadata pipe
                var row1Cell1 = "";
                try { row1Cell1 = ws.Cell(1, 1).Value.ToString().Trim(); } catch { }

                bool hasPipeMetadata = row1Cell1.Contains("|") && Regex.IsMatch(row1Cell1, @"\d{4,}\|");
                // Cek manifest di baris 2 (10 digit diawali 4 atau 6)
                var row2Manifest = "";
                try { row2Manifest = ws.Cell(2, 1).Value.ToString().Trim(); } catch { }
                bool hasTmminManifest = Regex.IsMatch(row2Manifest, @"^[46]\d{9}$");

                // Cek kode lokasi TMMIN di allText
                var tmminLocationCodes = new[] { "807D", "807B", "8078", "RM13", "AP1 ", "RC25", "MN43", "BZ2", "RD33", "RC2", "RS0" };
                int locationMatches = tmminLocationCodes.Count(code => allText.Contains(code, StringComparison.OrdinalIgnoreCase));

                _logger.LogInformation("SmartImport Excel TMMIN structural check: hasPipeMetadata={P}, hasTmminManifest={M}, locationMatches={L} (row1='{R1}', row2manifest='{R2}')",
                    hasPipeMetadata, hasTmminManifest, locationMatches, row1Cell1.Length > 50 ? row1Cell1.Substring(0, 50) : row1Cell1, row2Manifest);

                if ((hasPipeMetadata && hasTmminManifest) || (hasTmminManifest && locationMatches >= 1) || (hasPipeMetadata && locationMatches >= 2))
                {
                    detectedCustomer = "TMMIN";
                    _logger.LogInformation("SmartImport Excel: TMMIN terdeteksi via structural check (pipe={P}, manifest={M}, location={L})",
                        hasPipeMetadata, hasTmminManifest, locationMatches);
                }
            }

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN",
                ParserUsed = (detectedCustomer ?? "GENERIC") + "_EXCEL"
            };

            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer);
            var (detectedRouteExcel, detectedCycleExcel) = ExtractRouteCycle(allText, detectedCustomer);
            result.DetectedRoute = detectedRouteExcel;
            result.DetectedCycle = detectedCycleExcel;

            // Extract from Excel: find header row with Part No / Qty columns
            result.Items = ExtractItemsFromExcel(ws, lastRow, lastCol, detectedCustomer, result.DetectedManifest, result);

            // POST-FIX DetectedScheduledDate untuk Excel:
            // Jika item tidak memiliki ScheduledDate (kolom tanggal tidak ditemukan di tabel),
            // coba extract dari teks header Excel (baris metadata di atas tabel, mis: "DN Date : 26-MAR-2026")
            if (result.DetectedScheduledDate == null)
            {
                result.DetectedScheduledDate = result.Items.FirstOrDefault(i => i.ScheduledDate.HasValue)?.ScheduledDate;
            }
            if (result.DetectedScheduledDate == null && !string.IsNullOrEmpty(allText))
            {
                result.DetectedScheduledDate = ExtractScheduledDateFromPdfText(allText, detectedCustomer);
                if (result.DetectedScheduledDate.HasValue)
                {
                    foreach (var item in result.Items.Where(i => !i.ScheduledDate.HasValue))
                        item.ScheduledDate = result.DetectedScheduledDate;
                    _logger.LogInformation("SmartImport Excel {Cust}: DetectedScheduledDate = {Date} (dari header/metadata Excel, dipropagasi ke {N} items)",
                        detectedCustomer, result.DetectedScheduledDate.Value.ToString("yyyy-MM-dd"), result.Items.Count);
                }
            }

            // POST-FIX: If DetectedManifest is empty or just a generic word (like "Number"), use the first item's manifest
            var invalidManifests = new[] { "NUMBER", "MANIFEST", "ORDER", "SJ", "DN", "" };
            if (string.IsNullOrWhiteSpace(result.DetectedManifest) || invalidManifests.Contains(result.DetectedManifest.ToUpper()))
            {
                var rowManifest = result.Items.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Manifesting))?.Manifesting;
                if (!string.IsNullOrWhiteSpace(rowManifest) && !invalidManifests.Contains(rowManifest.ToUpper()))
                {
                    result.DetectedManifest = rowManifest;
                }
            }

            if (!result.Items.Any())
                result.Warnings.Add("Tidak ada item yang berhasil diekstrak dari Excel. Coba periksa apakah header 'Part No' dan 'Qty' sudah benar.");

            if (detectedCustomer == null)
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");

            return result;
        }

        #endregion

        #region === CSV / TXT Processing ===

        private async Task<SmartImportResult> ProcessTextAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName).ToLower();
            using var reader = new StreamReader(file.OpenReadStream());
            var allText = await reader.ReadToEndAsync();

            // For CSV, split by comma/semicolon; for TXT, split by newline
            var lines = allText.Split('\n')
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // For tab/semicolon-delimited, expand each line
            var expandedLines = new List<string>();
            foreach (var line in lines)
            {
                expandedLines.Add(line.Trim());
                // Also add sub-parts split by tab or semicolon for better parsing
                if (line.Contains('\t') || line.Contains(';'))
                {
                    var parts = line.Split(new[] { '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                        if (!string.IsNullOrWhiteSpace(p)) expandedLines.Add(p.Trim());
                }
            }

            string? detectedCustomer = DetectCustomer(allText);

            var result = new SmartImportResult
            {
                Success = true,
                DetectedCustomerName = detectedCustomer ?? "UNKNOWN",
                ParserUsed = ext == ".csv" ? "CSV" : "TXT"
            };

            result.DetectedManifest = ExtractManifestNumber(allText, detectedCustomer);
            result.DetectedDock = ExtractDockInfo(allText, detectedCustomer);
            var (detectedRouteTxt, detectedCycleTxt) = ExtractRouteCycle(allText, detectedCustomer);
            result.DetectedRoute = detectedRouteTxt;
            result.DetectedCycle = detectedCycleTxt;
            result.DetectedScheduledDate = ExtractScheduledDateFromPdfText(allText, detectedCustomer);
            result.Items = ExtractItemsFromText(allText, lines, detectedCustomer, result.DetectedManifest);

            if (!result.Items.Any())
                result.Warnings.Add($"Tidak ada item yang berhasil diekstrak dari file {ext.TrimStart('.')}.");

            if (detectedCustomer == null)
                result.Warnings.Add("Customer tidak terdeteksi otomatis. Pilih customer secara manual.");

            return result;
        }

        #endregion

        #region === Customer Detection ===

        private string? DetectCustomer(string text)
        {
            // â”€â”€ NEW: Check Dynamic Templates first â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            try
            {
                var customTemplates = GetDynamicTemplates(); // Gunakan cache & include mappings
                foreach (var t in customTemplates)
                {
                    // 1. Cek Keyword Khusus 'Customer' di Mapping (Prioritas Tinggi)
                    var custMapping = t.Mappings.FirstOrDefault(m => m.TargetField == "Customer");
                    if (custMapping != null && !string.IsNullOrEmpty(custMapping.SourceKeywords))
                    {
                        var keywords = custMapping.SourceKeywords.Split(',')
                                        .Select(k => k.Trim())
                                        .Where(k => k.Length > 0);
                        
                        if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                        {
                            _logger.LogInformation("SmartImport: Detected '{Name}' via explicit Customer Mapping keywords", t.CustomerName);
                            return t.CustomerName;
                        }
                    }

                    // 2. Fallback: Cek Nama Template atau Nama Customer (Prioritas Sedang)
                    if (text.Contains(t.Name, StringComparison.OrdinalIgnoreCase) ||
                        text.Contains(t.CustomerName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("SmartImport: Detected '{Name}' via Custom Template Name match", t.Name);
                        return t.CustomerName;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SmartImport: Error checking custom templates in DetectCustomer: {Msg}", ex.Message);
            }

            // â”€â”€ Original Hardcoded Profiles â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            foreach (var profile in Profiles)
            {
                var matchedKeywords = profile.Keywords
                    .Where(kw => text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchedKeywords.Count >= profile.MinKeywordMatch)
                {
                    _logger.LogInformation("SmartImport: Detected '{Name}' ({Count}/{Total} keywords)",
                        profile.Name, matchedKeywords.Count, profile.Keywords.Length);
                    return profile.Name;
                }
            }

            _logger.LogWarning("SmartImport DetectCustomer: No customer detected.");
            return null;
        }

        #endregion

        #region === Manifest Number Extraction ===

        private string? ExtractManifestNumber(string text, string? customer)
        {
            // Customer-specific patterns first
            var patterns = new List<string>();

            switch (customer)
            {
                case "AHM":
                    patterns.Add(@"No\s*DN\s*:\s*(\S+)");
                    patterns.Add(@"AHM[/\\](?:VIN|CKD)[/\\](\d+)");
                    break;
                case "HINO":
                    // HINO header format: "NO   : 2103902535"
                    // Bisa juga muncul sebagai "NO. : 2103902535" atau "NO: 2103902535"
                    patterns.Add(@"\bNO\.?\s*:\s*(\d{7,12})\b");
                    // Fallback: PAD/FR/LAS/00/011 di kanan atas (document reference, bukan manifest)
                    // Gunakan angka 7-12 digit standalone sebagai manifest fallback
                    break;
                case "ADM":
                    // ADM DN NO format: "DN27A02602130173" — DN + 2digit + huruf + digits, total ~16 char
                    // Pola di PDF: "DN NO. : DN27A02602130173"
                    patterns.Add(@"DN\s*NO\.?\s*:?\s*(DN[A-Z0-9]{10,20})");
                    // Excel ADM: "Order No : 12345678" atau "Order No (Manifest) : 12345678"
                    patterns.Add(@"(?:Order|Manifest)\s*No\.?.*?\s*[:\s]*([A-Z0-9\-/]{5,25})");
                    // Fallback: apapun setelah "DN NO." yang panjangnya >= 10
                    patterns.Add(@"DN\s*NO\.?\s*[:\s]+([A-Z0-9]{10,25})");
                    // Fallback lama (sebelumnya)
                    patterns.Add(@"DN\d{2}[A-Z]\d{10,}");
                    break;
                case "TMMIN":
                case "TMMIN_TXT":
                    // TMMIN: "MANIFEST NO." atau pola 10-digit (misal 1261103498) yang muncul di awal baris
                    patterns.Add(@"MANIFEST\s*NO\.?\s*[:\s]*(\d{7,13})");
                    // Untuk TMMIN TXT, cari pola 10 digit yang sering muncul (di awal baris)
                    patterns.Add(@"(?:^|\n)\s*(\d{10})\s+");
                    
                    // Prioritaskan pola MANIFEST NO eksplisit
                    foreach (var p in patterns)
                    {
                        var m = Regex.Match(text, p, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups[1].Value.Length >= 7)
                            return m.Groups[1].Value;
                    }
                    // Fallback: angka 10 digit standalone (Hanya jika mulai dengan 4 atau 6 - ciri khas TMMIN)
                    // Dan pastikan bukan Order No (biasanya mulai 202)
                    var tmminMatch = Regex.Match(text, @"(?<!\d)([46]\d{9})(?!\d)");
                    if (tmminMatch.Success && !tmminMatch.Value.StartsWith("202"))
                        return tmminMatch.Value;
                    
                    return null; // TMMIN only uses numeric manifest — don't fall through to generic patterns
                case "SANOH":
                    patterns.Add(@"DN\s*Number\s*:?\s*([A-Z0-9]+)");
                    break;
                case "AWI":
                    patterns.Add(@"DN\s*NO\.?\s*:?\s*([A-Z0-9\-/]+)");
                    break;
            }

            // Generic patterns (only for non-TMMIN customers)
            patterns.Add(@"DN\s*(?:NO|Number)\.?\s*:?\s*([A-Z0-9\-/]+)");
            patterns.Add(@"(?:No|Nomor)\s*(?:DN|SJ|DO)\s*:?\s*([A-Z0-9\-/]+)");
            patterns.Add(@"No\s*DN\s*:\s*(\S+)");

            var invalidWords = new[] { "NUMBER", "MANIFEST", "ORDER", "NO", "NOMOR", "SJ", "DN" };
            foreach (var pattern in patterns)
            {
                var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (m.Success && m.Groups[1].Value.Length >= 3)
                {
                    var val = m.Groups[1].Value.Trim();
                    if (!invalidWords.Contains(val.ToUpper()))
                        return val;
                }
            }

            return null;
        }

        #endregion

        #region === Dock Info Extraction ===

        private string? ExtractDockInfo(string text, string? customer)
        {
            switch (customer)
            {
                case "AHM":
                    var gateMatch = Regex.Match(text, @"GATE\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
                    if (gateMatch.Success) return gateMatch.Groups[1].Value;
                    break;
                case "TMMIN":
                    // DOCK CODE bisa berisi angka dan/atau huruf (misal: "6I", "43", "53")
                    var dockMatch = Regex.Match(text, @"DOCK\s*CODE\s*[:\s]*([A-Z0-9]{1,5})", RegexOptions.IgnoreCase);
                    if (dockMatch.Success) return "DOCK " + dockMatch.Groups[1].Value;
                    break;
                case "ADM":
                    // GROUP ROUTE di PDF ADM: "GROUP ROUTE : IKP" — route ADM = 2-5 huruf
                    var routeMatch = Regex.Match(text, @"GROUP\s*ROUTE\s*[:\s]+([A-Z]{2,5})\b", RegexOptions.IgnoreCase);
                    if (routeMatch.Success) return routeMatch.Groups[1].Value.Trim().ToUpper();
                    break;
                case "HINO":
                    // S. PLACE atau DEL. DATE bisa jadi info lokasi;
                    // gunakan SUPP. COD sebagai referensi supplier
                    var suppMatch = Regex.Match(text, @"SUPP\.?\s*COD\s*[:\s]*([\w]+)", RegexOptions.IgnoreCase);
                    if (suppMatch.Success) return "SUPP:" + suppMatch.Groups[1].Value.Trim();
                    break;
            }
            return null;
        }

        #endregion

        /// <summary>
        /// Ekstrak Route dan Cycle dari file untuk menyaring dock yang cocok.
        /// Dipanggil khusus untuk ADM — GROUP ROUTE dan CYCLE ISSUE ada di header PDF.
        /// </summary>
        private (string? route, string? cycle) ExtractRouteCycle(string text, string? customer)
        {
            string? route = null;
            string? cycle = null;

            switch (customer)
            {
                case "ADM":
                    // "GROUP ROUTE : IKP" — route ADM biasanya 2-5 huruf
                    var routeM = Regex.Match(text, @"GROUP\s*ROUTE\s*[:\s]+([A-Z]{2,5})\b", RegexOptions.IgnoreCase);
                    if (routeM.Success) route = routeM.Groups[1].Value.Trim().ToUpper();

                    // "CYCLE ISSUE : 203" — cycle ADM selalu angka (3-5 digit)
                    var cycleM = Regex.Match(text, @"CYCLE\s*ISSUE\s*[:\s]+(\d{1,5})\b", RegexOptions.IgnoreCase);
                    if (cycleM.Success) cycle = cycleM.Groups[1].Value.Trim();
                    break;

                case "TMMIN":
                    // CS ROUTE di TMMIN
                    var tmminRoute = Regex.Match(text, @"CS\s*ROUTE\s*[:\s]+([A-Z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (tmminRoute.Success) route = tmminRoute.Groups[1].Value.Trim().ToUpper();
                    break;
            }

            return (route, cycle);
        }

        /// <summary>
        /// Extract tanggal jadwal dari teks PDF untuk semua customer.
        /// ADM: "ORDER DATE : 17/03/2026", "ISSUE DATE : 17-Mar-2026", "DELIVERY DATE : 17/03/2026"
        /// TMMIN: "DELIVERY DATE : 17/03/2026"
        /// HINO: "DEL. DATE : 17/03/2026" (sudah ditangani di ExtractItemsHino)
        /// Generic: tanggal pertama dengan format dd/MM/yyyy atau yyyy-MM-dd
        /// </summary>
        private DateTime? ExtractScheduledDateFromPdfText(string text, string? customer)
        {
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                // 1. Prioritas Utama: Gunakan Dynamic Templates mapping 'Date'
                var templates = GetDynamicTemplates();
                if (!string.IsNullOrEmpty(customer))
                {
                    var template = templates.FirstOrDefault(t => t.CustomerName == customer);
                    if (template != null)
                    {
                        var dateMapping = template.Mappings.FirstOrDefault(m => m.TargetField == "Date");
                        if (dateMapping != null && !string.IsNullOrEmpty(dateMapping.SourceKeywords))
                        {
                            var keywords = dateMapping.SourceKeywords.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0);
                            foreach (var kw in keywords)
                            {
                                int idx = text.IndexOf(kw, StringComparison.OrdinalIgnoreCase);
                                if (idx >= 0)
                                {
                                    // Ambil teks setelah keyword (sekitar 30 karakter) untuk mencari tanggal
                                    var chunk = text.Substring(idx + kw.Length, Math.Min(35, text.Length - (idx + kw.Length)));
                                    var date = TryParseSmartDate(chunk);
                                    if (date.HasValue) 
                                    {
                                        _logger.LogInformation("SmartImport: Detected Date '{Date}' via dynamic keyword '{Kw}'", date.Value.ToString("yyyy-MM-dd"), kw);
                                        return date;
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. Fallbacks: Gunakan pattern deteksi otomatis (seperti yang sudah ada)
                var normalized = Regex.Replace(text, @"\s+", " ");
                var datePatternWithMonth = @"(\d{1,2}[-/\s][A-Za-z]{3,9}[-/\s]\d{2,4})";
                var datePatternNumeric = @"(\d{1,2}[-/\s]\d{1,2}[-/\s]\d{2,4})|(\d{8})";

                var labelPatterns = new List<string> {
                    @"DN\s*Date\s*[:\s]+" + datePatternWithMonth,
                    @"DN\s*Date\s*[:\s]+" + datePatternNumeric,
                    @"DELIVERY\s*DATE\s*[:\s]+" + datePatternWithMonth,
                    @"DELIVERY\s*DATE\s*[:\s]+" + datePatternNumeric,
                    @"Depart\s*Date\s*[:\s]+" + datePatternWithMonth,
                    @"ORDER\s*DATE\s*[:\s]+" + datePatternNumeric,
                    @"CALC\.?\s*DATE\s*[:\s]+(\d{4}-\d{2}-\d{2})"
                };

                foreach (var pattern in labelPatterns)
                {
                    var m = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var date = TryParseSmartDate(m.Groups[1].Value.Trim());
                        if (date.HasValue) return date;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SmartImport: Error in ExtractScheduledDate: {Msg}", ex.Message);
            }

            return null;
        }

        private DateTime? TryParseSmartDate(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            // Bersihkan input (hapus titik dua, spasi berlebih, dll di awal)
            string clean = Regex.Replace(input, @"^[:\s]+", "").Trim();
            
            // Ambil pattern tanggal pertama yang ditemukan: dd-MMM-yyyy atau dd/MM/yyyy atau yyyyMMdd
            var dateMatch = Regex.Match(clean, @"(\d{1,2}[-/\s][A-Za-z]{3,9}[-/\s]\d{2,4})|(\d{1,2}[-/\s]\d{1,2}[-/\s]\d{2,4})|(\d{8})");
            if (!dateMatch.Success) return null;

            string rawDate = dateMatch.Value;
            
            // Coba berbagai format
            string[] formats = { 
                "dd-MMM-yyyy", "dd-MMM-yy", "d-MMM-yyyy", "d-MMM-yy",
                "dd/MMM/yyyy", "d/MMM/yyyy", "dd MMM yyyy", "d MMM yyyy",
                "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d-M-yyyy",
                "yyyy-MM-dd", "yyyyMMdd", "yyyy/MM/dd", "dd/MM/yy"
            };

            if (DateTime.TryParseExact(rawDate, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime resExact))
                return resExact;

            if (DateTime.TryParse(rawDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime res)) 
                return res;

            return null;
        }



        #region === Item Extraction (Text/PDF) ===

        private List<ExtractedItem> ExtractItemsFromText(string allText, List<string> lines, string? customer, string? manifest)
        {
            // TMMIN TXT Parser (Fixed Width / Space Separated)
            if (customer == "TMMIN_TXT")
            {
                return ExtractItemsTmminTxt(lines, manifest);
            }

            // Use customer-specific extractor if available
            if (customer == "TMMIN")
                return ExtractItemsTmmin(allText, lines, manifest);
            if (customer == "ADM")
                return ExtractItemsAdm(allText, lines, manifest);
            if (customer == "HINO")
                return ExtractItemsHino(allText, lines, manifest);

            var items = new List<ExtractedItem>();

            // Part number patterns — ordered from most specific to generic
            var partPatterns = GetPartNumberPatterns(customer);

            // Try to find manifest number per-block/per-page
            // Build a map: line index â†’ manifest active at that point
            string activeManifest = manifest ?? "";

            foreach (var line in lines)
            {
                // Check if this line contains a manifest/DN number — update active manifest
                var manifestOnLine = TryExtractManifestFromLine(line, customer);
                if (!string.IsNullOrEmpty(manifestOnLine))
                {
                    activeManifest = manifestOnLine;
                    continue; // manifest line itself — no part number here
                }

                foreach (var pattern in partPatterns)
                {
                    var partMatch = Regex.Match(line, pattern);
                    if (!partMatch.Success) continue;

                    var partNo = partMatch.Groups[1].Success ? partMatch.Groups[1].Value : partMatch.Value;
                    partNo = partNo.Replace(" ", "").Trim().ToUpper();

                    // Skip if looks like a date or phone number
                    if (Regex.IsMatch(partNo, @"^\d{2}[-/]\d{2}[-/]\d{4}$")) continue;
                    if (partNo.Length < 5) continue;

                    // Extract quantity — find numbers in the line
                    int qty = ExtractQuantity(line, partNo);
                    if (qty <= 0) continue;

                    // Extract part name
                    string partName = ExtractPartName(line, partMatch);

                    // Allow all items so they can be summed by manifest + part in ProcessFileAsync
                    items.Add(new ExtractedItem
                    {
                        Manifesting = activeManifest,
                        PartNo = partNo,
                        PartName = partName,
                        Qty = qty
                    });

                    break; // One match per line
                }
            }

            return items;
        }

        /// <summary>
        /// TMMIN-specific extractor — reads "MANIFEST NO." header + item rows (NO | PART NO | PART NAME | ... | TTL QTY)
        /// </summary>
        private List<ExtractedItem> ExtractItemsTmmin(string allText, List<string> lines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();

            // TMMIN part number: 5digit-5alphanumeric-2digit  e.g. 44750-VT010-00, 16261-0Y030-00
            var partPattern = new Regex(@"(\d{5}-[A-Z0-9]{5}-\d{2})", RegexOptions.IgnoreCase);

            // Per-manifest tracking: each manifest block may have its own MANIFEST NO.
            // Scan lines, update activeManifest whenever we see "MANIFEST NO." pattern
            // Only use headerManifest if it's purely numeric (not "SUPPLIER" etc.)
            string activeManifest = (!string.IsNullOrWhiteSpace(headerManifest) && Regex.IsMatch(headerManifest, @"^\d+$"))
                ? headerManifest : "";

            var manifestLineRegex = new Regex(@"MANIFEST\s*NO\.?\s*[:\s]*(\d{7,13})", RegexOptions.IgnoreCase);
            var tenDigitRegex = new Regex(@"(?<!\d)(\d{10})(?!\d)");

            foreach (var line in lines)
            {
                // Update manifest if this line has a MANIFEST NO.
                var mMatch = manifestLineRegex.Match(line);
                if (mMatch.Success)
                {
                    activeManifest = mMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Standalone 10-digit number detection - TMMIN specific safety
                // Skip lines that look like ORDER NO or contain sequence numbers
                if (line.Trim().Length <= 25 && !line.Contains("ORDER", StringComparison.OrdinalIgnoreCase))
                {
                    var tenMatch = tenDigitRegex.Match(line.Trim());
                    if (tenMatch.Success)
                    {
                        var val = tenMatch.Groups[1].Value;
                        // TMMIN manifest usually starts with 4 or 6, Order No starts with 202
                        if ((val.StartsWith("4") || val.StartsWith("6")) && !val.StartsWith("202"))
                        {
                            activeManifest = val;
                            continue;
                        }
                    }
                }

                // Look for part number in line
                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpper();

                // Extract TTL QTY — TMMIN table: NO | PART NO | PART NAME | UNIQ NO | BOX TYPE | PCS/KBN | NO of KBN | TTL QTY
                // TTL QTY is the LAST numeric value on the line (after the part number)
                var afterPart = line.Substring(partMatch.Index + partMatch.Length);
                var allNumbers = Regex.Matches(afterPart, @"(?<!\d)(\d{1,3}(?:\.\d{3})*|\d{1,6})(?!\d)")
                    .Cast<Match>()
                    .Select(m => {
                        int.TryParse(m.Groups[1].Value.Replace(".", ""), out int n);
                        return n;
                    })
                    .Where(n => n > 0)
                    .ToList();

                if (!allNumbers.Any()) continue;

                // TTL QTY = last number (rightmost column)
                int qty = allNumbers.Last();
                if (qty <= 0) continue;

                // Part name = text between row-number and part-number
                string partName = ExtractPartName(line, partMatch);

                // Allow all rows — will be summed in ProcessFileAsync
                items.Add(new ExtractedItem
                {
                    Manifesting = activeManifest,
                    PartNo = partNo,
                    PartName = partName,
                    Qty = qty
                });
            }

            _logger.LogInformation("SmartImport TMMIN: {Count} items extracted with manifests: [{Manifests}]",
                items.Count,
                string.Join(", ", items.Select(i => i.Manifesting).Distinct()));

            return items;
        }

        /// <summary>
        /// HINO-specific extractor — Delivery Note PT. Hino Motors Manufacturing Indonesia
        /// Format tabel: NO | Supply Area | Address Part | Part No. | Uniq No. | Part Name | Total Pack | Pcs/Pack | Total Qty | Remarks
        /// Manifest  : field "NO" di header dokumen (e.g. "NO : 2103902535")
        /// Part No   : format NNNNN-XNNNN-NN atau NNNNN-NNNNN-AN (kolom ke-4)
        /// Total Qty : kolom terakhir numerik di baris item (bukan Pcs/Pack)
        /// </summary>
        private List<ExtractedItem> ExtractItemsHino(string allText, List<string> lines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();

            // HINO Part No: 5digit-5alphanum-2alphanum
            var partPattern = new Regex(
                @"(\d{5}-[A-Z0-9]{4,6}-[A-Z0-9]{2})",
                RegexOptions.IgnoreCase);

            // Manifest dari header: "NO   : 2103902535"
            // EXCLUDE: PROCESS NO, CONTROL NO
            var noHeaderRegex = new Regex(@"(?<!PROCESS|CONTROL)\s+NO\.?\s*:\s*(\d{7,12})\b", RegexOptions.IgnoreCase);

            // â”€â”€ Normalisasi teks PDF â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // PdfPig kadang menghasilkan teks tanpa spasi antar kata, atau dengan spasi ekstra.
            // Normalisasi: collapse multiple spaces menjadi satu spasi
            var normalizedText = Regex.Replace(allText, @" {2,}", " ");
            
            // Log 500 char pertama untuk debugging
            _logger.LogInformation("SmartImport HINO raw text (500 chars): '{Text}'",
                normalizedText.Length > 500 ? normalizedText.Substring(0, 500) : normalizedText);

            // DEL. DATE — variasi format dari PDF:
            // "DEL. DATE : 18/03/2026"  "DEL.DATE: 18/03/2026"  "DEL. DATE :18/03/2026"
            // "Del. Date : 18/3/2026"   "DEL DATE 18/03/2026"
            var delDateRegex = new Regex(
                @"DEL\.?\s*DATE\s*[:\s]*(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{2,4})",
                RegexOptions.IgnoreCase);

            // Fallback 1: kata "DATE" (atau "DATE:") diikuti tanggal dd/MM/yyyy
            // EXCLUDE: OP. DATE (Order/Output Date - biasanya hari ini, bukan hari delivery)
            var anyDateRegex = new Regex(
                @"(?<!OP\.\s*)DATE[:\s]+(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{4})",
                RegexOptions.IgnoreCase);

            // Fallback 2: pola tanggal dd/MM/yyyy di dalam baris/segmen yang mengandung "DEL"
            var delLineRegex = new Regex(
                @"\bDEL[^\n\r]{0,30}?(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{4})",
                RegexOptions.IgnoreCase);

            // Fallback 3: cari semua tanggal dd/MM/yyyy 4-digit di seluruh teks
            // Pilih yang paling relevan (bukan OP.DATE, bukan tanggal di barcode dsb)
            var genericDateRegex = new Regex(@"(\d{1,2}[\/\-]\d{1,2}[\/\-]\d{4})", RegexOptions.IgnoreCase);

            // Cari manifest dari full text terlebih dahulu
            string activeManifest = headerManifest ?? "";
            if (string.IsNullOrWhiteSpace(activeManifest))
            {
                var noMatch = noHeaderRegex.Match(normalizedText);
                if (noMatch.Success) activeManifest = noMatch.Groups[1].Value.Trim();
            }

            // Cari DEL. DATE dari full text — coba 4 cara secara berurutan
            DateTime? activeScheduledDate = null;
            Match delDateMatch = delDateRegex.Match(normalizedText);
            if (!delDateMatch.Success) delDateMatch = anyDateRegex.Match(normalizedText);
            if (!delDateMatch.Success) delDateMatch = delLineRegex.Match(normalizedText);

            if (delDateMatch.Success)
            {
                var dateStr = delDateMatch.Groups[1].Value.Trim();
                if (DateTime.TryParseExact(dateStr,
                    new[] { "dd/MM/yyyy", "d/M/yyyy", "d/MM/yyyy", "dd/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    activeScheduledDate = parsedDate;
                    _logger.LogInformation("SmartImport HINO: DEL. DATE terdeteksi = {Date} (match: '{Raw}')",
                        parsedDate.ToString("dd/MM/yyyy"), delDateMatch.Value);
                }
                else
                {
                    _logger.LogWarning("SmartImport HINO: DEL. DATE match '{Raw}' tapi gagal parse", delDateMatch.Value);
                }
            }
            
            // Fallback 3: ambil semua tanggal dd/MM/yyyy, pilih yang bukan OP.DATE (biasanya tanggal hari yg berbeda)
            if (activeScheduledDate == null)
            {
                var allDates = genericDateRegex.Matches(normalizedText)
                    .Cast<Match>()
                    .Select(m => {
                        DateTime.TryParseExact(m.Groups[1].Value,
                            new[] { "dd/MM/yyyy", "d/M/yyyy", "d/MM/yyyy", "dd/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime d);
                        return (Match: m, Date: d);
                    })
                    .Where(x => x.Date != default)
                    .ToList();

                _logger.LogInformation("SmartImport HINO fallback: semua tanggal ditemukan = {Dates}",
                    string.Join(", ", allDates.Select(x => $"'{x.Match.Value}'")));

                if (allDates.Any())
                {
                    // Pilih tanggal yang ada dalam konteks "DEL" terdekat (cari index dalam teks)
                    var delIdx = normalizedText.IndexOf("DEL", StringComparison.OrdinalIgnoreCase);
                    if (delIdx >= 0)
                    {
                        // Cari tanggal paling dekat setelah kata DEL
                        var closest = allDates
                            .Where(x => x.Match.Index > delIdx)
                            .OrderBy(x => x.Match.Index - delIdx)
                            .FirstOrDefault();
                        if (closest.Date != default)
                        {
                            activeScheduledDate = closest.Date;
                            _logger.LogInformation("SmartImport HINO: Fallback3 DEL+tanggal terdekat = {Date} ('{Raw}')",
                                closest.Date.ToString("dd/MM/yyyy"), closest.Match.Value);
                        }
                    }
                    else
                    {
                        // Tidak ada DEL sama sekali — ambil tanggal pertama
                        activeScheduledDate = allDates.First().Date;
                        _logger.LogInformation("SmartImport HINO: Fallback3 tanggal pertama = {Date}",
                            allDates.First().Date.ToString("dd/MM/yyyy"));
                    }
                }
            }

            if (activeScheduledDate == null)
            {
                _logger.LogWarning("SmartImport HINO: DEL. DATE tidak ditemukan. Text sample: '{S}'",
                    normalizedText.Length > 400 ? normalizedText.Substring(0, 400) : normalizedText);
            }

            // Skip baris header tabel HINO
            var headerKeywords = new[] { "Supply Area", "Address Part", "Uniq No", "Part Name", "Total Pack", "Pcs/Pack", "Total Qty" };

            foreach (var line in lines)
            {
                if (line.Trim().Length < 5) continue;

                // Update manifest jika ketemu pattern "NO : XXXXXXXXX"
                // EXCLUDE: PROCESS NO, CONTROL NO
                var noLineMatch = noHeaderRegex.Match(line);
                if (noLineMatch.Success && !line.Contains("Part", StringComparison.OrdinalIgnoreCase))
                {
                    activeManifest = noLineMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Update DEL. DATE jika ketemu di baris (multi-halaman)
                // HINDARI update jika baris mengandung "OP. DATE"
                if (!line.Contains("OP. DATE", StringComparison.OrdinalIgnoreCase))
                {
                    var delDateLineMatch = delDateRegex.Match(line);
                    if (!delDateLineMatch.Success) delDateLineMatch = anyDateRegex.Match(line);
                    if (!delDateLineMatch.Success) delDateLineMatch = delLineRegex.Match(line);
                    if (delDateLineMatch.Success)
                    {
                        var dateStr = delDateLineMatch.Groups[1].Value.Trim();
                        if (DateTime.TryParseExact(dateStr,
                            new[] { "dd/MM/yyyy", "d/M/yyyy", "d/MM/yyyy", "dd/M/yyyy", "dd-MM-yyyy", "d-M-yyyy" },
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime parsedDateLine))
                        {
                            activeScheduledDate = parsedDateLine;
                        }
                        if (!partPattern.IsMatch(line)) continue;
                    }
                }

                // Skip baris header tabel
                if (!partPattern.IsMatch(line) &&
                    headerKeywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip baris TOTAL, SUPP, NAME, dll (metadata bukan item)
                if (Regex.IsMatch(line, @"^\s*(SUPP|NAME|C\.\s*ISSUE|S\.\s*PLACE|DEL\.\s*DATE|TIME|PAGE|PROCESS|TOTAL|OP\.\s*DATE)",
                    RegexOptions.IgnoreCase) && !partPattern.IsMatch(line))
                    continue;

                // Cari Part No di baris
                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpper();
                if (partNo.Length < 8) continue;

                // ---------------------------------------------------------------
                // TOTAL QTY = angka TERAKHIR di line (setelah Part No)
                // Kolom tabel: ... | Total Pack | Pcs/Pack | Total Qty | Remarks
                // Remarks biasanya kosong â†’ angka terakhir = Total Qty
                // ---------------------------------------------------------------
                var afterPart = line.Substring(partMatch.Index + partMatch.Length);

                // Kumpulkan semua angka valid setelah Part No
                var allNums = Regex.Matches(afterPart, @"(?<!\d)(\d{1,3}(?:,\d{3})*|\d{1,6})(?!\d)")
                    .Cast<Match>()
                    .Select(m => {
                        int.TryParse(m.Groups[1].Value.Replace(",", ""), out int n);
                        return n;
                    })
                    .Where(n => n > 0)
                    .ToList();

                if (!allNums.Any()) continue;

                // Pcs/Pack = angka ke-2 dari belakang (Total Pack | Pcs/Pack | Total Qty)
                // Jika hanya ada 1 angka (edge case), gunakan angka tersebut
                int qty = allNums.Count >= 2 ? allNums[allNums.Count - 2] : allNums.Last();
                if (qty <= 0) continue;

                // Part Name: teks setelah Uniq No (VINxxx) sampai angka
                string partName = ExtractPartNameHino(line, partMatch);

                items.Add(new ExtractedItem
                {
                    Manifesting = activeManifest,
                    PartNo = partNo,
                    PartName = partName,
                    Qty = qty,
                    ScheduledDate = activeScheduledDate  // DEL. DATE dari header halaman ini
                });
            }

            _logger.LogInformation("SmartImport HINO: {Count} items extracted, manifest='{Manifest}', DEL.DATE={Date}",
                items.Count, activeManifest, activeScheduledDate?.ToString("dd/MM/yyyy") ?? "(none)");

            return items;
        }

        /// <summary>
        /// Ekstrak Part Name dari baris HINO.
        /// Format: ... | Part No | Uniq No (VINxxx) | Part Name | Total Pack | Pcs/Pack | Total Qty
        /// Part Name ada SETELAH Uniq No (pattern VINxxx).
        /// </summary>
        private string ExtractPartNameHino(string line, Match partMatch)
        {
            try
            {
                var afterPart = line.Substring(partMatch.Index + partMatch.Length).Trim();
                // Uniq No format: VIN + digits (e.g. VIN010, VIN001)
                var vinMatch = Regex.Match(afterPart, @"VIN\d{3,5}", RegexOptions.IgnoreCase);
                if (vinMatch.Success)
                {
                    var afterVin = afterPart.Substring(vinMatch.Index + vinMatch.Length).Trim();
                    // Ambil teks sampai ketemu angka standalone
                    var nameMatch = Regex.Match(afterVin, @"^([A-Z][A-Z0-9\s,\.\-/#]{2,50}?)(?=\s+\d|$)", RegexOptions.IgnoreCase);
                    if (nameMatch.Success) return nameMatch.Groups[1].Value.Trim();
                    // Fallback: ambil semua non-digit di awal
                    var words = afterVin.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var nameWords = words.TakeWhile(w => !Regex.IsMatch(w, @"^\d+$")).ToList();
                    if (nameWords.Any()) return string.Join(" ", nameWords).Trim();
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// AHM PDF per-page extractor.
        /// Setiap halaman fisik berisi 2 kolom identik (asli + copy). 
        /// Header tiap halaman: KEPADA PT ASTRA HONDA MOTOR, Plant [name] ([code]), GATE, No DN, DN Date.
        ///
        /// Plant codes yang dikenali (angka dalam kurung di baris Plant):
        ///   1300 / 1350  → CIKARANG  (Plant III / III.A Cikarang)
        ///   1700 / 1800  → KRW       (Plant Karawang / KRW EXT 1)
        ///   Keyword "SUNTER" / "STR"   → SUNTER
        ///
        /// Location tag disimpan di DetectedDockCode untuk dipakai MatchAhmDocksByPlant.
        /// </summary>
        private List<ExtractedItem> ExtractItemsAhm(string pageText, List<string> pageLines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();

            // 1. Extract No DN (manifest) dari halaman ini
            // Format: "No DN : AHMVIN02579" atau "No DN : AHM/VIN/02579"
            string activeManifest = headerManifest ?? "";
            var dnMatch = Regex.Match(pageText, @"No\s*DN\s*:\s*([A-Z0-9/]{5,25})", RegexOptions.IgnoreCase);
            if (dnMatch.Success) activeManifest = dnMatch.Groups[1].Value.Trim();

            // 2. Extract DN Date / Depart Date (scheduled date) dari halaman ini
            // Format: "DN Date : 08-MAY-2026" atau "Depart Date : 08-MAY-2026"
            DateTime? pageDate = null;
            var dnDateMatch = Regex.Match(pageText,
                @"(?:DN\s*Date|Depart\s*Date)\s*:\s*(\d{1,2}[-/\s][A-Za-z]{3,9}[-/\s]\d{2,4}|\d{1,2}[-/]\d{1,2}[-/]\d{2,4})",
                RegexOptions.IgnoreCase);
            if (dnDateMatch.Success)
                pageDate = TryParseSmartDate(dnDateMatch.Groups[1].Value.Trim());

            // 3. Extract Plant location tag untuk dock matching
            // Prioritas: kode numerik 4-digit dalam tanda kurung (1300/1350/1700/1800)
            string? locationTag = null;

            // 3a. Cari plant code dalam kurung di baris yang mengandung "Plant"
            var plantCodeMatch = Regex.Match(pageText, @"\bPlant\b[^\n]{0,80}\((\d{4})\)", RegexOptions.IgnoreCase);
            if (!plantCodeMatch.Success)
                // Fallback: cari (1300)/(1350)/(1700)/(1800) di mana saja
                plantCodeMatch = Regex.Match(pageText, @"\((1300|1350|1700|1800)\)");

            if (plantCodeMatch.Success)
            {
                var code = plantCodeMatch.Groups[1].Value;
                switch (code)
                {
                    case "1300": case "1350": locationTag = "CIKARANG"; break;
                    case "1700": case "1800": locationTag = "KRW";      break;
                }
            }

            // 3b. Fallback: keyword lokasi di baris "Plant ..."
            if (locationTag == null)
            {
                var plantLineMatch = Regex.Match(pageText, @"\bPlant\b[^\n]{0,100}", RegexOptions.IgnoreCase);
                if (plantLineMatch.Success)
                {
                    var pt = plantLineMatch.Value.ToUpper();
                    if (pt.Contains("CIKARANG"))                                       locationTag = "CIKARANG";
                    else if (pt.Contains("KARAWANG") || Regex.IsMatch(pt, @"\bKRW\b")) locationTag = "KRW";
                    else if (pt.Contains("SUNTER") || Regex.IsMatch(pt, @"\bSTR\b"))   locationTag = "SUNTER";
                }
            }

            // 3c. Last-resort: keyword di seluruh teks halaman
            if (locationTag == null)
            {
                var pu = pageText.ToUpper();
                if (pu.Contains("CIKARANG"))                                            locationTag = "CIKARANG";
                else if (pu.Contains("KARAWANG") || Regex.IsMatch(pu, @"\bKRW\b"))     locationTag = "KRW";
                else if (pu.Contains("SUNTER"))                                         locationTag = "SUNTER";
            }

            // 4. Extract Part items — AHM part no: 5digit-3char-4char-2char (e.g. 11103-K0J-N000-H1)
            var partPattern = new Regex(
                @"(\d{5}\s*-\s*[A-Z0-9]{3}\s*-\s*[A-Z0-9]{4}\s*-\s*[A-Z0-9]{2})",
                RegexOptions.IgnoreCase);

            // Dedup dalam halaman: AHM PDF punya 2 kolom identik (asli + copy) → simpan 1 per PartNo
            var seenOnPage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string pageActiveManifest = activeManifest;

            foreach (var line in pageLines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Length < 8) continue;

                // Update manifest jika baris mengandung "No DN"
                var lineManifest = Regex.Match(line, @"No\s*DN\s*:\s*([A-Z0-9/]{5,25})", RegexOptions.IgnoreCase);
                if (lineManifest.Success) { pageActiveManifest = lineManifest.Groups[1].Value.Trim(); continue; }

                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value.Replace(" ", "").Trim().ToUpper();
                if (partNo.Length < 8) continue;

                int qty = ExtractQuantity(line, partNo);
                if (qty <= 0) continue;

                // Skip duplicate (2nd copy identik di halaman yang sama)
                if (seenOnPage.Contains(partNo)) continue;
                seenOnPage.Add(partNo);

                items.Add(new ExtractedItem
                {
                    Manifesting      = pageActiveManifest,
                    PartNo           = partNo,
                    PartName         = ExtractPartName(line, partMatch),
                    Qty              = qty,
                    ScheduledDate    = pageDate,
                    DetectedDockCode = locationTag   // "CIKARANG", "KRW", or "SUNTER"
                });
            }

            _logger.LogInformation(
                "SmartImport AHM page: manifest='{Mfst}', date='{Date}', plant='{Plant}', {Count} item(s)",
                pageActiveManifest,
                pageDate?.ToString("dd/MM/yyyy") ?? "(none)",
                locationTag ?? "(unknown)",
                items.Count);

            return items;
        }

        /// <summary>
        /// ADM-specific extractor — Delivery Note PT. Astra Daihatsu Motor
        /// Format tabel: No | Material No | Material Name | Total Kanban | Packing Type | Total QTY(PCS)
        /// DN NO format : DN27A02602130173
        /// Part No      : 12261-0Y041-00-87  (5digit-5alphanum-2digit-2digit)
        /// QTY          : kolom TOTAL QTY(PCS) = angka paling kanan di baris item
        /// </summary>
        private List<ExtractedItem> ExtractItemsAdm(string allText, List<string> lines, string? headerManifest)
        {
            // ADM Material No: 4-5 alphanum - 5 alphanum - 2digit [-2digit]
            // Contoh dari PDF: 12201-0Y041-00-87, 72213-BZ260-00-87, 80044-A8068-00-97, 9004A-48060-00-87
            var partPattern = new Regex(
                @"([A-Z0-9]{4,5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2}(?:\s*-\s*\d{2})?)",
                RegexOptions.IgnoreCase);

            // ADM DN NO format: "DN27A02602130173"
            // Format di PDF: "DN NO.    :    DN27A02602130173"
            // Pola 1: DN diikuti huruf+angka panjang
            var dnStrictRegex = new Regex(
                @"DN\s*NO\.?\s*[:\s]+(DN[A-Z0-9]{10,22})",
                RegexOptions.IgnoreCase);
            // Pola 2: Fallback - value setelah DN NO tidak dimulai dengan "DN"
            var dnFallbackRegex = new Regex(
                @"DN\s*NO\.?\s*[:\s]+([A-Z0-9]{10,25})",
                RegexOptions.IgnoreCase);

            // Cari DN NO dari full text halaman ini terlebih dahulu
            string activeManifest = headerManifest ?? "";
            if (string.IsNullOrWhiteSpace(activeManifest))
            {
                var dnMatch = dnStrictRegex.Match(allText);
                if (!dnMatch.Success) dnMatch = dnFallbackRegex.Match(allText);
                if (dnMatch.Success) activeManifest = dnMatch.Groups[1].Value.Trim().ToUpper();
            }

            // Untuk item dari halaman saat ini — dikelola sebagai dictionary PartNo â†’ item
            // sehingga Part No yang sama di halaman yang sama (dan manifest yang sama) dijumlah Qty-nya
            var itemMap = new Dictionary<string, ExtractedItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines)
            {
                // Skip baris yang sangat pendek atau kosong
                if (line.Trim().Length < 5) continue;

                // Update DN NO jika ada baris yang mengandung "DN NO"
                var dnLineMatch = dnStrictRegex.Match(line);
                if (!dnLineMatch.Success) dnLineMatch = dnFallbackRegex.Match(line);
                if (dnLineMatch.Success)
                {
                    activeManifest = dnLineMatch.Groups[1].Value.Trim().ToUpper();
                    continue;
                }

                // Skip baris header tabel HANYA jika TIDAK ada Part No di baris
                // Alasan: OCR kadang menggabungkan baris item terakhir dengan baris TOTAL di bawahnya
                // sehingga satu baris mengandung Part No DAN kata "TOTAL" sekaligus
                if (!partPattern.IsMatch(line) &&
                    Regex.IsMatch(line, @"\b(MATERIAL\s*NO|MATERIAL\s*NAME|TOTAL\s*KBN|PACKING\s*TYPE|TOTAL\s*QTY|QTY\(PCS\))\b", RegexOptions.IgnoreCase))
                    continue;

                // Cari Material No di baris
                var partMatch = partPattern.Match(line);
                if (!partMatch.Success) continue;

                var partNo = partMatch.Groups[1].Value
                    .Replace(" ", "").Trim().ToUpper();

                if (partNo.Length < 8) continue; // too short to be a valid part no

                // -----------------------------------------------------------------
                // TOTAL QTY(PCS) = kolom paling kanan
                // Format kolom ADM: No | Material No | Material Name | Total Kanban | Packing Type | Total QTY(PCS)
                // Semua angka di sisi kanan Part No, ambil yang terakhir (rightmost)
                // -----------------------------------------------------------------
                var afterPart = line.Substring(partMatch.Index + partMatch.Length);

                // Kumpulkan semua angka setelah Part No
                var allNums = Regex.Matches(afterPart, @"(?<!\d)(\d{1,3}(?:,\d{3})*|\d{1,7})(?!\d)")
                    .Cast<Match>()
                    .Select(m => {
                        int.TryParse(m.Groups[1].Value.Replace(",", ""), out int n);
                        return n;
                    })
                    .Where(n => n > 0)
                    .ToList();

                if (!allNums.Any()) continue;

                // TOTAL QTY(PCS) adalah angka TERAKHIR di baris (kolom paling kanan)
                int qty = allNums.Last();
                if (qty <= 0) continue;

                // Ambil nama material: teks antara nomor urut (di depan) dan Part No
                string partName = ExtractPartNameAdm(line, partMatch);

                // ADM Rule: Part No yang sama pada manifest yang sama â†’ QTY dijumlahkan
                string mapKey = $"{activeManifest}|{partNo}";
                if (itemMap.TryGetValue(mapKey, out var existing))
                {
                    existing.Qty += qty;
                }
                else
                {
                    itemMap[mapKey] = new ExtractedItem
                    {
                        Manifesting = activeManifest,
                        PartNo = partNo,
                        PartName = partName,
                        Qty = qty
                    };
                }
            }

            var items = itemMap.Values.ToList();

            _logger.LogInformation("SmartImport ADM: {Count} items extracted, manifest='{Manifest}'",
                items.Count, activeManifest);

            return items;
        }

        /// <summary>
        /// Extract Material Name dari baris ADM.
        /// Di ADM, nama material ada setelah Material No (kolom ke-3 tabel).
        /// Nama bisa mengandung angka (mis: "HOSE VENTILATION. NO.1").
        /// </summary>
        private string ExtractPartNameAdm(string line, Match partMatch)
        {
            try
            {
                // Teks setelah Part No
                var afterPart = line.Substring(partMatch.Index + partMatch.Length).Trim();

                // Split menjadi kata-kata, ambil sampai ketemu angka berdiri sendiri atau packing code
                var words = afterPart.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var nameWords = new List<string>();

                foreach (var word in words)
                {
                    // Stop jika word adalah angka murni (Total Kanban: "60", "180", "2,160")
                    if (Regex.IsMatch(word, @"^\d[\d,.]*$")) break;

                    // Stop jika word = Packing Type code (OC-13D, OC-130, TP-331, Tv-4, Tn-4)
                    if (Regex.IsMatch(word, @"^[A-Z]{2}-\d", RegexOptions.IgnoreCase)) break;
                    if (Regex.IsMatch(word, @"^[A-Z][a-z]-\d", RegexOptions.IgnoreCase)) break;

                    nameWords.Add(word);
                }

                var result = string.Join(" ", nameWords).Trim();

                // Hilangkan noise OCR di awal/akhir
                result = Regex.Replace(result, @"^[|Il1\[\]\s,]+", "").Trim();
                result = result.TrimEnd(',', '.', ' ');

                if (result.Length > 3) return result;
            }
            catch { }
            return "";
        }



        /// <summary>
        /// Try to detect a manifest/DN number from a single line (for non-TMMIN customers).
        /// Returns null if this line doesn't look like a manifest line.
        /// </summary>
        private string? TryExtractManifestFromLine(string line, string? customer)
        {
            // NEW: Check dynamic keywords from templates
            var templates = GetDynamicTemplates();
            var matchedTemplate = templates.FirstOrDefault(t => t.CustomerName == customer || (customer == null && line.Contains(t.Name, StringComparison.OrdinalIgnoreCase)));
            
            if (matchedTemplate != null)
            {
                var mapping = matchedTemplate.Mappings.FirstOrDefault(m => m.TargetField == "Manifest");
                if (mapping != null)
                {
                    var keywords = mapping.SourceKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim());
                    foreach (var kw in keywords)
                    {
                        if (line.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        {
                            // Try extract value after keyword
                            var pattern = Regex.Escape(kw) + @"\s*[:\s]*([A-Z0-9]{5,25})";
                            var m2 = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
                            if (m2.Success) return m2.Groups[1].Value.Trim();
                        }
                    }
                }
            }

            // Only try original regex if the line has a manifest keyword
            if (!Regex.IsMatch(line, @"MANIFEST|DN\s*NO|DN\s*NUMBER|ORDER\s*NO|NOMOR\s*DN|No\.?\s*SJ", RegexOptions.IgnoreCase))
                return null;

            var m = Regex.Match(line, @"(?:MANIFEST\s*NO\.?|DN\s*(?:NO|NUMBER)\.?)\s*[:\s]*([A-Z0-9]{5,20})", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            return null;
        }

        private List<string> GetPartNumberPatterns(string? customer)
        {
            var patterns = new List<string>();

            switch (customer)
            {
                case "AHM":
                    // AHM: 11103-K0J-N000-H1 (5digit-3char-4char-2char, may have spaces)
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{3}\s*-\s*[A-Z0-9]{4}\s*-\s*[A-Z0-9]{2})");
                    break;
                case "ADM":
                    // ADM: 12261-0Y041-00-87 or 12345-BZ010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2}(?:\s*-\s*\d{2})?)");
                    break;
                case "TMMIN":
                    // TMMIN: 44750-VT010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2})");
                    break;
                case "HINO":
                    // HINO: 48344-0W030-00, 12361-0W020-A0, 44773-F1020-00
                    // Segment ketiga bisa angka atau huruf+angka (berbeda dengan TMMIN yang selalu 2 digit)
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{4,6}\s*-\s*[A-Z0-9]{2})");
                    break;
                case "SANOH":
                    // Sanoh: GI272-0J020 or similar
                    patterns.Add(@"([A-Z]{1,2}\d{3,4}\s*-\s*[A-Z0-9]{4,6})");
                    break;
                case "AWI":
                    // AWI: 33519-BZ010-00
                    patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{5}\s*-\s*\d{2})");
                    break;
            }

            // Generic patterns (fallback for all)
            patterns.Add(@"(\d{5}\s*-\s*[A-Z0-9]{3,5}\s*-\s*[A-Z0-9]{2,5}(?:\s*-\s*[A-Z0-9]{2})?)"); // 5digit-XXX-XXXX style
            patterns.Add(@"([A-Z]{1,3}\d{3,5}\s*-\s*[A-Z0-9]{3,6})"); // Letter-prefix parts

            return patterns;
        }

        private int ExtractQuantity(string line, string partNo)
        {
            // Remove the part number from line to avoid confusion
            var lineWithoutPart = line.Replace(partNo, " ");

            // Find all standalone numbers (including dot/comma-thousands like 1.000, 2,700)
            var numbers = Regex.Matches(lineWithoutPart, @"(?<!\S)(\d{1,3}(?:[.,]\d{3})*|\d{1,7})(?:[\s,;|]|$)")
                .Cast<Match>()
                .Select(m => {
                    // Handle Indonesian thousand separator: 1.000 â†’ 1000, 10.500 â†’ 10500
                    string raw = m.Groups[1].Value.Replace(".", "").Replace(",", "");
                    int.TryParse(raw, out int n);
                    return n;
                })
                .Where(n => n > 0)
                .ToList();

            if (!numbers.Any()) return 0;

            // Strategy: Qty is typically the last significant number or the largest number
            // Filter out small sequence numbers (1, 2, 3...)
            var candidates = numbers.Where(n => n >= 10).ToList();
            if (candidates.Any())
            {
                // Return the last candidate (usually Qty is at the end of the row)
                return candidates.Last();
            }

            // If all numbers are small, return the last one
            return numbers.Last();
        }

        private string ExtractPartName(string line, Match partMatch)
        {
            try
            {
                // Try text after part number (TMMIN Style)
                var afterPart = line.Substring(partMatch.Index + partMatch.Length).Trim();
                
                // Clean up OCR noise at the start of afterPart (e.g. |, I, 1, [, ])
                afterPart = Regex.Replace(afterPart, @"^[|Il1\[\]\s,]+", "").Trim();
                
                // Regex: Match alphanumeric and special chars common in part names
                var nameMatch = Regex.Match(afterPart, @"^([A-Z0-9][A-Z0-9\s,\.\-/#]{2,60})", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                    return nameMatch.Groups[1].Value.Trim();
                
                // Try text before part number (AHM/ADM Style)
                var beforePart = line.Substring(0, partMatch.Index).Trim();
                // Remove leading row number
                beforePart = Regex.Replace(beforePart, @"^\d+[\.\)\s]+", "").Trim();
                // Clean up OCR noise
                beforePart = Regex.Replace(beforePart, @"^[|Il1\[\]\s,]+", "").Trim();

                if (!string.IsNullOrEmpty(beforePart) && beforePart.Length > 3)
                    return beforePart;
            }
            catch { }
            return "";
        }

        #endregion

        #region === Item Extraction (Excel) ===

        private List<ExtractedItem> ExtractItemsFromExcel(IXLWorksheet ws, int lastRow, int lastCol, string? customer, string? manifest, SmartImportResult result)
        {
            var items = new List<ExtractedItem>();

            // Step 1: Find header row
            int headerRow = 0;
            int colManifest = 0, colPartNo = 0, colPartName = 0, colQty = 0;
            int colScheduledDate = 0; // Kolom tanggal jadwal (ADM: "Order Date" = kol N; TMMIN: datetime kol Y)
            int colRoute = 0, colCycle = 0, colDock = 0;

            // Override khusus untuk TMMIN Excel (karena file tidak ada header, kolomnya di-hardcode)
            if (customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA")))
            {
                colManifest = 1; // Kolom A
                colPartNo = 11;  // Kolom K
                colQty = 17;     // Kolom Q
                colScheduledDate = 25; // Kolom Y
                colDock = 7;     // Kolom G
                colRoute = 30;   // Kolom AD
                colCycle = 0;
                
                // Cari data start row (abaikan baris jika baris pertama berisi data metadata pipe-separated)
                int dataStartRow = 1;
                for (int r = 1; r <= Math.Min(lastRow, 5); r++)
                {
                    var firstCell = "";
                    try { firstCell = ws.Cell(r, 1).Value.ToString().Trim(); } catch { }
                    if (firstCell.Contains("|"))
                    {
                        dataStartRow = r + 1;
                        continue;
                    }
                    break;
                }
                headerRow = dataStartRow - 1; // loop r = headerRow + 1 akan dimulai dari dataStartRow
            }

            // NEW: Prioritize dynamic templates from ScheduleTemplates table
            var dynamicTemplate = _db.ScheduleTemplates
                .Include(t => t.Mappings)
                .FirstOrDefault(t => t.IsActive && (t.CustomerName == customer || t.Name == customer));

            var mappings = _db.SmartImportHeaderMaps
                .Where(m => m.CustomerName == customer || m.CustomerName == "GENERIC")
                .ToList();

            // Prepare keyword sets for faster lookup
            var manifestSets = mappings.Where(m => m.TargetField == "Manifest").SelectMany(m => m.HeaderKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Select(k => k.ToUpper()).ToList();
            var partNoSets = mappings.Where(m => m.TargetField == "PartNo").SelectMany(m => m.HeaderKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Select(k => k.ToUpper()).ToList();
            var partNameSets = mappings.Where(m => m.TargetField == "PartName").SelectMany(m => m.HeaderKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Select(k => k.ToUpper()).ToList();
            var qtySets = mappings.Where(m => m.TargetField == "Qty").SelectMany(m => m.HeaderKeywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Select(k => k.ToUpper()).ToList();

            _logger.LogInformation("SmartImport Excel extraction: Cust='{Cust}', LastRow={LR}, LastCol={LC}, MappingsCount={M}", 
                customer, lastRow, lastCol, mappings.Count);

            // Fallback keywords if DB is empty or missing fields
            if (!manifestSets.Any()) manifestSets.AddRange(new[] { "MANIFEST", "DN NO", "DN", "DELIVERY NO", "DELIVERY NOTE", "DLV. NOTE", "DLV NOTE", "DLV. NOTE NO", "ORDER NO" });
            if (!partNoSets.Any()) partNoSets.AddRange(new[] { "PART NO", "NOMOR PART", "MATERIAL NO", "PART NUMBER", "MATERIAL NUMBER", "KODE ITEM", "PART" });
            if (!qtySets.Any()) qtySets.AddRange(new[] { "QTY", "QUANTITY", "JUMLAH", "TOTAL QTY", "PCS", "ORDER (PCS)", "ORDER QTY", "QTY (PC)", "JUMLAH (PC)" });

            // Keyword untuk kolom tanggal jadwal
            var scheduledDateKeywords = new[] { 
                // AHM: "DN Date", "DN.Date", "DN DATE"
                "DN DATE", "DNDATE", "DN.DATE",
                "ORDER DATE", "ORDERDATE", "ORDER_DATE", 
                "CALC DATE", "CALC.DATE", "CALCDATE", "CALC. DATE",
                "DEL DATE", "DEL.DATE", "DELDATE", "DEL. DATE",
                "DELIVERY DATE", "DELIVERYDATE",
                "DLV DATE", "DLVDATE", "DLV.DATE", "DLV. DATE",
                "SCHEDULE DATE", "SCHEDULEDATE", "SCHED DATE",
                "PLAN DATE", "PLANDATE",
                "DEPART DATE", "DEPARTDATE"
            };

            // Helper to check if a cell matches any keyword in a set
            bool IsMatch(string val, List<string> set) => set.Any(k => 
                val == k || 
                (k.Length >= 3 && val.Contains(k)) || 
                (val.Length >= 3 && k.Contains(val)));

            bool IsScheduledDateHeader(string val)
            {
                // Normalize: hapus spasi, titik, underscore, strip
                var valNorm = val.Replace(" ", "").Replace(".", "").Replace("_", "").Replace("-", "");
                return scheduledDateKeywords.Any(k =>
                {
                    var kNorm = k.Replace(" ", "").Replace(".", "").Replace("_", "").Replace("-", "");
                    return string.Equals(val, k, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(valNorm, kNorm, StringComparison.OrdinalIgnoreCase)
                        || valNorm.Contains(kNorm, StringComparison.OrdinalIgnoreCase);
                });
            }

            // Robust scanning: dua pass — pass 1 cari headerRow, pass 2 scan penuh baris header untuk semua kolom
            // Pass 1: Cari headerRow (baris yang mengandung Part No + Qty)
            for (int r = 1; r <= Math.Min(lastRow, 100); r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 200); c++)
                {
                    string val;
                    try { val = ws.Cell(r, c).Value.ToString().Trim().ToUpper(); } catch { continue; }
                    if (string.IsNullOrEmpty(val)) continue;

                    if (IsMatch(val, manifestSets) || val == "DN")
                    {
                        if (colManifest == 0) colManifest = c;
                    }
                    if (IsMatch(val, partNoSets) || val == "ITEM")
                    {
                        if (colPartNo == 0) { colPartNo = c; headerRow = r; }
                    }
                    if (IsMatch(val, partNameSets) || val.Contains("DESKRIPSI") || val.Contains("DESCRIPTION"))
                    {
                        if (colPartName == 0) colPartName = c;
                    }
                    if (IsMatch(val, qtySets))
                    {
                        bool currentIsStrong = val.Contains("TOTAL");
                        bool currentIsActual = (val.Contains("QTY") || val.Contains("PCS")) && !val.Contains("ORDER");
                        
                        if (colQty == 0 || currentIsStrong || (currentIsActual && !ws.Cell(headerRow > 0 ? headerRow : r, colQty).Value.ToString().ToUpper().Contains("TOTAL")))
                            colQty = c;
                    }

                    // Dynamic Mapping matching
                    if (dynamicTemplate != null)
                    {
                        foreach (var m in dynamicTemplate.Mappings)
                        {
                            var keywords = m.SourceKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(kw => kw.Trim().ToUpper());
                            if (keywords.Any(kw => val.Contains(kw)))
                            {
                                switch (m.TargetField)
                                {
                                    case "Manifest": if (colManifest == 0) colManifest = c; break;
                                    case "PartNo": if (colPartNo == 0) { colPartNo = c; headerRow = r; } break;
                                    case "PartName": if (colPartName == 0) colPartName = c; break;
                                    case "Qty": if (colQty == 0) colQty = c; break;
                                    case "Date": if (colScheduledDate == 0) colScheduledDate = c; break;
                                    case "Route": if (colRoute == 0) colRoute = c; break;
                                    case "Cycle": if (colCycle == 0) colCycle = c; break;
                                    case "Dock": if (colDock == 0) colDock = c; break;
                                }
                            }
                        }
                    }
                }

                // Jika sudah menemukan PartNo + Qty di baris ini â†’ baris header ditemukan, stop
                if (headerRow > 0 && colPartNo > 0 && colQty > 0) break;
            }

            // Pass 2: Setelah headerRow diketahui, scan PENUH baris header untuk temukan kolom tanggal
            // (prioritaskan kolom "Dlv. Date" jika ada, lalu fallback ke "Order Date" dll.)
            if (headerRow > 0 && colScheduledDate == 0)
            {
                // Pass 2a: Cari kolom DLV. DATE (prioritas utama)
                for (int c = 1; c <= Math.Min(lastCol, 200); c++)
                {
                    string val;
                    try { val = ws.Cell(headerRow, c).Value.ToString().Trim().ToUpper(); } catch { continue; }
                    if (string.IsNullOrEmpty(val)) continue;

                    if (val.Contains("DLV. DATE") || val.Contains("DLV DATE") || val.Contains("DLV.DATE") || val.Contains("DLV_DATE"))
                    {
                        colScheduledDate = c;
                        _logger.LogInformation("SmartImport Excel {Cust}: Prioritas utama tanggal (DLV DATE) terdeteksi di kolom {Col} (header='{Header}')",
                            customer, c, val);
                        break;
                    }
                }

                // Pass 2b: Fallback ke keyword lain jika DLV DATE tidak ditemukan
                if (colScheduledDate == 0)
                {
                    for (int c = 1; c <= Math.Min(lastCol, 200); c++)
                    {
                        string val;
                        try { val = ws.Cell(headerRow, c).Value.ToString().Trim().ToUpper(); } catch { continue; }
                        if (string.IsNullOrEmpty(val)) continue;

                        if (IsScheduledDateHeader(val))
                        {
                            colScheduledDate = c;
                            _logger.LogInformation("SmartImport Excel {Cust}: Kolom tanggal jadwal terdeteksi di kolom {Col} (header='{Header}')",
                                customer, c, val);
                            break;
                        }
                    }
                }
            }

            // Log semua header yang ditemukan untuk debugging (extended: sampai kolom 60)
            if (headerRow > 0)
            {
                var headerDump = new System.Text.StringBuilder();
                for (int c = 1; c <= Math.Min(lastCol, 60); c++)
                {
                    try { var v = ws.Cell(headerRow, c).Value.ToString().Trim(); if (!string.IsNullOrEmpty(v)) headerDump.Append($"[{c}/{(char)('A'+c-1)}={v}] "); } catch { }
                }
                _logger.LogInformation("SmartImport Excel {Cust}: Header row {R} — {Headers}", customer, headerRow, headerDump);

                // Juga log baris data pertama (row headerRow+1) untuk TMMIN/ADM debugging
                var dataDump = new System.Text.StringBuilder();
                for (int c = 20; c <= Math.Min(lastCol, 35); c++)
                {
                    try {
                        var cell = ws.Cell(headerRow + 1, c);
                        var v = cell.Value.ToString().Trim();
                        if (!string.IsNullOrEmpty(v)) dataDump.Append($"[{c}/{(char)('A'+c-1)}={v} type={cell.DataType}] ");
                    } catch { }
                }
                _logger.LogInformation("SmartImport Excel {Cust}: Data row {R} cols T-AI — {Data}", customer, headerRow + 1, dataDump);
            }

            // FALLBACK khusus per-customer jika header scan tidak menemukan kolom tanggal:
            // ADM Excel: "Order Date" berada di kolom N (14)
            // TMMIN Excel: datetime di kolom Y (25) — "Calc. Date" atau kolom datetime
            if (colScheduledDate == 0 && headerRow > 0)
            {
                if (customer == "ADM")
                {
                    // Scan semua kolom di baris header: cari "ORDER DATE" atau yang mengandung "DATE"
                    for (int c = 1; c <= Math.Min(lastCol, 50); c++)
                    {
                        try
                        {
                            var hdr = ws.Cell(headerRow, c).Value.ToString().Trim().ToUpper();
                            // "Order Date" â†’ toUpper = "ORDER DATE"
                            if (hdr.Contains("ORDER") || (hdr.Contains("DATE") && !hdr.Contains("ENTRY") && !hdr.Contains("ISSUE")))
                            {
                                colScheduledDate = c;
                                _logger.LogInformation("SmartImport ADM fallback: kolom tanggal = {Col} (header='{Hdr}')", c, hdr);
                                break;
                            }
                        }
                        catch { }
                    }
                    // Hard fallback: kolom N (14) — verifikasi isi datanya berupa tanggal
                    if (colScheduledDate == 0 && lastCol >= 14)
                    {
                        try
                        {
                            var dataVal = ws.Cell(headerRow + 1, 14).Value.ToString().Trim();
                            var cell14 = ws.Cell(headerRow + 1, 14);
                            if (cell14.DataType == XLDataType.DateTime || DateTime.TryParse(dataVal, out _))
                            {
                                colScheduledDate = 14;
                                _logger.LogInformation("SmartImport ADM: Hard fallback kolom N=14 (nilai data='{V}')", dataVal);
                            }
                        }
                        catch { }
                    }
                }
                else if (customer == "TMMIN")
                {
                    // TMMIN: "Calc. Date" di kolom Y (25) — nilai berupa DateTime: "2026-03-17 15:30:00.000"
                    // Scan header row: cari kolom berisi "CALC" atau "DATE"
                    for (int c = 1; c <= Math.Min(lastCol, 60); c++)
                    {
                        try
                        {
                            var hdr = ws.Cell(headerRow, c).Value.ToString().Trim().ToUpper();
                            if (hdr.Contains("CALC") || hdr.Contains("PLAN DATE") || hdr.Contains("SCHEDULE"))
                            {
                                colScheduledDate = c;
                                _logger.LogInformation("SmartImport TMMIN fallback header: kolom tanggal = {Col} (header='{Hdr}')", c, hdr);
                                break;
                            }
                        }
                        catch { }
                    }
                    // Hard fallback: cek kolom Y=25 langsung
                    if (colScheduledDate == 0 && lastCol >= 25)
                    {
                        try
                        {
                             // Cek apakah kolom 25 (Y) berisi tanggal/jam (TMMIN standard)
                             colScheduledDate = 25;
                             _logger.LogInformation("SmartImport TMMIN: Forced use of Column Y=25 for date/time.");
                        }
                        catch { }
                    }
                    // Scan baris data: cari kolom bertipe DateTime jika masih belum ketemu
                    if (colScheduledDate == 0)
                    {
                        for (int c = 1; c <= Math.Min(lastCol, 60); c++)
                        {
                            try
                            {
                                var cell = ws.Cell(headerRow + 1, c);
                                if (cell.DataType == XLDataType.DateTime)
                                {
                                    colScheduledDate = c;
                                    _logger.LogInformation("SmartImport TMMIN: Hard fallback kolom {Col} terdeteksi sebagai DateTime", c);
                                    break;
                                }
                                var dv = cell.Value.ToString().Trim();
                                if (Regex.IsMatch(dv, @"^\d{4}-\d{2}-\d{2}"))
                                {
                                    colScheduledDate = c;
                                    _logger.LogInformation("SmartImport TMMIN: Hard fallback kolom {Col} via regex (val='{V}')", c, dv);
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    // Generic fallback untuk customer lain: scan header, cari kolom dengan kata "DATE" atau "TGL"
                    // Khusus AHM: cari "DN DATE", "DN.DATE", "DEPART DATE"
                    bool isAhm = customer == "AHM";
                    for (int c = 1; c <= Math.Min(lastCol, 50); c++)
                    {
                        try
                        {
                            var hdr = ws.Cell(headerRow, c).Value.ToString().Trim().ToUpper();
                            bool isDnDate = isAhm && (hdr.Contains("DN") && hdr.Contains("DATE"));
                            bool isDepartDate = isAhm && hdr.Contains("DEPART");
                            bool isGenericDate = (hdr.Contains("DATE") || hdr.Contains("TGL") || hdr.Contains("TANGGAL"))
                                && !hdr.Contains("ENTRY") && !hdr.Contains("ISSUE") && !hdr.Contains("CREATED");
                            if (isDnDate || isDepartDate || isGenericDate)
                            {
                                colScheduledDate = c;
                                _logger.LogInformation("SmartImport {Cust} generic fallback: kolom tanggal = {Col} (header='{Hdr}')", customer, c, hdr);
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }

            // For TMMIN special case: Force Column Q (17) as requested by user
            if (customer != null && customer.Contains("TMMIN") && lastCol >= 17)
            {
                colQty = 17;
                _logger.LogInformation("SmartImport {Cust}: Forced use of Column Q=17 for Qty by user request.", customer);
            }

            // Warning jika kolom tanggal (global) tidak ditemukan, KECUALI TMMIN yang mengambil tanggal per-baris dari kolom Y
            bool isTmmin = customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA"));
            if (colScheduledDate == 0 && !isTmmin)
            {
                _logger.LogWarning("SmartImport Excel {Cust}: Kolom tanggal jadwal TIDAK ditemukan — akan fallback ke DateTime.Today", customer);
                result.Warnings.Add($"Kolom tanggal jadwal tidak ditemukan di file. Jadwal akan dibuat dengan tanggal hari ini sebagai fallback.");
            }

            // If no structured header found, try pattern matching on each row
            bool isTmminCust = customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA"));
            if ((headerRow == 0 && !isTmminCust) || colPartNo == 0)
            {
                _logger.LogInformation("SmartImport Excel: No structured header, using pattern matching");
                return ExtractItemsFromExcelByPattern(ws, lastRow, lastCol, customer, manifest, result);
            }

            if (colQty > 0)
            {
                try {
                    var qtyHeader = ws.Cell(headerRow, colQty).Value.ToString();
                    result.Warnings.Add($"Sistem mendeteksi kolom Qty dari header: '{qtyHeader}' (Kolom {(char)('A' + colQty - 1)}).");
                } catch { }
            }

            _logger.LogInformation("SmartImport Excel: Header at row {Row}, PartNo=col{P}, Qty=col{Q}, ScheduledDate=col{D}",
                headerRow, colPartNo, colQty, colScheduledDate);

            // Tanggal jadwal header (dari baris pertama data, dipakai sebagai default jika baris tidak punya tanggal)
            DateTime? headerScheduledDate = null;

            // Step 2: Read data rows
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string partNo;
                try { partNo = ws.Cell(r, colPartNo).Value.ToString().Trim(); } catch { continue; }
                if (string.IsNullOrEmpty(partNo)) continue;

                // Must look like a part number (at least 3 alnum characters)
                if (!Regex.IsMatch(partNo, @"[A-Z0-9]{3,}", RegexOptions.IgnoreCase)) continue;

                // Skip instruction/header/summary rows
                if (partNo.StartsWith("Keterangan", StringComparison.OrdinalIgnoreCase) ||
                    partNo.StartsWith("Hapus", StringComparison.OrdinalIgnoreCase) ||
                    partNo.StartsWith("Customer", StringComparison.OrdinalIgnoreCase) ||
                    partNo.ToUpper().Contains("TOTAL") || 
                    partNo.ToUpper().Contains("SUBTOTAL"))
                    continue;

                // Robust check: skip row if any cell before PartNo or around Qty contains "TOTAL"
                bool isSummaryRow = false;
                for (int c = 1; c <= Math.Min(lastCol, 10); c++) {
                    try {
                        var cellVal = ws.Cell(r, c).Value.ToString().ToUpper();
                        if (cellVal.Contains("TOTAL") || cellVal.Contains("SUBTOTAL")) {
                            isSummaryRow = true;
                            break;
                        }
                    } catch { }
                }
                if (isSummaryRow) continue;

                string manifestVal = "";
                if (colManifest > 0)
                {
                    try { 
                        manifestVal = ws.Cell(r, colManifest).Value.ToString().Trim(); 
                        manifestVal = Regex.Replace(manifestVal, @"[\(\)\[\]\s]+$", "");
                    } catch { }
                }
                
                if (string.IsNullOrEmpty(manifestVal)) manifestVal = manifest ?? "";
                
                var partName = "";
                if (colPartName > 0)
                {
                    try { partName = ws.Cell(r, colPartName).Value.ToString().Trim(); } catch { }
                }

                int qty = 0;
                if (colQty > 0)
                {
                    try {
                        var qtyCell = ws.Cell(r, colQty);
                        if (qtyCell.DataType == XLDataType.Number) {
                            qty = (int)Math.Round(qtyCell.GetDouble());
                        } else {
                            var qtyStr = qtyCell.Value.ToString().Replace(",", "").Trim();
                            int.TryParse(Regex.Match(qtyStr, @"\d+").Value, out qty);
                        }
                    } catch { }
                }

                if (qty <= 0) continue;

                // Baca tanggal jadwal dari kolom tanggal (ADM: Order Date, TMMIN: Calc. Date)
                DateTime? rowScheduledDate = null;
                TimeSpan? rowTimeOfDay = null;
                string? rowMappedCycle = null;
                if (colScheduledDate > 0)
                {
                    try
                    {
                        var dateCell = ws.Cell(r, colScheduledDate);
                        if (!dateCell.IsEmpty())
                        {
                            // Coba baca sebagai DateTime langsung (Excel datetime cell)
                            if (dateCell.DataType == XLDataType.DateTime)
                            {
                                var fullDt = dateCell.GetDateTime();
                                rowScheduledDate = fullDt.Date;
                                // TMMIN: extract TimeOfDay untuk matching dock berdasarkan jam pickup
                                if (customer == "TMMIN" && fullDt.TimeOfDay.TotalMinutes > 0)
                                    rowTimeOfDay = fullDt.TimeOfDay;
                            }
                            else
                            {
                                // Coba parse dari string: "2026-03-17 15:30:00.000", "17-Mar-26", "17/03/2026"
                                var dateStr = dateCell.Value.ToString().Trim();
                                if (DateTime.TryParse(dateStr,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                                {
                                    rowScheduledDate = parsedDate.Date;
                                    if (customer == "TMMIN" && parsedDate.TimeOfDay.TotalMinutes > 0)
                                        rowTimeOfDay = parsedDate.TimeOfDay;
                                }
                                else
                                {
                                    // Format "17-Mar-26" (ADM style)
                                    if (DateTime.TryParseExact(dateStr,
                                        new[] { "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy", "dd-MMM-yyyy", "dd/MM/yyyy", "d/M/yyyy" },
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out DateTime parsedDate2))
                                    {
                                        rowScheduledDate = parsedDate2.Date;
                                    }
                                }
                            }

                            // Simpan tanggal pertama yang valid sebagai header default
                            if (rowScheduledDate.HasValue && !headerScheduledDate.HasValue)
                            {
                                headerScheduledDate = rowScheduledDate;
                                _logger.LogInformation("SmartImport Excel {Cust}: Tanggal pertama berhasil dibaca = {Date} (baris {Row}, kolom {Col})",
                                    customer, rowScheduledDate.Value.ToString("yyyy-MM-dd"), r, colScheduledDate);
                            }
                        }
                    }
                    catch { }
                }

                // TMMIN khusus: ambil tanggal dan jam dari kolom Y (25) per-baris.
                // Lakukan mapping cycle & jam pickup sesuai skenario:
                // - Kolom M (13) atau Kolom C (3) mendeteksi cycle (1 atau 2).
                // - Kolom Y (25) mengambil jam nya saja.
                // - Jika cycle = 2: 23:00 -> 19:00, 02:00 -> 09:00.
                // - Jika cycle = 1: 19:00 -> 19:00, 09:00 -> 09:00.
                // - Hasil akhir di-set ke Cycle 1 ("C1").
                if (customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA")))
                {
                    try {
                        // 1. Ambil tanggal dan jam dari kolom Y (25)
                        var cellY = ws.Cell(r, 25);
                        TimeSpan? rawTimeY = null;

                        if (cellY.DataType == XLDataType.DateTime)
                        {
                            var dtY = cellY.GetDateTime();
                            if (dtY.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtY.Date;
                            rawTimeY = dtY.TimeOfDay;
                        }
                        else if (cellY.DataType == XLDataType.Number)
                        {
                            double oaDateY = cellY.GetDouble();
                            if (oaDateY >= 1)
                            {
                                var dtY = DateTime.FromOADate(oaDateY);
                                if (dtY.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtY.Date;
                                rawTimeY = dtY.TimeOfDay;
                            }
                            else
                            {
                                rawTimeY = DateTime.FromOADate(oaDateY).TimeOfDay;
                            }
                        }
                        else if (cellY.DataType == XLDataType.TimeSpan)
                        {
                            rawTimeY = cellY.GetTimeSpan();
                        }
                        else
                        {
                            var valY = cellY.Value.ToString().Trim();
                            if (!string.IsNullOrEmpty(valY))
                            {
                                if (DateTime.TryParse(valY, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtY))
                                {
                                    if (dtY.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtY.Date;
                                    rawTimeY = dtY.TimeOfDay;
                                }
                                else 
                                {
                                    // Parse fallback
                                    if (valY.Length >= 10 && DateTime.TryParse(valY.Substring(0, 10), out DateTime dtShort))
                                        rowScheduledDate = dtShort.Date;
                                        
                                    var timeMatch = Regex.Match(valY, @"(\d{2}:\d{2})(?::\d{2})?");
                                    if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups[1].Value, out TimeSpan tsRegex))
                                    {
                                        rawTimeY = tsRegex;
                                    }
                                }
                            }
                        }

                        // 3. Mapping Cycle dan Hour
                        if (rawTimeY.HasValue)
                        {
                            // Deteksi cycle dari kolom M (13) atau kolom C (3)
                            string cycleVal = "";
                            try { cycleVal = ws.Cell(r, 13).Value.ToString().Trim(); } catch { }
                            if (string.IsNullOrEmpty(cycleVal))
                            {
                                try { cycleVal = ws.Cell(r, 3).Value.ToString().Trim(); } catch { }
                            }

                            string cycleClean = Regex.Match(cycleVal, @"\d").Value;

                            // HANYA ambil time-nya (termasuk menit) untuk matching cycle
                            rowTimeOfDay = rawTimeY;
                            rowMappedCycle = cycleClean;
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "TMMIN date/time parsing error on row {Row}", r);
                    }
                }

                // Jika baris masih tidak punya tanggal, gunakan tanggal header dari baris pertama
                if (!rowScheduledDate.HasValue && headerScheduledDate.HasValue)
                    rowScheduledDate = headerScheduledDate;

                // Baca Route/Cycle jika ada kolomnya
                string? rowRoute = null, rowCycle = null, rowDock = null;
                if (colRoute > 0) try { rowRoute = ws.Cell(r, colRoute).Value.ToString().Trim(); } catch { }
                if (colCycle > 0) try { rowCycle = ws.Cell(r, colCycle).Value.ToString().Trim(); } catch { }
                if (colDock > 0) try { rowDock = ws.Cell(r, colDock).Value.ToString().Trim(); } catch { }

                // Konversi khusus PBOD untuk TMMIN
                if (customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA")) && !string.IsNullOrEmpty(rowDock))
                {
                    var dockUpper = rowDock.Trim().ToUpper();
                    if (dockUpper == "7N" || dockUpper == "7L")
                    {
                        rowDock = "PBOD DOCK 7";
                    }
                    else if (dockUpper == "8N" || dockUpper == "8L" || dockUpper == "8N/7L" || dockUpper == "8N/8L")
                    {
                        rowDock = "PBOD DOCK 8";
                    }
                }

                items.Add(new ExtractedItem
                {
                    Manifesting = !string.IsNullOrEmpty(manifestVal) ? manifestVal : (manifest ?? ""),
                    PartNo = partNo.ToUpper(),
                    PartName = partName,
                    Qty = qty,
                    ScheduledDate = rowScheduledDate,
                    TimeOfDay = rowTimeOfDay,
                    DetectedRouteCode = rowRoute ?? result.DetectedRoute,
                    DetectedDockCode = rowCycle ?? rowDock ?? result.DetectedCycle, // Use detected cycle if no dock code
                    DetectedCycleCode = rowMappedCycle
                });
            }

            // Set DetectedScheduledDate dari item pertama yang punya tanggal
            if (result.DetectedScheduledDate == null && headerScheduledDate.HasValue)
            {
                result.DetectedScheduledDate = headerScheduledDate;
                _logger.LogInformation("SmartImport Excel {Cust}: DetectedScheduledDate = {Date}",
                    customer, headerScheduledDate.Value.ToString("yyyy-MM-dd"));
            }

            if (result.DetectedScheduledDate == null)
            {
                _logger.LogWarning("SmartImport Excel {Cust}: Tidak ada tanggal jadwal berhasil dibaca dari file", customer);
            }

            return items;
        }

        /// <summary>
        /// Smart Excel parser for files WITHOUT headers.
        /// Uses "Column Fingerprinting" — scans data rows to detect Manifest, Part No, and Qty
        /// columns by their data patterns (e.g., 10-digit numbers for manifest, 12-char alnum for part no).
        /// </summary>
        private List<ExtractedItem> ExtractItemsFromExcelByPattern(IXLWorksheet ws, int lastRow, int lastCol, string? customer, string? manifest, SmartImportResult result)
        {
            var items = new List<ExtractedItem>();

            // â”€â”€ Step 1: Skip metadata rows and find first real data row â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Row 1 may contain pipe-separated metadata like "5453|1|202603161200|..."
            int dataStartRow = 1;
            for (int r = 1; r <= Math.Min(lastRow, 5); r++)
            {
                var firstCell = "";
                try { firstCell = ws.Cell(r, 1).GetString().Trim(); } catch { }
                // If first cell contains pipes, it's a metadata row, skip it
                if (firstCell.Contains("|") || firstCell.Length < 5)
                {
                    dataStartRow = r + 1;
                    continue;
                }
                break;
            }

            _logger.LogInformation("SmartImport Excel (No Header): dataStartRow={Start}, lastRow={Last}, lastCol={Col}", dataStartRow, lastRow, lastCol);

            if (dataStartRow > lastRow) return items;

            // â”€â”€ Step 2: Column fingerprinting â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Scan sample rows to identify Manifest, PartNo, and Qty columns by data pattern
            int sampleCount = Math.Min(lastRow - dataStartRow + 1, 15); // scan up to 15 rows
            int colManifest = 0, colPartNo = 0, colQty = 0;

            var colManifestScores = new Dictionary<int, int>();
            var colPartNoScores   = new Dictionary<int, int>();
            var colPartNoAlphaScores = new Dictionary<int, int>();
            var colQtyScores      = new Dictionary<int, int>();
            
            // SMART SCORING for QTY: track values to find high-variance columns
            var colSums = new Dictionary<int, long>();
            var colDistinctVals = new Dictionary<int, HashSet<string>>();

            // Part number patterns: TMMIN raw (483410K15000), formatted (48341-0K150-00), HINO (48344-0W030-00)
            // Part number patterns: TMMIN raw (483410K15000), formatted (48341-0K150-00), HINO/AHM (11103-K0J-N000-H1), and letter-prefixed (SU003-01183)
            var partNoRegex = new Regex(@"^([A-Z0-9]{4,7}(?:\s*-\s*[A-Z0-9]{1,10}){1,4}|[A-Z0-9]{8,15})$", RegexOptions.IgnoreCase);
            // Manifest patterns: pure digits (10-13) OR Alphanumeric with slashes/dashes (min 5 chars)
            var manifestRegex = new Regex(@"^(?:\d{8,13}|[A-Z0-9][A-Z0-9\/-]{4,19})$", RegexOptions.IgnoreCase);
            // ISO datetime pattern: "2026-03-17 15:30:00.000" or "2026-03-17T..."
            var isoDateRegex = new Regex(@"^\d{4}-\d{2}-\d{2}[\s T]", RegexOptions.IgnoreCase);

            // â”€â”€ Kolom tanggal: deteksi dari baris data (untuk file tanpa header) â”€â”€â”€â”€â”€â”€
            int colScheduledDate = 0;
            var colDateScores = new Dictionary<int, int>();

            // TMMIN hard-coded fallback: kolom Y=25 adalah "Calc. Date"
            if (customer == "TMMIN" && lastCol >= 25)
            {
                try
                {
                    var sampleCell = ws.Cell(dataStartRow, 25);
                    var sampleVal = sampleCell.Value.ToString().Trim();
                    if (sampleCell.DataType == XLDataType.DateTime || isoDateRegex.IsMatch(sampleVal))
                    {
                        colScheduledDate = 25;
                        _logger.LogInformation("SmartImport TMMIN NoHeader: Kolom tanggal hard-coded Y=25 (val='{V}', type={T})", sampleVal, sampleCell.DataType);
                    }
                }
                catch { }
            }

            // TMMIN hard-coded fallback for Qty: kolom Q=17
            if (customer != null && customer.Contains("TMMIN") && lastCol >= 17)
            {
                colQty = 17;
                _logger.LogInformation("SmartImport {Cust} NoHeader: Forced use of Column Q=17 for Qty by user request.", customer);
            }

            for (int r = dataStartRow; r < dataStartRow + sampleCount; r++)
            {
                for (int c = 1; c <= Math.Min(lastCol, 35); c++)
                {
                    string val;
                    IXLCell? cell2 = null;
                    try { cell2 = ws.Cell(r, c); val = cell2.Value.ToString().Trim(); } catch { continue; }
                    if (string.IsNullOrEmpty(val)) continue;

                    if (!colDistinctVals.ContainsKey(c)) colDistinctVals[c] = new HashSet<string>();
                    colDistinctVals[c].Add(val);

                    // Score manifest: pure digit string of length 8-13 (high length numeric)
                    if (manifestRegex.IsMatch(val))
                        colManifestScores[c] = colManifestScores.GetValueOrDefault(c) + 1;

                    // Score part no
                    if (partNoRegex.IsMatch(val))
                    {
                        colPartNoScores[c] = colPartNoScores.GetValueOrDefault(c) + 1;
                        if (val.Any(char.IsLetter))
                            colPartNoAlphaScores[c] = colPartNoAlphaScores.GetValueOrDefault(c) + 1;
                    }

                    // Score qty candidate
                    int qv = ParseSmartQty(val);
                    if (qv > 0 && qv <= 100000)
                    {
                        if (val.Length < 10)
                        {
                            colQtyScores[c] = colQtyScores.GetValueOrDefault(c) + 1;
                            colSums[c] = colSums.GetValueOrDefault(c) + qv;
                        }
                    }

                    // Score kolom tanggal: DateTime type atau format ISO/umum
                    if (colScheduledDate == 0)
                    {
                        bool isDateCell = (cell2?.DataType == XLDataType.DateTime)
                            || isoDateRegex.IsMatch(val)
                            || (val.Length >= 8 && Regex.IsMatch(val, @"^\d{1,2}[\/\-]\d{1,2}[\/\-]\d{4}"));
                        if (isDateCell)
                            colDateScores[c] = colDateScores.GetValueOrDefault(c) + 1;
                    }
                }
            }

            int minHits = Math.Max(2, sampleCount / 3);
            // minHits untuk kolom tanggal lebih longgar — cukup 1 hit karena tanggal jarang muncul di banyak kolom
            int minDateHits = Math.Max(1, sampleCount / 5);
            var partNoCandidates = colPartNoScores.Where(kv => kv.Value >= minHits).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            var alphaCandidates  = colPartNoAlphaScores.Where(kv => kv.Value >= minHits).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            var manifestCandidates = colManifestScores.Where(kv => kv.Value >= minHits).OrderBy(kv => kv.Key).Select(kv => kv.Key).ToList();

            // 1. Pick Part No
            colPartNo = alphaCandidates.FirstOrDefault() != 0 ? alphaCandidates.FirstOrDefault() : partNoCandidates.FirstOrDefault();

            // 2. Pick Manifest (leftmost numeric excluding PartNo)
            colManifest = manifestCandidates.FirstOrDefault(c => c != colPartNo);

            // 3. Pick Qty (Strong Heuristics)
            var qtyCandidates = colQtyScores.Where(kv => kv.Value >= minHits && kv.Key != colManifest && kv.Key != colPartNo)
                                            .Select(kv => new { 
                                                Col = kv.Key, 
                                                Hits = kv.Value, 
                                                Avg = (double)colSums.GetValueOrDefault(kv.Key) / kv.Value,
                                                DistinctCount = colDistinctVals[kv.Key].Count
                                            })
                                            .ToList();

            if (colQty == 0 && qtyCandidates.Any())
            {
                // RANKING QTY candidates:
                // - Preference 1: Has more than 1 distinct value (not a static flag like '1' in all rows)
                // - Preference 2: Higher average (more likely to be actual quantity vs sequence number) but not EXTREME
                // - Preference 3: Further right is common for Qty
                var bestQty = qtyCandidates
                    .OrderByDescending(x => x.DistinctCount > 1) // Column with varied values is better
                    .ThenByDescending(x => x.Avg > 1 && x.Avg < 10000) // Realistic Qty range (up to 10k)
                    .ThenByDescending(x => x.Col) // Further right is usually the actual quantity
                    .ThenByDescending(x => x.Avg) // Prefer non-zero averages
                    .FirstOrDefault();

                colQty = bestQty?.Col ?? 0;
            }

            if (colQty > 0)
                result.Warnings.Add($"Sistem mendeteksi kolom Qty otomatis (tanpa header) di Kolom {(char)('A' + colQty - 1)}.");

            // 4. Pick scheduled date column (jika belum dari hard-coded fallback)
            if (colScheduledDate == 0 && colDateScores.Any())
            {
                // Pilih kolom tanggal yang paling sering muncul, bukan manifest/partno/qty
                var bestDateCol = colDateScores
                    .Where(kv => kv.Key != colManifest && kv.Key != colPartNo && kv.Key != colQty)
                    .OrderByDescending(kv => kv.Value)
                    .FirstOrDefault();
                if (bestDateCol.Key > 0 && bestDateCol.Value >= minDateHits)
                {
                    colScheduledDate = bestDateCol.Key;
                    _logger.LogInformation("SmartImport {Cust} NoHeader: Kolom tanggal terdeteksi dari fingerprint = {Col}/{Letter} (hits={H})",
                        customer, colScheduledDate, (char)('A' + colScheduledDate - 1), bestDateCol.Value);
                }
            }

            if (colScheduledDate == 0)
                _logger.LogWarning("SmartImport {Cust} NoHeader: Kolom tanggal TIDAK ditemukan — ScheduledDate akan null", customer);
            else
                _logger.LogInformation("SmartImport {Cust} NoHeader: Kolom tanggal final = {Col}/{Letter}",
                    customer, colScheduledDate, (char)('A' + colScheduledDate - 1));

            _logger.LogInformation("SmartImport Detected (Dynamic): Manifest={M}, PartNo={P}, Qty={Q}", colManifest, colPartNo, colQty);

            // If we couldn't find part no column at all, fall back to text-based regex on each row
            if (colPartNo == 0)
            {
                _logger.LogWarning("SmartImport Excel NoHeader: Could not fingerprint columns, using legacy text-pattern scan.");
                var partPatterns = GetPartNumberPatterns(customer);
                for (int r = dataStartRow; r <= lastRow; r++)
                {
                    var rowText = "";
                    for (int c = 1; c <= Math.Min(lastCol, 50); c++)
                        rowText += ws.Cell(r, c).GetString() + " ";

                    foreach (var pattern in partPatterns)
                    {
                        var match = Regex.Match(rowText, pattern);
                        if (!match.Success) continue;
                        var pn = (match.Groups[1].Success ? match.Groups[1].Value : match.Value).Replace(" ", "").Trim();
                        if (pn.Length < 5) continue;
                        int qty = ExtractQuantity(rowText, pn);
                        if (qty <= 0) continue;
                        items.Add(new ExtractedItem { Manifesting = manifest ?? "", PartNo = pn, Qty = qty });
                        break;
                    }
                }
                return items;
            }

            // â”€â”€ Step 3: Read data rows using detected column positions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            string activeManifest = manifest ?? "";
            var itemMap = new Dictionary<string, ExtractedItem>(StringComparer.OrdinalIgnoreCase);
            DateTime? globalScheduledDate = null; // Tanggal pertama yang valid, dipakai sebagai fallback

            for (int r = dataStartRow; r <= lastRow; r++)
            {
                // Read Part No
                string partNo;
                try { partNo = ws.Cell(r, colPartNo).GetString().Trim().ToUpper(); } catch { continue; }
                if (string.IsNullOrEmpty(partNo)) continue;
                if (!Regex.IsMatch(partNo, @"[A-Z0-9]{4,}")) continue; // relaxed check for actual data row

                // Read Manifest
                if (colManifest > 0)
                {
                    string mVal;
                    try { mVal = ws.Cell(r, colManifest).GetString().Trim(); } catch { mVal = ""; }
                    if (!string.IsNullOrWhiteSpace(mVal) && manifestRegex.IsMatch(mVal))
                        activeManifest = mVal;
                }

                // Read Qty
                int qty = 0;
                if (colQty > 0)
                {
                    try
                    {
                        var qtyCell = ws.Cell(r, colQty);
                        if (qtyCell.DataType == XLDataType.Number)
                            qty = (int)Math.Round(qtyCell.GetDouble());
                        else
                            qty = ParseSmartQty(qtyCell.GetString().Trim());
                    }
                    catch { }
                }
                if (qty <= 0) continue;

                // Read tanggal jadwal
                DateTime? rowScheduledDate = null;
                if (colScheduledDate > 0)
                {
                    try
                    {
                        var dateCell = ws.Cell(r, colScheduledDate);
                        if (!dateCell.IsEmpty())
                        {
                            if (dateCell.DataType == XLDataType.DateTime)
                            {
                                rowScheduledDate = dateCell.GetDateTime().Date;
                            }
                            else
                            {
                                var dv = dateCell.Value.ToString().Trim();
                                if (DateTime.TryParse(dv,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTime pd))
                                {
                                    rowScheduledDate = pd.Date;
                                }
                                else if (DateTime.TryParseExact(dv,
                                    new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-MMM-yy", "dd-MMM-yy" },
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out DateTime pd2))
                                {
                                    rowScheduledDate = pd2.Date;
                                }
                            }

                            if (rowScheduledDate.HasValue && !globalScheduledDate.HasValue)
                            {
                                globalScheduledDate = rowScheduledDate;
                                _logger.LogInformation("SmartImport {Cust} NoHeader: Tanggal pertama = {Date} (baris {R}, kolom {C})",
                                    customer, rowScheduledDate.Value.ToString("yyyy-MM-dd"), r, colScheduledDate);
                                // Set ke result agar tampil di UI
                                if (result.DetectedScheduledDate == null)
                                    result.DetectedScheduledDate = globalScheduledDate;
                            }
                        }
                    }
                    catch { }
                }

                // TMMIN khusus: ambil dock dari kolom G (7), route dari kolom AD (30), lalu jam pick-up dari kolom Y (25)
                string? rowDockCode = null;
                string? rowRouteCode = null;
                TimeSpan? rowTimeOfDay = null;
                string? rowMappedCycle = null;
                if (customer != null && (customer.Contains("TMMIN") || customer.Contains("TOYOTA")))
                {
                    try {
                        var valG = ws.Cell(r, 7).Value.ToString().Trim();
                        if (string.IsNullOrEmpty(valG)) valG = ws.Cell(r, 6).Value.ToString().Trim();
                        if (string.IsNullOrEmpty(valG)) valG = ws.Cell(r, 8).Value.ToString().Trim();
                        if (!string.IsNullOrEmpty(valG)) rowDockCode = valG;
                    } catch { }

                    try {
                        var valAD = ws.Cell(r, 30).Value.ToString().Trim();
                        if (string.IsNullOrEmpty(valAD)) valAD = ws.Cell(r, 29).Value.ToString().Trim();
                        if (string.IsNullOrEmpty(valAD)) valAD = ws.Cell(r, 31).Value.ToString().Trim();
                        if (!string.IsNullOrEmpty(valAD)) rowRouteCode = valAD;
                    } catch { }

                    try {
                        // 1. Ambil tanggal dari kolom Z (26) - HANYA TANGGAL untuk jadwal delivery manifest
                        var cellZ = ws.Cell(r, 26);
                        if (!cellZ.IsEmpty())
                        {
                            if (cellZ.DataType == ClosedXML.Excel.XLDataType.DateTime)
                            {
                                var dtZ = cellZ.GetDateTime();
                                if (dtZ.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtZ.Date;
                            }
                            else if (cellZ.DataType == ClosedXML.Excel.XLDataType.Number)
                            {
                                double oaDate = cellZ.GetDouble();
                                if (oaDate >= 1)
                                {
                                    var dtZ = DateTime.FromOADate(oaDate);
                                    if (dtZ.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtZ.Date;
                                }
                            }
                            else
                            {
                                var valZ = cellZ.Value.ToString().Trim();
                                if (DateTime.TryParse(valZ, out DateTime dtZ1))
                                {
                                    if (dtZ1.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtZ1.Date;
                                }
                                else if (DateTime.TryParseExact(valZ, new[] { "dd/MM/yyyy HH:mm:ss", "dd-MM-yyyy HH:mm:ss", "d/M/yyyy H:m:s", "MM/dd/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtZ2))
                                {
                                    if (dtZ2.Date > new DateTime(1900, 1, 1)) rowScheduledDate = dtZ2.Date;
                                }
                                else if (valZ.Length >= 10 && DateTime.TryParse(valZ.Substring(0, 10), out DateTime dtShort))
                                {
                                    rowScheduledDate = dtShort.Date;
                                }
                            }
                        }
                    } catch { }

                    try {
                        // 2. Ambil jam dari kolom Y (25) - HANYA JAM untuk menentukan pickup/cycle
                        var cellY = ws.Cell(r, 25);
                        if (!cellY.IsEmpty())
                        {
                            if (cellY.DataType == XLDataType.DateTime)
                            {
                                rowTimeOfDay = cellY.GetDateTime().TimeOfDay;
                            }
                            else if (cellY.DataType == XLDataType.TimeSpan)
                            {
                                rowTimeOfDay = cellY.GetTimeSpan();
                            }
                            else if (cellY.DataType == XLDataType.Number)
                            {
                                double oaDateY = cellY.GetDouble();
                                rowTimeOfDay = DateTime.FromOADate(oaDateY).TimeOfDay;
                            }
                            else
                            {
                                var valY = cellY.Value.ToString().Trim();
                                if (DateTime.TryParse(valY, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime dtY))
                                {
                                    rowTimeOfDay = dtY.TimeOfDay;
                                }
                                else if (TimeSpan.TryParse(valY, out TimeSpan tsY))
                                {
                                    rowTimeOfDay = tsY;
                                }
                                else
                                {
                                    var timeMatch = Regex.Match(valY, @"\s?(\d{2}:\d{2})(?::\d{2})?");
                                    if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups[1].Value, out TimeSpan tsRegex))
                                    {
                                        rowTimeOfDay = tsRegex;
                                    }
                                }
                            }
                        }
                    } catch { }

                    try {
                        // 3. Deteksi cycle/lokasi dari kolom M (13) atau kolom C (3) untuk area 1 / 2
                        string cycleVal = "";
                        try { cycleVal = ws.Cell(r, 13).Value.ToString().Trim(); } catch { }
                        if (string.IsNullOrEmpty(cycleVal))
                        {
                            try { cycleVal = ws.Cell(r, 3).Value.ToString().Trim(); } catch { }
                        }
                        if (!string.IsNullOrEmpty(cycleVal))
                        {
                            rowMappedCycle = Regex.Match(cycleVal, @"\d").Value;
                        }
                    } catch { }
                }

                // Gunakan tanggal global sebagai fallback jika baris tidak punya tanggal
                if (!rowScheduledDate.HasValue && globalScheduledDate.HasValue)
                    rowScheduledDate = globalScheduledDate;

                // Format part no (TMMIN 12-char â†’ dash separated)
                string formattedPartNo = FormatTmminPartNo(partNo);

                // Merge duplicates within same manifest & date & time & dock
                string key = $"{activeManifest}|{rowDockCode}|{rowRouteCode}|{formattedPartNo}|{rowScheduledDate:yyyyMMdd}|{rowTimeOfDay}|{rowMappedCycle}";
                if (itemMap.TryGetValue(key, out var existing))
                {
                    existing.Qty += qty;
                    // Pertahankan ScheduledDate jika belum ada
                    if (!existing.ScheduledDate.HasValue && rowScheduledDate.HasValue)
                        existing.ScheduledDate = rowScheduledDate;
                    if (!existing.TimeOfDay.HasValue && rowTimeOfDay.HasValue)
                        existing.TimeOfDay = rowTimeOfDay;
                    if (string.IsNullOrEmpty(existing.DetectedDockCode) && !string.IsNullOrEmpty(rowDockCode))
                        existing.DetectedDockCode = rowDockCode;
                    if (string.IsNullOrEmpty(existing.DetectedRouteCode) && !string.IsNullOrEmpty(rowRouteCode))
                        existing.DetectedRouteCode = rowRouteCode;
                    if (string.IsNullOrEmpty(existing.DetectedCycleCode) && !string.IsNullOrEmpty(rowMappedCycle))
                        existing.DetectedCycleCode = rowMappedCycle;
                }
                else
                    itemMap[key] = new ExtractedItem { Manifesting = activeManifest, DetectedDockCode = rowDockCode, DetectedRouteCode = rowRouteCode, DetectedCycleCode = rowMappedCycle, PartNo = formattedPartNo, Qty = qty, ScheduledDate = rowScheduledDate, TimeOfDay = rowTimeOfDay };
            }

            items = itemMap.Values.ToList();
            _logger.LogInformation("SmartImport Excel NoHeader: {Count} unique items extracted (manifest={M}, partNoCol={P}, qtyCol={Q}, dateCol={D})", items.Count, activeManifest, colPartNo, colQty, colScheduledDate);
            return items;
        }


        #endregion

        #region === Customer Database Matching ===

        /// <summary>
        /// TMMIN-specific: match setiap item yang punya TimeOfDay ke Customer.Pickup di database.
        /// Ini memungkinkan auto-detect dock, route, dan cycle per item berdasarkan jam pickup.
        /// Misal: jam 19:00 â†’ Pickup "19:00" â†’ Customer "SAP ROUTE RC25 C2" â†’ Route "RC25", Cycle "C2"
        /// </summary>
        /// <summary>
        /// AHM Multi-Dock Matching — map DetectedDockCode (plant location tag) ke Customer DB.
        ///
        /// Location tags yang dihasilkan oleh ExtractItemsAhm:
        ///   "CIKARANG" → Customer AHM dengan "CIKARANG" / "CKD" di CustomerName/Code/Docking/Area
        ///   "KRW"      → Customer AHM dengan "KRW" / "KARAWANG" di CustomerName/Code/Docking/Area
        ///   "SUNTER"   → Customer AHM dengan "SUNTER" / "STR" di CustomerName/Code/Docking/Area
        ///
        /// Setelah matching berhasil, sets IsTmminMultiDock=true sehingga dock selection jadi opsional di UI.
        /// </summary>
        private async Task MatchAhmDocksByPlant(SmartImportResult result)
        {
            // Load semua customer AHM yang aktif
            var allCustomers = await _db.Customers.Where(c => c.IsActive).ToListAsync();
            var ahmCustomers = allCustomers.Where(c =>
                (c.CustomerCode ?? "").Contains("AHM", StringComparison.OrdinalIgnoreCase) ||
                (c.CustomerName ?? "").Contains("AHM", StringComparison.OrdinalIgnoreCase) ||
                (c.CustomerName ?? "").Contains("HONDA", StringComparison.OrdinalIgnoreCase) ||
                (c.Docking    ?? "").Contains("AHM", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (!ahmCustomers.Any())
            {
                _logger.LogWarning("SmartImport AHM DockByPlant: tidak ada customer AHM aktif di database.");
                return;
            }

            _logger.LogInformation(
                "SmartImport AHM DockByPlant: {Count} AHM customer(s) aktif: {Names}",
                ahmCustomers.Count,
                string.Join(", ", ahmCustomers.Select(c => $"{c.CustomerCode}|{c.CustomerName}|{c.Docking}|{c.Area}")));

            // Score function: seberapa cocok customer dengan location tag
            int ScoreDockForLocation(Customer cust, string locationTag)
            {
                int score = 0;
                var fields = new[]
                {
                    (cust.CustomerName ?? "").ToUpper(),
                    (cust.CustomerCode ?? "").ToUpper(),
                    (cust.Docking ?? "").ToUpper(),
                    (cust.Area    ?? "").ToUpper()
                };

                // Alias expansion:
                //   CIKARANG → CKG (kode dock AHM Cikarang), CIKARANG, P3 (Gate P3P3 = Plant III Cikarang)
                //   KRW      → KRW, KARAWANG
                //   SUNTER   → SUNTER, STR
                var aliases = locationTag switch
                {
                    "CIKARANG" => new[] { "CKG", "CIKARANG", "P3" },
                    "KRW"      => new[] { "KRW", "KARAWANG" },
                    "SUNTER"   => new[] { "SUNTER", "STR" },
                    _          => new[] { locationTag }
                };

                foreach (var f in fields)
                    foreach (var alias in aliases)
                        if (f.Contains(alias)) score += 10;

                return score;
            }

            int matchedCount = 0, unmatchedCount = 0;
            var matchedDockIds = new HashSet<int>();

            foreach (var item in result.Items)
            {
                var tag = (item.DetectedDockCode ?? "").Trim().ToUpper();
                if (string.IsNullOrEmpty(tag)) continue;

                Customer? bestMatch = null;
                int bestScore = 0;

                foreach (var cust in ahmCustomers)
                {
                    int score = ScoreDockForLocation(cust, tag);
                    if (score > bestScore) { bestScore = score; bestMatch = cust; }
                }

                if (bestMatch != null && bestScore > 0)
                {
                    item.MatchedCustomerId = bestMatch.CustomerId;
                    item.MatchedDockName   = bestMatch.CustomerName;
                    item.MatchedRoute      = bestMatch.Route;
                    item.MatchedCycle      = bestMatch.Cycle;
                    matchedDockIds.Add(bestMatch.CustomerId);
                    matchedCount++;
                    _logger.LogInformation(
                        "SmartImport AHM DockByPlant: Part '{Part}' plant='{Tag}' → Dock '{Dock}' (CustId={Id})",
                        item.PartNo, tag, bestMatch.CustomerName, bestMatch.CustomerId);
                }
                else
                {
                    unmatchedCount++;
                    _logger.LogWarning(
                        "SmartImport AHM DockByPlant: Part '{Part}' plant='{Tag}' → TIDAK ADA DOCK COCOK (customers checked: {Customers})",
                        item.PartNo, tag,
                        string.Join("; ", ahmCustomers.Select(c => $"{c.CustomerCode}|{c.CustomerName}|{c.Docking}")));
                }
            }

            if (matchedCount > 0)
            {
                // Reuse flag — dock selection jadi opsional di UI karena sudah auto-detect per item
                result.IsTmminMultiDock = true;

                // Hapus warning generik tentang pilih dock
                result.Warnings?.RemoveAll(w =>
                    w.Contains("Pilih dock yang sesuai") || w.Contains("dock tersebut"));

                _logger.LogInformation(
                    "SmartImport AHM DockByPlant: {Matched}/{Total} items matched, {Docks} unique dock(s)",
                    matchedCount, result.Items.Count, matchedDockIds.Count);

                // Fallback: item tanpa tag → inherit dock dari manifest yang sama (mayoritas)
                var manifestDockLookup = result.Items
                    .Where(i => i.MatchedCustomerId > 0 && !string.IsNullOrEmpty(i.Manifesting))
                    .GroupBy(i => new { Manifest = i.Manifesting!.Trim().ToUpper(), Date = i.ScheduledDate?.Date })
                    .ToDictionary(
                        g => g.Key,
                        g => g.GroupBy(i => i.MatchedCustomerId)
                              .OrderByDescending(x => x.Count())
                              .First().First());

                int inheritedCount = 0;
                foreach (var item in result.Items.Where(i => i.MatchedCustomerId == 0))
                {
                    var key = new { Manifest = item.Manifesting?.Trim().ToUpper() ?? "", Date = item.ScheduledDate?.Date };
                    if (!string.IsNullOrEmpty(key.Manifest) && manifestDockLookup.TryGetValue(key, out var rep))
                    {
                        item.MatchedCustomerId = rep.MatchedCustomerId;
                        item.MatchedDockName   = rep.MatchedDockName;
                        item.MatchedRoute      = rep.MatchedRoute;
                        item.MatchedCycle      = rep.MatchedCycle;
                        matchedDockIds.Add(rep.MatchedCustomerId);
                        inheritedCount++;
                    }
                }
                if (inheritedCount > 0)
                    _logger.LogInformation("SmartImport AHM DockByPlant: {N} item inherit dock dari manifest", inheritedCount);

                int stillUnmatched = result.Items.Count(i => i.MatchedCustomerId == 0);
                if (stillUnmatched > 0)
                    result.Warnings?.Add($"{stillUnmatched} item tidak bisa di-matching ke dock AHM. Pilih dock secara manual.");
            }
        }
        private async Task MatchTmminDocksByTime(SmartImportResult result)
        {
            // Load semua customer TMMIN yang aktif.
            // Filter via CustomerCode ATAU CustomerName agar dock yang CustomerCode-nya tidak
            // mengandung "TMMIN"/"TOYOTA" (mis. "DC43_C3") tetap bisa ikut matching.
            var tmminCustomers = await _db.Customers
                .Where(c => c.IsActive &&
                    ((c.CustomerCode != null && (c.CustomerCode.Contains("TMMIN") || c.CustomerCode.Contains("TOYOTA"))) ||
                     (c.CustomerName != null && (c.CustomerName.Contains("TMMIN") || c.CustomerName.Contains("TOYOTA")))))
                .ToListAsync();

            // Seed dari CustomerId global (customer yg terdeteksi dari file) dan sertakan
            // seluruh customer yang punya prefix nama yang sama (misal "TMMIN DOCK 43 C*").
            if (result.CustomerId.HasValue && result.CustomerId.Value > 0)
            {
                var seedCustomer = await _db.Customers.FindAsync(result.CustomerId.Value);
                if (seedCustomer != null && !tmminCustomers.Any(c => c.CustomerId == seedCustomer.CustomerId))
                    tmminCustomers.Add(seedCustomer);

                // Ambil prefix nama seed (hingga 12 karakter) untuk menemukan dock saudara (C1, C2, C3, ...)
                var seedName = (seedCustomer?.CustomerName ?? "").Trim();
                if (seedName.Length >= 5)
                {
                    // Strip trailing cycle indicator: "TMMIN DOCK 43 C1" → "TMMIN DOCK 43"
                    var prefixName = System.Text.RegularExpressions.Regex.Replace(seedName, @"\s+C\d+\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    if (prefixName.Length >= 5)
                    {
                        var siblings = await _db.Customers
                            .Where(c => c.IsActive && c.CustomerName != null && c.CustomerName.StartsWith(prefixName))
                            .ToListAsync();
                        foreach (var sib in siblings)
                            if (!tmminCustomers.Any(c => c.CustomerId == sib.CustomerId))
                                tmminCustomers.Add(sib);
                    }
                }
            }

            if (!tmminCustomers.Any())
            {
                _logger.LogWarning("SmartImport TMMIN DockByTime: Tidak ada customer TMMIN aktif di database.");
                return;
            }

            _logger.LogInformation(
                "SmartImport TMMIN DockByTime: {Count} customer kandidat: {Names}",
                tmminCustomers.Count,
                string.Join(", ", tmminCustomers.Select(c => $"{c.CustomerCode}|{c.CustomerName}|Cycle={c.Cycle}")));

            int matchedCount = 0;
            int unmatchedCount = 0;
            var matchedDockIds = new HashSet<int>();

            // Hitung jam unik (diurutkan kronologis) untuk Area 1 dan Area 2 per dock
            var area1TimesPerDock = new Dictionary<string, List<TimeSpan>>();
            var area2TimesPerDock = new Dictionary<string, List<TimeSpan>>();
            
            var dockGroups = result.Items
                .Where(i => !string.IsNullOrEmpty(i.DetectedDockCode) && i.TimeOfDay.HasValue)
                .GroupBy(i => i.DetectedDockCode!.Trim().ToUpper());
                
            foreach (var g in dockGroups)
            {
                area1TimesPerDock[g.Key] = g.Where(i => i.Area?.Trim() == "1").Select(i => i.TimeOfDay!.Value).Distinct().OrderBy(t => t).ToList();
                area2TimesPerDock[g.Key] = g.Where(i => i.Area?.Trim() == "2").Select(i => i.TimeOfDay!.Value).Distinct().OrderBy(t => t).ToList();
            }

            // ——— Mapping cycle-code kolom G —> keyword dock untuk PBOD ———
            var pbodCycleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "8N", "8L", "7N", "7L", "7H", "6N", "6L", "5N", "5L" };

            foreach (var item in result.Items.Where(i => i.TimeOfDay.HasValue || !string.IsNullOrEmpty(i.DetectedDockCode) || !string.IsNullOrEmpty(i.DetectedRouteCode)))
            {
                var hasTime = item.TimeOfDay.HasValue;
                var itemTime = item.TimeOfDay;
                var rawDockCode = item.DetectedDockCode?.Trim().ToUpper();
                var routeCode = item.DetectedRouteCode?.Trim().ToUpper();

                // Jika ada DetectedCycleCode dari item, gunakan sebagai cycleHint utama
                string? cycleHint = !string.IsNullOrEmpty(item.DetectedCycleCode) ? item.DetectedCycleCode : null;
                var dockCode = rawDockCode;

                if (string.IsNullOrEmpty(cycleHint))
                {
                    if (!string.IsNullOrEmpty(rawDockCode) && pbodCycleCodes.Contains(rawDockCode))
                    {
                        cycleHint = rawDockCode; // simpan untuk bonus cycle matching
                        if (rawDockCode.StartsWith("8")) dockCode = "PBOD DOCK 8";
                        else if (rawDockCode.StartsWith("7")) dockCode = "PBOD DOCK 7";
                        else if (rawDockCode.StartsWith("6")) dockCode = "PBOD DOCK 6";
                        else if (rawDockCode.StartsWith("5")) dockCode = "PBOD DOCK 5";
                        else dockCode = "PBOD";
                    }
                    else if (!string.IsNullOrEmpty(rawDockCode))
                    {
                        // Non-PBOD: "C1" -> cycleHint="C1" -> customer Cycle="C1" dapat +60
                        cycleHint = rawDockCode;
                    }

                    // Fallback ke DetectedCycle global jika cycleHint kosong
                    if (string.IsNullOrEmpty(cycleHint) && !string.IsNullOrEmpty(result.DetectedCycle))
                    {
                        cycleHint = result.DetectedCycle;
                    }
                }
                else
                {
                    // Jika dockCode adalah cycle PBOD (8N, 8L, dst) -> gunakan "PBOD DOCK x" sebagai dockCode
                    if (!string.IsNullOrEmpty(rawDockCode) && pbodCycleCodes.Contains(rawDockCode))
                    {
                        if (rawDockCode.StartsWith("8")) dockCode = "PBOD DOCK 8";
                        else if (rawDockCode.StartsWith("7")) dockCode = "PBOD DOCK 7";
                        else if (rawDockCode.StartsWith("6")) dockCode = "PBOD DOCK 6";
                        else if (rawDockCode.StartsWith("5")) dockCode = "PBOD DOCK 5";
                        else dockCode = "PBOD";
                    }
                }

                var itemTimeStr = hasTime ? $"{itemTime!.Value.Hours:D2}:{itemTime!.Value.Minutes:D2}" : "NULL";

                // Cari customer yang paling mendekati kecocokannya
                Customer? bestMatch = null;

                // 1. TMMIN jam-lokasi matching (jika memiliki jam dan kandidat dock teridentifikasi)
                if (hasTime && !string.IsNullOrEmpty(dockCode))
                {
                    var safeDockCode = new string(dockCode.Where(char.IsLetterOrDigit).ToArray()).ToUpper();
                    // Alias 1E -> PBOD STR
                    if (safeDockCode == "1E") safeDockCode = "PBODSTR";

                    var dockCandidates = tmminCustomers
                        .Where(c => {
                            var safeCustName = new string((c.CustomerName ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpper();
                            return safeCustName.Contains(safeDockCode) || safeDockCode.Contains(safeCustName);
                        })
                        .ToList();

                    if (dockCandidates.Any())
                    {
                        int hourY = itemTime!.Value.Hours;
                        string locationVal = item.Area?.Trim() ?? ""; // Kolom C/M: "1" atau "2"
                        string itemCycleHint = item.DetectedCycleCode?.Trim() ?? "";

                        // TMMIN FIX: First, try to find an EXACT cycle match among the candidates for this dock.
                        // This prevents Cycle 2 from disappearing if its Pickup time in the database is empty or invalid.
                        var exactCycleMatch = dockCandidates.FirstOrDefault(c =>
                            !string.IsNullOrEmpty(itemCycleHint) &&
                            !string.IsNullOrEmpty(c.Cycle) &&
                            c.Cycle.Equals(itemCycleHint, StringComparison.OrdinalIgnoreCase));

                        if (exactCycleMatch != null)
                        {
                            bestMatch = exactCycleMatch;
                            _logger.LogInformation("TMMIN Cycle Match: Forced exact match for Dock {Dock} Cycle {Cycle}", dockCode, itemCycleHint);
                        }
                        else
                        {
                            // --- CHRONOLOGICAL PAIRING LOGIC ---
                            // Jika dock ini memiliki multiple area, kita gunakan jam Area 1 sebagai patokan untuk Area 2
                            if (!string.IsNullOrEmpty(rawDockCode) && locationVal == "2")
                            {
                                var dockKey = rawDockCode.Trim().ToUpper();
                                if (area1TimesPerDock.TryGetValue(dockKey, out var a1Times) && a1Times.Any() &&
                                    area2TimesPerDock.TryGetValue(dockKey, out var a2Times) && a2Times.Any())
                                {
                                    int rank = a2Times.IndexOf(itemTime!.Value);
                                    if (rank >= 0)
                                    {
                                        // Ambil jam Area 1 dengan ranking yang sama (cap at max length)
                                        int mappedRank = Math.Min(rank, a1Times.Count - 1);
                                        hourY = a1Times[mappedRank].Hours;
                                        _logger.LogInformation("TMMIN Area 2 Pairing: Item time {T2} (rank {R}) mapped to Area 1 time {T1} for matching.", itemTime!.Value, rank, a1Times[mappedRank]);
                                    }
                                }
                            }

                            double minDiff = double.MaxValue;

                            foreach (var cust in dockCandidates)
                            {
                                if (string.IsNullOrWhiteSpace(cust.Pickup) || !TimeSpan.TryParse(cust.Pickup.Trim(), out TimeSpan pickupTs))
                                    continue;

                                int custPickupHour = pickupTs.Hours;
                                
                                // Karena Area 2 sudah menggunakan jam Area 1 (jika berpasangan),
                                // kita gunakan Absolute Match (jam terdekat) untuk semuanya
                                double diff = Math.Abs(hourY - custPickupHour);
                                if (diff > 12) diff = 24 - diff;

                                if (diff < minDiff)
                                {
                                    minDiff = diff;
                                    bestMatch = cust;
                                }
                                else if (diff == minDiff)
                                {
                                    // Jika selisih waktu sama persis, gunakan kecocokan cycle sebagai tie-breaker!
                                    // Hitung score untuk cust ini
                                    int currentScore = 0;
                                    if (!string.IsNullOrEmpty(itemCycleHint) && !string.IsNullOrEmpty(cust.Cycle))
                                    {
                                        if (cust.Cycle.Equals(itemCycleHint, StringComparison.OrdinalIgnoreCase))
                                            currentScore += 60;
                                    }
                                    
                                    int bestMatchScore = 0;
                                    if (bestMatch != null && !string.IsNullOrEmpty(itemCycleHint) && !string.IsNullOrEmpty(bestMatch.Cycle))
                                    {
                                        if (bestMatch.Cycle.Equals(itemCycleHint, StringComparison.OrdinalIgnoreCase))
                                            bestMatchScore += 60;
                                    }

                                    if (currentScore > bestMatchScore)
                                    {
                                        bestMatch = cust;
                                    }
                                }
                            }
                        }
                    }
                }

                // 2. Fallback ke scoring model lama jika tidak ter-match dengan pencarian jam-lokasi di atas
                if (bestMatch == null)
                {
                    int bestScore = -1;
                    foreach (var cust in tmminCustomers)
                    {
                        int score = 0;
                        bool failed = false;

                        // Bersihkan karakter khusus untuk perbandingan yang lebih tahan banting
                        var safeCustRoute = new string((cust.Route ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpper();
                        var safeRouteCode = new string((routeCode ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpper();
                        var safeCustName = new string((cust.CustomerName ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpper();
                        var safeDockCode = new string((dockCode ?? "").Where(c => char.IsLetterOrDigit(c)).ToArray()).ToUpper();
                        
                        // Alias 1E -> PBOD STR
                        if (safeDockCode == "1E") safeDockCode = "PBODSTR";

                        int dockRouteScore = 0;
                        // 1. Cek Route dari AD (Bobot Utama)
                        if (!string.IsNullOrEmpty(safeRouteCode))
                        {
                            if (safeCustRoute.Contains(safeRouteCode) || safeRouteCode.Contains(safeCustRoute))
                                dockRouteScore += 50; 
                        }

                        // 2. Cek string Dock dari G 
                        if (!string.IsNullOrEmpty(safeDockCode))
                        {
                            if (safeCustName.Contains(safeDockCode) || safeDockCode.Contains(safeCustName))
                                dockRouteScore += 30;
                        }
                        
                        // Strict check: jika dock/route disediakan tapi sama sekali tidak match, 
                        // langsung gagalkan (jangan berikan kesempatan menang via jam/cycle)
                        if ((!string.IsNullOrEmpty(safeDockCode) || !string.IsNullOrEmpty(safeRouteCode)) && dockRouteScore == 0)
                            failed = true;

                        score += dockRouteScore;

                        // 2b. Bonus: jika cycleHint (8N/8L/7N/7L) cocok dengan Cycle customer (bobot tinggi)
                        if (!string.IsNullOrEmpty(cycleHint) && !failed)
                        {
                            var safeCustCycle = (cust.Cycle ?? "").Trim().ToUpper();
                            var cycleHintUpper = cycleHint.ToUpper();
                            if (safeCustCycle == cycleHintUpper ||
                                safeCustCycle.Contains(cycleHintUpper) ||
                                cycleHintUpper.Contains(safeCustCycle))
                            {
                                score += 60; // cycle exact match = sangat memprioritaskan customer ini
                            }
                            else
                            {
                                // Mismatched cycle: jika keduanya adalah cycle (misal C1/C2 atau 8N/8L), hard fail untuk menghindari kesalahan penetapan cycle!
                                bool isCustCycleC = safeCustCycle.StartsWith("C") && safeCustCycle.Length > 1 && char.IsDigit(safeCustCycle[1]);
                                bool isHintCycleC = cycleHintUpper.StartsWith("C") && cycleHintUpper.Length > 1 && char.IsDigit(cycleHintUpper[1]);
                                
                                bool isCustCyclePBOD = pbodCycleCodes.Contains(safeCustCycle);
                                bool isHintCyclePBOD = pbodCycleCodes.Contains(cycleHintUpper);
                                
                                if ((isCustCycleC && isHintCycleC) || (isCustCyclePBOD && isHintCyclePBOD))
                                {
                                    failed = true;
                                }
                            }
                        }


                        // 3. Cek jam dari kolom Y (TimeOfDay)
                        if (!failed && hasTime)
                        {
                            if (!string.IsNullOrWhiteSpace(cust.Pickup) && TimeSpan.TryParse(cust.Pickup.Trim(), out TimeSpan pickupTs))
                            {
                                var diff = Math.Abs((itemTime!.Value - pickupTs).TotalMinutes);
                                if (diff > 12 * 60) diff = (24 * 60) - diff;
                                
                                if (diff >= 6 * 60 && !safeCustName.Contains(safeDockCode))
                                {
                                    failed = true;
                                }
                                else
                                {
                                    var timeScore = (int)Math.Max(0, 120.0 - (diff / 3.0));
                                    score += timeScore;
                                }
                            }
                        }

                        if (!failed && score > bestScore)
                        {
                            bestScore = score;
                            bestMatch = cust;
                        }
                    }
                }

                if (bestMatch != null)
                {
                    item.MatchedCustomerId = bestMatch.CustomerId;
                    item.MatchedDockName = bestMatch.CustomerName;
                    item.MatchedRoute = bestMatch.Route;
                    item.MatchedCycle = bestMatch.Cycle;
                    matchedDockIds.Add(bestMatch.CustomerId);
                    matchedCount++;

                    // Pastikan jam item sinkron dengan jam pickup master customer
                    if (!string.IsNullOrWhiteSpace(bestMatch.Pickup) && TimeSpan.TryParse(bestMatch.Pickup.Trim(), out TimeSpan pickupTs))
                    {
                        item.TimeOfDay = pickupTs;
                    }

                    _logger.LogInformation(
                        "SmartImport TMMIN DockByTime: Item '{Part}' jam {Time} -> Dock '{Dock}' (Route={Route}, Cycle={Cycle}, CustId={Id})",
                        item.PartNo, itemTimeStr, bestMatch.CustomerName, bestMatch.Route, bestMatch.Cycle, bestMatch.CustomerId);
                }
                else
                {
                    unmatchedCount++;
                    _logger.LogWarning(
                        "SmartImport TMMIN DockByTime: Item '{Part}' jam {Time} â†’ TIDAK ADA DOCK COCOK",
                        item.PartNo, itemTimeStr);
                }
            }

            // Set flag multi-dock:
            // - Jika ada item yang berhasil di-match ke customer spesifik → multi-dock aktif
            // - ATAU jika ada TimeOfDay di file (cycle dibedakan dari jam) tapi belum ada Pickup di master
            //   → tetap set multi-dock agar user bisa lihat dock-badge dan perItemDock=true
            var hasTimeInFile = result.Items.Any(i => i.TimeOfDay.HasValue);
            if (matchedCount > 0 || (hasTimeInFile && matchedDockIds.Count == 0 && unmatchedCount > 0))
            {
                result.IsTmminMultiDock = true;
                _logger.LogInformation(
                    "SmartImport TMMIN DockByTime: {Matched}/{Total} items matched, {Unmatched} unmatched, {DockCount} unique docks",
                    matchedCount, result.Items.Count, unmatchedCount, matchedDockIds.Count);

                // Hapus warning dari proses MatchCustomerToDatabase generik yang membingungkan
                if (result.Warnings != null)
                {
                    result.Warnings.RemoveAll(w => w.Contains("Pilih dock yang sesuai") || w.Contains("tidak ditemukan"));
                }

                // â”€â”€ Fallback: item yang tidak ter-match â†’ inherit dock dari manifest yang sama â”€â”€
                // Bangun lookup: manifest â†’ CustomerId yang paling sering muncul (majority vote)
                var manifestDockLookup = result.Items
                    .Where(i => i.MatchedCustomerId > 0 && !string.IsNullOrEmpty(i.Manifesting))
                    .GroupBy(i => new { Manifest = i.Manifesting!.Trim().ToUpper(), Date = i.ScheduledDate?.Date })
                    .ToDictionary(
                        g => g.Key,
                        g => g.GroupBy(i => i.MatchedCustomerId)
                              .OrderByDescending(x => x.Count())
                              .First().First() // ambil item representatif dari dock mayoritas
                    );

                int inheritedCount = 0;
                foreach (var item in result.Items.Where(i => i.MatchedCustomerId == 0))
                {
                    var manifestKey = new { Manifest = item.Manifesting?.Trim().ToUpper() ?? "", Date = item.ScheduledDate?.Date };
                    if (!string.IsNullOrEmpty(manifestKey.Manifest) && manifestDockLookup.TryGetValue(manifestKey, out var rep))
                    {
                        item.MatchedCustomerId = rep.MatchedCustomerId;
                        item.MatchedDockName   = rep.MatchedDockName;
                        item.MatchedRoute      = rep.MatchedRoute;
                        item.MatchedCycle      = rep.MatchedCycle;
                        matchedDockIds.Add(rep.MatchedCustomerId);
                        inheritedCount++;
                    }
                }

                // Hitung ulang unmatched setelah fallback
                int stillUnmatched = result.Items.Count(i => i.MatchedCustomerId == 0);

                if (inheritedCount > 0)
                    _logger.LogInformation("SmartImport TMMIN DockByTime: {N} item inherit dock dari manifest yang sama", inheritedCount);

                if (stillUnmatched > 0)
                {
                    result.Warnings.Add($"{stillUnmatched} item tidak bisa di-matching ke dock berdasarkan jam/cycle. Pilih dock secara manual untuk item tersebut.");
                }
            }
        }

        private async Task MatchCustomerToDatabase(SmartImportResult result)
        {
            if (string.IsNullOrEmpty(result.DetectedCustomerName) || result.DetectedCustomerName == "UNKNOWN")
                return;

            var customers = await _db.Customers.Where(c => c.IsActive).ToListAsync();
            
            // Find ALL docks that match the detected customer name
            List<Customer> matchedCustomers = new();

            // 1. Direct code match (e.g. "ADM" == CustomerCode "ADM")
            matchedCustomers = customers.Where(c =>
                !string.IsNullOrWhiteSpace(c.CustomerCode) &&
                c.CustomerCode.Trim().Equals(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();

            // 2. Code contains match — CustomerCode mengandung nama customer yang terdeteksi
            //    Misal detectedName="ADM" â†’ hanya match CustomerCode yang mengandung "ADM"
            //    (tidak ikut match CustomerCode lain seperti "HONDA" hanya karena alias)
            if (!matchedCustomers.Any())
            {
                matchedCustomers = customers.Where(c =>
                    !string.IsNullOrWhiteSpace(c.CustomerCode) &&
                    c.CustomerCode.Contains(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 3. Name contains match
            if (!matchedCustomers.Any())
            {
                matchedCustomers = customers.Where(c =>
                    !string.IsNullOrWhiteSpace(c.CustomerName) &&
                    c.CustomerName.Contains(result.DetectedCustomerName, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 4. Broader keyword/alias match — HANYA jika step 1-3 benar-benar tidak ada hasil
            if (!matchedCustomers.Any())
            {
                var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    { "AHM", new[] { "HONDA", "AHM", "ASTRA HONDA" } },
                    { "ADM", new[] { "DAIHATSU", "ADM", "ASTRA DAIHATSU" } },
                    { "TMMIN", new[] { "TOYOTA", "TMMIN" } },
                    { "SANOH", new[] { "SANOH" } },
                    { "HINO", new[] { "HINO", "HINO MOTORS", "PT HINO", "PT. HINO" } },
                    { "TMMIN_TXT", new[] { "TMMIN", "TOYOTA MOTOR", "TOYOTA" } },
                };

                if (aliases.TryGetValue(result.DetectedCustomerName, out var terms))
                {
                    foreach (var term in terms)
                    {
                        matchedCustomers = customers.Where(c =>
                            (c.CustomerName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.CustomerCode ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.Docking ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            (c.Area ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (matchedCustomers.Any()) break;
                    }
                }
            }

            if (matchedCustomers.Any())
            {
                // â”€â”€ Urutkan dock: yang Route-nya cocok dengan DetectedRoute letakkan paling atas â”€â”€
                // Ini membantu pre-select dock yang paling relevan secara otomatis
                // ── Urutkan dock: yang Route/Cycle-nya cocok dengan DetectedRoute/DetectedCycle letakkan paling atas ──
                // Ini membantu pre-select dock yang paling relevan secara otomatis
                if (!string.IsNullOrWhiteSpace(result.DetectedRoute) || !string.IsNullOrWhiteSpace(result.DetectedCycle))
                {
                    var routeNorm = (result.DetectedRoute ?? "").Trim().ToUpper();
                    var cycleNorm = (result.DetectedCycle ?? "").Trim().ToUpper();
                    matchedCustomers = matchedCustomers
                        .OrderByDescending(c =>
                        {
                            int score = 0;
                            if (!string.IsNullOrWhiteSpace(routeNorm))
                            {
                                if (!string.IsNullOrWhiteSpace(c.Route) &&
                                    c.Route.Trim().ToUpper().Contains(routeNorm)) score += 10;
                                if (!string.IsNullOrWhiteSpace(c.Docking) &&
                                    c.Docking.Trim().ToUpper().Contains(routeNorm)) score += 5;
                                if (!string.IsNullOrWhiteSpace(c.CustomerName) &&
                                    c.CustomerName.Trim().ToUpper().Contains(routeNorm)) score += 5;
                                if (!string.IsNullOrWhiteSpace(c.Area) &&
                                    c.Area.Trim().ToUpper().Contains(routeNorm)) score += 3;
                            }
                            if (!string.IsNullOrWhiteSpace(cycleNorm))
                            {
                                if (!string.IsNullOrWhiteSpace(c.Cycle) &&
                                    c.Cycle.Trim().ToUpper().Equals(cycleNorm, StringComparison.OrdinalIgnoreCase)) score += 20;
                            }
                            return score;
                        })
                        .ThenBy(c => c.CustomerCode)
                        .ToList();
                }

                // Populate all matching docks
                result.MatchedDocks = matchedCustomers.Select(c => new MatchedDock
                {
                    CustomerId = c.CustomerId,
                    CustomerCode = c.CustomerCode ?? "",
                    DockName = c.CustomerName ?? "",
                    Route = c.Route,
                    Cycle = c.Cycle,
                    Docking = c.Docking,
                    Area = c.Area
                }).ToList();

                // Set first match sebagai default (sudah diurutkan berdasarkan kecocokan route)
                var first = matchedCustomers.First();
                result.CustomerId = first.CustomerId;
                result.DetectedCustomerCode = first.CustomerCode;
                result.DetectedDock = first.CustomerName;

                _logger.LogInformation(
                    "SmartImport: Matched {Count} dock(s) for '{Name}' (route='{Route}') — best: '{Code}' (ID={Id})",
                    matchedCustomers.Count, result.DetectedCustomerName,
                    result.DetectedRoute ?? "-", first.CustomerCode, first.CustomerId);

                if (matchedCustomers.Count > 1)
                {
                    result.Warnings.Add($"Ditemukan {matchedCustomers.Count} dock untuk customer '{result.DetectedCustomerName}'. Pilih dock yang sesuai.");
                }
            }
            else
            {
                result.Warnings.Add($"Customer '{result.DetectedCustomerName}' tidak ditemukan di database. Pilih customer/dock secara manual.");
            }
        }

        #region TMMIN TXT Parser
        private List<ExtractedItem> ExtractItemsTmminTxt(List<string> lines, string? headerManifest)
        {
            var items = new List<ExtractedItem>();
            string activeManifest = headerManifest ?? "";

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Contains("|")) continue;

                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) continue;

                // 1. Manifest: Kolom 1 (10 digit, biasanya mulai 126 atau 127)
                string lineManifest = parts[0].Trim();
                if (lineManifest.Length == 10 && Regex.IsMatch(lineManifest, @"^\d+$"))
                {
                    activeManifest = lineManifest;
                }

                // 2. Part No & Qty
                string partNo = "";
                int qty = 0;

                // Cari PartNo dari kolom sebelah kanan (layout TMMIN: Manifest ... Order/Date ... PartNo ... Qty)
                // Kita cari dari indeks terakhir - 1 (sebelum Qty)
                for (int i = parts.Length - 2; i >= 1; i--)
                {
                    string candidate = parts[i].Trim();
                    
                    // Toyota PartNo: 10 atau 12 alnum, dan TIDAK diawali "202" (Year/Date)
                    if (Regex.IsMatch(candidate, @"^\d{5}[A-Z0-9]{5,7}$") && 
                        candidate != lineManifest && 
                        !candidate.StartsWith("202"))
                    {
                        partNo = candidate;
                        
                        // 3. Qty: Berdasarkan gambar manifest TMMIN, Qty berada setelah Shop Code (4 digit: 7203, 4482, dll)
                        // Shop Code biasanya muncul setelah kolom alamat/lokasi.
                        for (int j = i + 1; j < parts.Length; j++)
                        {
                            // Cari angka 4 digit (Shop Code)
                            if (Regex.IsMatch(parts[j], @"^\d{4}$"))
                            {
                                // Qty adalah angka di kolom berikutnya
                                if (j + 1 < parts.Length && int.TryParse(parts[j+1], out int q) && q > 0)
                                {
                                    qty = q;
                                    break;
                                }
                            }
                        }

                        // Fallback: Jika shop code tidak ditemukan, ambil angka terakhir yang masuk akal (< 1000)
                        if (qty == 0)
                        {
                            for (int j = parts.Length - 1; j > i; j--)
                            {
                                if (int.TryParse(parts[j], out int q) && q > 0 && q < 1000)
                                {
                                    qty = q;
                                    break;
                                }
                            }
                        }
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(partNo) && qty > 0)
                {
                    items.Add(new ExtractedItem
                    {
                        Manifesting = activeManifest,
                        PartNo = FormatTmminPartNo(partNo),
                        Qty = qty
                    });
                }
            }

            return items;
        }

        private string FormatTmminPartNo(string raw)
        {
            if (raw.Length == 12 && !raw.Contains("-"))
            {
                // 483410K15000 -> 48341-0K150-00
                return $"{raw.Substring(0, 5)}-{raw.Substring(5, 5)}-{raw.Substring(10, 2)}";
            }
            if (raw.Length == 10 && !raw.Contains("-"))
            {
                // 483410K150 -> 48341-0K150
                return $"{raw.Substring(0, 5)}-{raw.Substring(5, 5)}";
            }
            return raw;
        }
        #endregion

        #endregion
        /// <summary>
        /// Robust Qty parser that handles thousands separators (1.000 -> 1000)
        /// and numeric formats from various Excel exports.
        /// </summary>
        private int ParseSmartQty(string? val)
        {
            if (string.IsNullOrWhiteSpace(val)) return 0;
            string clean = val.Trim();

            // 1. If it matches 1.000 or 1,000 pattern (thousands separator)
            if (Regex.IsMatch(clean, @"^\d{1,3}([.,]\d{3})+$"))
            {
                return int.TryParse(clean.Replace(".", "").Replace(",", ""), out int res) ? res : 0;
            }

            // 2. Try normal int parse
            if (int.TryParse(clean, out int ires)) return ires;

            // 3. Try double (decimal) parse and round
            // Use Invariant to ensure dot is decimal, but if that yields 1 for "1.000", we have step 1 above
            if (double.TryParse(clean.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture, out double dres))
            {
                return (int)Math.Round(dres);
            }

            return 0;
        }

        #region === ADM KEP New Format Parser ===

        // â”€â”€ OpenXML helpers (bypass ClosedXML for files with embedded images/shapes) â”€â”€â”€â”€â”€â”€

        private static int OxmlColIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpper(ch) - 'A' + 1);
            }
            return col;
        }

        private static string OxmlCellValue(DocumentFormat.OpenXml.Spreadsheet.Cell? cell,
            DocumentFormat.OpenXml.Spreadsheet.SharedStringTable? sst)
        {
            if (cell?.CellValue == null) return "";
            var raw = cell.CellValue.Text ?? "";
            if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString && sst != null)
            {
                if (int.TryParse(raw, out int idx))
                {
                    var items = sst.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>().ToList();
                    if (idx >= 0 && idx < items.Count)
                        return items[idx].InnerText ?? "";
                }
                return raw;
            }
            return raw;
        }

        private static Dictionary<int, string> OxmlGetRowCells(
            DocumentFormat.OpenXml.Spreadsheet.Row row,
            DocumentFormat.OpenXml.Spreadsheet.SharedStringTable? sst)
        {
            var result = new Dictionary<int, string>();
            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>())
            {
                var cellRef = cell.CellReference?.Value;
                if (string.IsNullOrEmpty(cellRef)) continue;
                int col = OxmlColIndex(cellRef);
                if (col > 0) result[col] = OxmlCellValue(cell, sst);
            }
            return result;
        }

        private static bool TryParseAdmDate(string raw, out DateTime result)
        {
            result = default;
            var s = raw.TrimStart(':', ' ').Trim();
            if (string.IsNullOrEmpty(s)) return false;

            // Try common formats used by ADM: "17-Dec-2025", "09-Jan-2026", "2026-01-09", etc.
            var formats = new[] { "dd-MMM-yyyy", "d-MMM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MMM-yy", "d-MMM-yy" };
            if (DateTime.TryParseExact(s, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result)) return true;

            // OADate serial (Excel stores dates as numbers)
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double serial)
                && serial > 1000 && serial < 100000)
            {
                try { result = DateTime.FromOADate(serial); return true; } catch { }
            }

            return DateTime.TryParse(s, out result);
        }

        /// <summary>
        /// Probes an already-buffered Excel stream for the ADM KEP Packing Instruction format.
        /// Uses OpenXML SDK directly to tolerate files with embedded images/shapes that ClosedXML rejects.
        /// Stream position after this call is undefined — caller must reset to 0.
        /// </summary>
        private bool IsAdmKepNewFormat(Stream excelStream)
        {
            try
            {
                using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(excelStream, false);
                var wbPart = doc.WorkbookPart;
                if (wbPart == null) return false;

                var firstSheet = wbPart.Workbook
                    .Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault();
                if (firstSheet?.Id?.Value == null) return false;

                var wsPart = (DocumentFormat.OpenXml.Packaging.WorksheetPart)
                    wbPart.GetPartById(firstSheet.Id.Value);

                // If the worksheet has embedded drawings (images/shapes), ClosedXML will fail.
                // In that case we only need a partial column match to route via OpenXML path.
                bool hasDrawings = wsPart.DrawingsPart != null;

                var sheetData = wsPart.Worksheet
                    .Descendants<DocumentFormat.OpenXml.Spreadsheet.SheetData>().FirstOrDefault();
                if (sheetData == null) return false;

                var sst = wbPart.SharedStringTablePart?.SharedStringTable;
                int rowNum = 0;
                bool anyPartNo = false, anyDN = false, anyQtyOrder = false;

                foreach (var row in sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>())
                {
                    if (++rowNum > 50) break;   // extended from 30 → 50 to handle larger header sections
                    var cells = OxmlGetRowCells(row, sst);

                    bool hasDN = false, hasQtyOrder = false, hasPartNo = false;
                    foreach (var v in cells.Values)
                    {
                        var u = v.Replace("\n", " ").Replace("\r", " ").Trim().ToUpper();
                        if (string.IsNullOrEmpty(u)) continue;

                        if (u == "DN" || u.StartsWith("DN ") || u.Contains("DN NO") ||
                            u.Contains("DN NUMBER") || u.Contains("DELIVERY NOTE") || u.Contains("D/N"))
                        { hasDN = true; anyDN = true; }

                        if (u.Contains("QTY ORDER") || u.Contains("ORDER (PCS)") || u.Contains("ORDER QTY") ||
                            u.Contains("QTY (PCS)") || u.Contains("ORDER  (PCS)") || u == "QTY/BOX" ||
                            u.Contains("QTY PCS") || u.Contains("TOTAL QTY"))
                        { hasQtyOrder = true; anyQtyOrder = true; }

                        if (u.Contains("PART NO") || u.Contains("PART NUMBER") || u.Contains("MATERIAL NO") ||
                            u.StartsWith("PART NO"))
                        { hasPartNo = true; anyPartNo = true; }
                    }

                    if (hasDN && hasQtyOrder && hasPartNo)
                    {
                        _logger.LogInformation("SmartImport: ADM KEP new format detected via OpenXML probe (row {R})", rowNum);
                        return true;
                    }
                }

                // Fallback: file has embedded images (ClosedXML will fail) AND we found at least 2 out of 3 key columns
                // — treat it as ADM KEP new format and let ParseAdmKepExcelAsync handle the error gracefully.
                if (hasDrawings)
                {
                    int colMatches = (anyDN ? 1 : 0) + (anyPartNo ? 1 : 0) + (anyQtyOrder ? 1 : 0);
                    if (colMatches >= 2)
                    {
                        _logger.LogInformation("SmartImport: ADM KEP new format detected via image+partial-column fallback (DN={D}, PartNo={P}, Qty={Q})",
                            anyDN, anyPartNo, anyQtyOrder);
                        return true;
                    }
                    // Has images but not a recognizable ADM KEP format — still route via OpenXML to avoid ClosedXML crash.
                    _logger.LogInformation("SmartImport: File has embedded drawings but no ADM KEP columns — routing via OpenXML to avoid ClosedXML failure.");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SmartImport IsAdmKepNewFormat probe failed: {Msg}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Probes an already-buffered Excel stream for the TMMIN Excel format (No Headers, specific columns).
        /// Uses OpenXML SDK directly to avoid loading entire data into memory for just a probe.
        /// Stream position after this call is undefined — caller must reset to 0.
        /// </summary>
        private bool IsTmminExcelFormat(Stream excelStream)
        {
            try
            {
                using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(excelStream, false);
                var wbPart = doc.WorkbookPart;
                if (wbPart == null) return false;

                var firstSheet = wbPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault();
                if (firstSheet?.Id?.Value == null) return false;

                var wsPart = (DocumentFormat.OpenXml.Packaging.WorksheetPart)wbPart.GetPartById(firstSheet.Id.Value);
                var sheetData = wsPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.SheetData>().FirstOrDefault();
                if (sheetData == null) return false;

                var sst = wbPart.SharedStringTablePart?.SharedStringTable;
                int rowNum = 0;

                foreach (var row in sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>())
                {
                    if (++rowNum > 10) break; // Probe top 10 rows
                    var cells = OxmlGetRowCells(row, sst);
                    
                    // A=1(Manifest), C=3(Area), G=7(Dock), K=11(PartNo), Q=17(Qty), Y=25(DateTime)
                    if (cells.TryGetValue(11, out var pn) && cells.TryGetValue(7, out var dock) && 
                        cells.TryGetValue(25, out var dateTimeVal))
                    {
                        var pnClean = pn.Trim();
                        // TMMIN Part No regex
                        if (Regex.IsMatch(pnClean, @"^\d{5}[A-Z0-9]{5,7}$") || Regex.IsMatch(pnClean, @"^\d{5}-[A-Z0-9]+"))
                        {
                            // If DateTime exists
                            if (!string.IsNullOrWhiteSpace(dateTimeVal))
                            {
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse format Excel ADM KEP Packing Instruction: DN | Part No | Qty Order (Pcs).
        /// Uses OpenXML SDK to tolerate embedded images/shapes that ClosedXML cannot open.
        /// </summary>
        public async Task<SmartImportResult> ParseAdmKepExcelAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            try
            {
                using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(stream, false);
                var wbPart = doc.WorkbookPart;
                if (wbPart == null)
                    return new SmartImportResult { Success = false, ErrorMessage = "File Excel tidak valid." };

                var firstSheet = wbPart.Workbook
                    .Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().FirstOrDefault();
                if (firstSheet?.Id?.Value == null)
                    return new SmartImportResult { Success = false, ErrorMessage = "File Excel tidak memiliki worksheet." };

                var wsPart = (DocumentFormat.OpenXml.Packaging.WorksheetPart)
                    wbPart.GetPartById(firstSheet.Id.Value);
                var sheetData = wsPart.Worksheet
                    .Descendants<DocumentFormat.OpenXml.Spreadsheet.SheetData>().FirstOrDefault();
                if (sheetData == null)
                    return new SmartImportResult { Success = false, ErrorMessage = "File Excel kosong." };

                var sst = wbPart.SharedStringTablePart?.SharedStringTable;
                var rows = sheetData.Elements<DocumentFormat.OpenXml.Spreadsheet.Row>().ToList();

                // â”€â”€ Scan metadata rows 1-15 for DELIVERY date (format: "17-Dec-2025") â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                DateTime? metaDeliveryDate = null;
                for (int i = 0; i < Math.Min(rows.Count, 20); i++)
                {
                    var cells = OxmlGetRowCells(rows[i], sst);
                    var vals = cells.Values.ToList();

                    // Look for a cell labelled "DELIVERY" and pick the date value in a nearby cell
                    bool seenDelivery = false;
                    foreach (var kvp in cells.OrderBy(k => k.Key))
                    {
                        var label = kvp.Value.Trim().ToUpper();
                        if (label.Contains("DELIVERY") || label == "DELIVERY DATE") { seenDelivery = true; continue; }
                        if (seenDelivery && TryParseAdmDate(kvp.Value, out var dDate))
                        { metaDeliveryDate = dDate; break; }
                    }
                    if (metaDeliveryDate.HasValue) break;

                    // Fallback: any cell value that looks like a date with "Dec/Jan/..." format
                    foreach (var val in vals)
                    {
                        var stripped = val.TrimStart(':', ' ').Trim();
                        if (stripped.Length >= 9 && TryParseAdmDate(stripped, out var d) && d.Year >= 2020)
                        {
                            metaDeliveryDate ??= d; // prefer first found date (ORDER or DELIVERY)
                        }
                    }
                }

                // â”€â”€ Find header row (DN + Part No + Qty Order) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                int headerListIdx = -1;
                int colDN = 0, colPartNo = 0, colQty = 0, colDate = 0;
                for (int i = 0; i < Math.Min(rows.Count, 50); i++)  // extended from 30 -> 50
                {
                    var cells = OxmlGetRowCells(rows[i], sst);
                    int tmpDN = 0, tmpPartNo = 0, tmpQtyPcs = 0, tmpQtyKanban = 0, tmpDate = 0;
                    bool foundDlvDate = false;
                    foreach (var kvp in cells)
                    {
                        var u = kvp.Value.Replace("\n", " ").Replace("\r", " ").Trim().ToUpper();
                        if (string.IsNullOrEmpty(u)) continue;

                        if (u.Contains("DLV. DATE") || u.Contains("DLV DATE") || u.Contains("DLV.DATE") || u.Contains("DELIVERY DATE"))
                        {
                            tmpDate = kvp.Key;
                            foundDlvDate = true;
                        }
                        else if (!foundDlvDate && tmpDate == 0 && (u == "DATE" || u.Contains("ORDER DATE") || u.Contains("DEL DATE") || u.Contains("TANGGAL")))
                        {
                            tmpDate = kvp.Key;
                        }

                        if (tmpDN == 0 && (u == "DN" || u.StartsWith("DN ") || u.Contains("DN NO") || u.Contains("D/N") ||
                            u.Contains("DN NUMBER") || u.Contains("DELIVERY NOTE") || u.Contains("MANIFEST") || u.Contains("ORDER NO") ||
                            u.Contains("DLV. NOTE") || u.Contains("DLV NOTE")))
                            tmpDN = kvp.Key;

                        if (tmpPartNo == 0 && (u.Contains("PART NO") || u.Contains("PART NUMBER") ||
                            u.Contains("MATERIAL NO") || u.Contains("MATERIAL NUMBER") || u == "PART"))
                            tmpPartNo = kvp.Key;

                        // Prefer Qty Order (Pcs) over (Kanban) - we want total pieces, not kanban count
                        if (tmpQtyPcs == 0 && (u.Contains("ORDER (PCS)") || u.Contains("QTY ORDER (PCS)") ||
                            u.Contains("ORDER QTY (PCS)") || u.Contains("QTY (PCS)") || u.Contains("ORDER  (PCS)") ||
                            u.Contains("QTY PCS") || u.Contains("TOTAL QTY")))
                            tmpQtyPcs = kvp.Key;

                        // Fallback: generic QTY ORDER or kanban count
                        if (tmpQtyKanban == 0 && (u.Contains("QTY ORDER") || u.Contains("ORDER QTY") || u == "QTY"))
                            tmpQtyKanban = kvp.Key;
                    }

                    int tmpQty = tmpQtyPcs > 0 ? tmpQtyPcs : tmpQtyKanban;  // prefer pcs count
                    if (tmpPartNo > 0 && tmpQty > 0)
                    {
                        headerListIdx = i;
                        colDN = tmpDN; colPartNo = tmpPartNo; colQty = tmpQty; colDate = tmpDate;
                        break;
                    }
                }

                if (headerListIdx < 0)
                    return new SmartImportResult
                    {
                        Success = false,
                        ErrorMessage = "Tidak dapat menemukan kolom Part No dan Qty di file. " +
                            "Pastikan format file benar (kolom: DN, Part No, QTY Order (Pcs))."
                    };

                _logger.LogInformation("ParseAdmKepExcel OpenXML: HeaderIdx={H}, colDN={D}, colPartNo={P}, colQty={Q}, colDate={DateCol}",
                    headerListIdx, colDN, colPartNo, colQty, colDate);

                if (headerListIdx >= 0)
                {
                    var headerCells = OxmlGetRowCells(rows[headerListIdx], sst);
                    var headerStr = string.Join(", ", headerCells.Select(kvp => $"[{kvp.Key}]: '{kvp.Value}'"));
                    _logger.LogInformation("ParseAdmKepExcel OpenXML Row {Idx} cells: {Cells}", headerListIdx, headerStr);

                    if (rows.Count > headerListIdx + 1)
                    {
                        var dataCells = OxmlGetRowCells(rows[headerListIdx + 1], sst);
                        var dataStr = string.Join(", ", dataCells.Select(kvp => $"[{kvp.Key}]: '{kvp.Value}'"));
                        _logger.LogInformation("ParseAdmKepExcel OpenXML Data Row {Idx} cells: {Cells}", headerListIdx + 1, dataStr);
                    }
                }

                // â”€â”€ Parse data rows â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var items = new List<ExtractedItem>();
                string lastDN = "";
                DateTime? lastDate = metaDeliveryDate;

                for (int i = headerListIdx + 1; i < rows.Count; i++)
                {
                    try
                    {
                        var cells = OxmlGetRowCells(rows[i], sst);
                        if (cells.Count == 0) continue;

                        // Part No
                        var partNo = colPartNo > 0 && cells.TryGetValue(colPartNo, out var pn)
                            ? pn.Trim() : "";
                        if (string.IsNullOrWhiteSpace(partNo)) continue;
                        if (partNo.ToUpper().StartsWith("TOTAL")) continue;
                        // skip re-header rows
                        if (partNo.ToUpper().Contains("PART NO")) continue;

                        // DN (fill-down)
                        if (colDN > 0 && cells.TryGetValue(colDN, out var dnVal) &&
                            !string.IsNullOrWhiteSpace(dnVal))
                            lastDN = dnVal.Trim();

                        // Date (fill-down from column, override metaDeliveryDate)
                        if (colDate > 0 && cells.TryGetValue(colDate, out var dateVal) &&
                            !string.IsNullOrWhiteSpace(dateVal) &&
                            TryParseAdmDate(dateVal, out var rowDate))
                            lastDate = rowDate;

                        // Qty
                        var qtyRaw = colQty > 0 && cells.TryGetValue(colQty, out var qv) ? qv.Trim() : "0";
                        int qty = ParseSmartQty(qtyRaw);
                        if (qty <= 0) continue;

                        items.Add(new ExtractedItem
                        {
                            Manifesting = lastDN,
                            PartNo = partNo,
                            Qty = qty,
                            ScheduledDate = lastDate
                        });
                    }
                    catch { continue; }
                }

                if (!items.Any())
                    return new SmartImportResult
                    {
                        Success = false,
                        ErrorMessage = "Tidak ada data yang berhasil dibaca. Pastikan kolom DN, Part No, dan Qty Order terisi."
                    };

                // Group by (DN, PartNo, Date)
                var grouped = items
                    .GroupBy(i => (DN: i.Manifesting?.ToUpper() ?? "", PN: i.PartNo?.ToUpper() ?? "", Date: i.ScheduledDate?.Date))
                    .Select(g => new ExtractedItem
                    {
                        Manifesting = g.First().Manifesting,
                        PartNo = g.First().PartNo,
                        Qty = g.Sum(x => x.Qty),
                        ScheduledDate = g.Key.Date
                    })
                    .ToList();

                var result = new SmartImportResult
                {
                    Success = true,
                    DetectedCustomerName = "ADM",
                    DetectedCustomerCode = "ADM",
                    DetectedManifest = grouped.FirstOrDefault(i => !string.IsNullOrEmpty(i.Manifesting))?.Manifesting,
                    DetectedScheduledDate = grouped.FirstOrDefault(i => i.ScheduledDate.HasValue)?.ScheduledDate,
                    ParserUsed = "ADM_KEP_NEW_EXCEL",
                    FileFormat = "xlsx",
                    Items = grouped
                };

                _logger.LogInformation("ParseAdmKepExcel: {N} items (after grouping), manifest='{M}', date={D}",
                    grouped.Count, result.DetectedManifest, result.DetectedScheduledDate);

                await MatchCustomerToDatabase(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ParseAdmKepExcelAsync error");
                return new SmartImportResult { Success = false, ErrorMessage = $"Error membaca file Excel ADM KEP: {ex.Message}" };
            }
        }

        #endregion

        #region === TMMIN Excel Parser ===
        
        public async Task<SmartImportResult> ParseTmminExcelAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            try
            {
                using var workbook = new XLWorkbook(stream);
                var ws = workbook.Worksheets.FirstOrDefault(w => (w.LastRowUsed()?.RowNumber() ?? 0) > 0);
                if (ws == null)
                    return new SmartImportResult { Success = false, ErrorMessage = "File Excel kosong." };

                var items = new List<ExtractedItem>();
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                
                // A=1, C=3, G=7, K=11, Q=17, Y=25
                for (int r = 1; r <= lastRow; r++)
                {
                    var partNo = ws.Cell(r, 11).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(partNo) || partNo.StartsWith("Part")) continue; // Skip empty or if they accidentally added header

                    int qty = 0;
                    var qtyStr = ws.Cell(r, 17).GetString().Trim();
                    if (!int.TryParse(qtyStr, out qty)) continue;
                    if (qty <= 0) continue; // Skip zero orderan

                    var manifest = ws.Cell(r, 1).GetString().Trim();
                    var area = ws.Cell(r, 3).GetString().Trim();
                    var dock = ws.Cell(r, 7).GetString().Trim();
                    
                    int excelKanban = 0;
                    var kbnStr = ws.Cell(r, 18).GetString().Trim();
                    int.TryParse(kbnStr, out excelKanban);
                    
                    // Datetime Parsing
                    DateTime? pickupDate = null;
                    TimeSpan? pickupTime = null;

                    if (ws.Cell(r, 25).TryGetValue<DateTime>(out var yDateTime))
                    {
                        pickupDate = yDateTime.Date;
                        pickupTime = yDateTime.TimeOfDay;
                    }
                    else
                    {
                        var yStr = ws.Cell(r, 25).GetString().Trim();
                        if (DateTime.TryParse(yStr, out var d)) 
                        {
                            pickupDate = d.Date;
                            pickupTime = d.TimeOfDay;
                        }
                    }

                    if (pickupDate == null) continue; // Need date

                    items.Add(new ExtractedItem
                    {
                        Manifesting = manifest,
                        PartNo = FormatTmminPartNo(partNo),
                        Qty = qty,
                        ExcelKanban = excelKanban,
                        Area = area,
                        DetectedDockCode = dock,
                        ScheduledDate = pickupDate,
                        TimeOfDay = pickupTime
                    });
                }

                if (!items.Any())
                    return new SmartImportResult { Success = false, ErrorMessage = "Tidak ada data order valid (Qty > 0) yang berhasil dibaca dari file TMMIN." };

                // TMMIN Dock & Cycle Matching against Master Customer using Column Y (TimeOfDay)
                string ResolveTmminDock(string? raw)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return "";
                    var d = raw.Trim().ToUpper();
                    if (d == "53" || d.Contains("53")) return "DOCK 53";
                    if (d == "43" || d.Contains("43")) return "DOCK 43";
                    if (d == "6I" || d.Contains("6I")) return "DOCK 6I";
                    if (d.StartsWith("7") || d.Contains("DOCK 7")) return "PBOD DOCK 7";
                    if (d.StartsWith("8") || d.Contains("DOCK 8")) return "PBOD DOCK 8";
                    if (d == "1E" || d.Contains("STR")) return "PBOD STR";
                    return d;
                }

                double GetTimeDiffMinutes(TimeSpan t1, TimeSpan t2)
                {
                    double diff = Math.Abs((t1 - t2).TotalMinutes);
                    if (diff > 12 * 60) diff = 24 * 60 - diff;
                    return diff;
                }

                var tmminCustomers = await _db.Customers
                    .Where(c => c.IsActive &&
                        ((c.CustomerCode != null && (c.CustomerCode.Contains("TMMIN") || c.CustomerCode.Contains("TOYOTA"))) ||
                         (c.CustomerName != null && (c.CustomerName.Contains("TMMIN") || c.CustomerName.Contains("TOYOTA") || c.CustomerName.Contains("DOCK") || c.CustomerName.Contains("PBOD")))))
                    .ToListAsync();

                foreach (var item in items)
                {
                    var cleanDock = ResolveTmminDock(item.DetectedDockCode);
                    item.DetectedDockCode = cleanDock;

                    if (!item.TimeOfDay.HasValue) continue;

                    var tod = item.TimeOfDay.Value;
                    var safeDock = new string(cleanDock.Where(char.IsLetterOrDigit).ToArray()).ToUpper();
                    var candidates = tmminCustomers.Where(c =>
                    {
                        var safeCust = new string((c.CustomerName ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpper();
                        return safeCust.Contains(safeDock) || safeDock.Contains(safeCust);
                    }).ToList();

                    Customer? bestCust = null;
                    double minDiff = double.MaxValue;

                    foreach (var c in candidates)
                    {
                        TimeSpan? etdTs = TimeSpan.TryParse(c.ETD?.Trim(), out var etd) ? etd : (TimeSpan?)null;
                        TimeSpan? pickupTs = TimeSpan.TryParse(c.Pickup?.Trim(), out var pic) ? pic : (TimeSpan?)null;
                        TimeSpan? dockingTs = TimeSpan.TryParse(c.Docking?.Trim(), out var doc) ? doc : (TimeSpan?)null;

                        double d = double.MaxValue;
                        if (etdTs.HasValue)
                        {
                            var diff = GetTimeDiffMinutes(tod, etdTs.Value);
                            if (diff < d) d = diff;
                        }
                        if (pickupTs.HasValue)
                        {
                            var diff = GetTimeDiffMinutes(tod, pickupTs.Value);
                            if (diff < d) d = diff;
                        }
                        if (dockingTs.HasValue)
                        {
                            var diff = GetTimeDiffMinutes(tod, dockingTs.Value);
                            if (diff < d) d = diff;
                        }

                        if (d < minDiff)
                        {
                            minDiff = d;
                            bestCust = c;
                        }
                    }

                    if (bestCust != null)
                    {
                        item.DetectedCycleCode = bestCust.Cycle;
                        item.MatchedCycle = bestCust.Cycle;
                        item.MatchedCustomerId = bestCust.CustomerId;
                        item.MatchedDockName = bestCust.CustomerName;
                        item.MatchedRoute = bestCust.Route;
                    }
                }

                // TMMIN KANBAN FIX: Pre-calculate the true SNP (QtyLot) for each PartNo + Dock BEFORE grouping!
                // This ensures fractional kanban rows do not skew the Master Data, and preserves the exact kanban ratio for the preview UI.
                // We MUST group by Dock as well because TMMIN packs the same part with different SNPs for different docks (e.g. Dock 43 vs Dock 53).
                var trueSnps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var validItems = items.Where(i => i.ExcelKanban > 0 && i.Qty > 0 && !string.IsNullOrWhiteSpace(i.PartNo)).ToList();
                var partGroups = validItems.GroupBy(i => i.PartNo.Trim() + "|" + (i.DetectedDockCode ?? ""));
                foreach (var pg in partGroups)
                {
                    int maxSnp = pg.Max(i => (int)Math.Ceiling((double)i.Qty / i.ExcelKanban));
                    if (maxSnp > 0)
                    {
                        trueSnps[pg.Key] = maxSnp;
                    }
                }

                // Group identical parts
                var grouped = items
                    .GroupBy(i => new { i.Manifesting, i.PartNo, i.ScheduledDate, i.MatchedCycle, i.DetectedCycleCode, i.DetectedDockCode, i.MatchedCustomerId, i.MatchedDockName, i.MatchedRoute })
                    .Select(g => new ExtractedItem
                    {
                        Manifesting = g.Key.Manifesting,
                        PartNo = g.Key.PartNo,
                        Qty = g.Sum(x => x.Qty),
                        ExcelKanban = g.Sum(x => x.ExcelKanban),
                        QtyLot = trueSnps.TryGetValue(g.Key.PartNo.Trim() + "|" + (g.Key.DetectedDockCode ?? ""), out int snp) ? snp : (int?)null,
                        ScheduledDate = g.Key.ScheduledDate,
                        MatchedCycle = g.Key.MatchedCycle,
                        DetectedCycleCode = g.Key.DetectedCycleCode,
                        DetectedDockCode = g.Key.DetectedDockCode,
                        MatchedCustomerId = g.Key.MatchedCustomerId,
                        MatchedDockName = g.Key.MatchedDockName,
                        MatchedRoute = g.Key.MatchedRoute,
                        TimeOfDay = g.Where(x => x.TimeOfDay.HasValue).OrderBy(x => x.TimeOfDay).FirstOrDefault()?.TimeOfDay
                    })
                    .ToList();

                var result = new SmartImportResult
                {
                    Success = true,
                    DetectedCustomerName = "TMMIN",
                    DetectedCustomerCode = "TMMIN",
                    DetectedManifest = grouped.FirstOrDefault(i => !string.IsNullOrEmpty(i.Manifesting))?.Manifesting,
                    DetectedScheduledDate = grouped.FirstOrDefault(i => i.ScheduledDate.HasValue)?.ScheduledDate,
                    ParserUsed = "TMMIN_EXCEL",
                    FileFormat = "xlsx",
                    Items = grouped
                };

                _logger.LogInformation("ParseTmminExcel: {N} items (after grouping), manifest='{M}', date={D}",
                    grouped.Count, result.DetectedManifest, result.DetectedScheduledDate);

                await MatchCustomerToDatabase(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ParseTmminExcelAsync error");
                return new SmartImportResult { Success = false, ErrorMessage = $"Error membaca file Excel TMMIN: {ex.Message}" };
            }
        }
        #endregion
    }
}
