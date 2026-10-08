using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using System.Text.Json;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace DeliveryControl.Controllers
{
    [DeliveryControl.Filters.AuthorizeRoles("Admin", "Super Admin")]
    public class ScheduleTemplatesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ScheduleTemplatesController> _logger;

        public ScheduleTemplatesController(ApplicationDbContext context, ILogger<ScheduleTemplatesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: ScheduleTemplates
        public async Task<IActionResult> Index()
        {
            var templates = await _context.ScheduleTemplates
                .Include(t => t.Mappings)
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();
            return View(templates);
        }

        // GET: ScheduleTemplates/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var template = await _context.ScheduleTemplates
                .Include(t => t.Mappings)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (template == null) return NotFound();

            return View(template);
        }

        // GET: ScheduleTemplates/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: ScheduleTemplates/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ScheduleTemplate template)
        {
            if (ModelState.IsValid)
            {
                template.CreatedDate = DateTime.Now;
                template.CreatedBy = User.Identity?.Name ?? "System";
                _context.Add(template);
                await _context.SaveChangesAsync();
                
                // Redirect to Mapping Wizard
                return RedirectToAction(nameof(Mapping), new { id = template.Id });
            }
            return View(template);
        }

        // GET: ScheduleTemplates/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var template = await _context.ScheduleTemplates.FindAsync(id);
            if (template == null) return NotFound();
            return View(template);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ScheduleTemplate template)
        {
            if (id != template.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(template);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TemplateExists(template.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(template);
        }

        // GET: ScheduleTemplates/Mapping/5
        public async Task<IActionResult> Mapping(int? id)
        {
            if (id == null) return NotFound();

            var template = await _context.ScheduleTemplates
                .Include(t => t.Mappings)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (template == null) return NotFound();

            // Sediakan list field target sistem yang utama (termasuk Customer untuk deteksi otomatis)
            ViewBag.TargetFields = new List<string> { "Customer", "Manifest", "PartNo", "Qty", "Date" };

            return View(template);
        }

        [HttpPost]
        public async Task<IActionResult> UploadSample(int templateId, IFormFile sampleFile)
        {
            if (sampleFile == null || sampleFile.Length == 0)
                return Json(new { success = false, message = "File tidak valid." });

            try
            {
                List<string> headers = new List<string>();
                var ext = Path.GetExtension(sampleFile.FileName).ToLower();

                if (ext == ".xlsx" || ext == ".xls")
                {
                    using var stream = sampleFile.OpenReadStream();
                    using var workbook = new XLWorkbook(stream);
                    var ws = workbook.Worksheets.FirstOrDefault();
                    if (ws != null)
                    {
                        var firstRow = ws.FirstRowUsed();
                        if (firstRow != null)
                        {
                            foreach (var cell in firstRow.Cells())
                                headers.Add(cell.Value.ToString().Trim());
                        }
                    }
                }
                else if (ext == ".pdf")
                {
                    using var stream = sampleFile.OpenReadStream();
                    using var pdf = PdfDocument.Open(stream);
                    var page = pdf.GetPages().FirstOrDefault();
                    if (page != null)
                    {
                        // Urutkan kata dari atas ke bawah, lalu kiri ke kanan
                        var words = page.GetWords()
                            .OrderByDescending(w => w.BoundingBox.Top)
                            .ThenBy(w => w.BoundingBox.Left)
                            .ToList();
                            
                        var phrases = new List<string>();
                        if (words.Any())
                        {
                            var currentLineText = "";
                            double? currentTop = null;
                            const double tolerance = 5.0; // Toleransi ditingkatkan agar baris lebih stabil

                            double? currentRight = null;
                            foreach (var w in words)
                            {
                                if (currentTop == null || Math.Abs(w.BoundingBox.Top - currentTop.Value) > tolerance)
                                {
                                    // Baris baru, proses baris sebelumnya
                                    if (!string.IsNullOrEmpty(currentLineText))
                                        ProcessLine(currentLineText.Trim(), phrases);
                                    
                                    currentLineText = "";
                                    currentTop = w.BoundingBox.Top;
                                    currentRight = null;
                                }

                                // Jika jarak horizontal dengan kata sebelumnya > 15 pixel, anggap pemisah kolom
                                if (currentRight != null && (w.BoundingBox.Left - currentRight.Value) > 15.0)
                                {
                                    currentLineText += "   "; // Berikan spasi lebar sebagai penanda kolom
                                }

                                currentLineText += w.Text + " ";
                                currentRight = w.BoundingBox.Right;
                            }
                            if (!string.IsNullOrEmpty(currentLineText))
                                ProcessLine(currentLineText.Trim(), phrases);
                        }

                        headers = phrases.Where(p => p.Length > 1).Distinct().Take(120).ToList();
                    }
                }

                return Json(new { success = true, headers });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private void ProcessLine(string line, List<string> phrases)
        {
            // 1. Simpan baris utuh jika tidak terlalu panjang (sangat penting untuk label seperti "DN Date : ...")
            if (line.Length < 60) phrases.Add(line);

            // 2. Deteksi Label sebelum Titik Dua
            if (line.Contains(":"))
            {
                var label = line.Split(':')[0].Trim();
                if (label.Length > 1) phrases.Add(label);
            }

            // 3. Pecah berdasarkan spasi lebar (Pemisah kolom)
            var segments = line.Split(new[] { "   ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in segments)
            {
                var clean = s.Trim();
                if (clean.Length > 1 && clean.Length < 50) 
                {
                    phrases.Add(clean);
                    
                    // Jika mengandung "Part No" tanpa spasi (beberapa PDF formatter unik)
                    if (clean.ToUpper().Contains("PARTNO")) phrases.Add("Part No");
                }
            }
            
            // 4. Heuristik Penggabungan: Pastikan istilah kunci tidak terpecah
            var upper = line.ToUpper();
            if (upper.Contains("PART") && upper.Contains("NO")) phrases.Add("Part No");
            if (upper.Contains("DN") && upper.Contains("NO")) phrases.Add("DN No");
            if (upper.Contains("DN") && upper.Contains("DATE")) phrases.Add("DN Date");
            if (upper.Contains("NOMOR") && upper.Contains("PART")) phrases.Add("Nomor Part");
            if (upper.Contains("QTY") && upper.Contains("ORDER")) phrases.Add("Qty Order");
            if (upper.Contains("MANIFEST") && upper.Contains("NO")) phrases.Add("Manifest No");
        }

        [HttpPost]
        public async Task<IActionResult> SaveMappings(int templateId, string mappingsJson)
        {
            try
            {
                var template = await _context.ScheduleTemplates
                    .Include(t => t.Mappings)
                    .FirstOrDefaultAsync(t => t.Id == templateId);

                if (template == null) return Json(new { success = false, message = "Template tidak ditemukan." });

                var newMappings = JsonSerializer.Deserialize<List<ScheduleTemplateMapping>>(mappingsJson);
                if (newMappings == null) return Json(new { success = false, message = "Format data mapping tidak valid." });

                // Hapus mapping lama
                _context.ScheduleTemplateMappings.RemoveRange(template.Mappings);
                
                // Tambahkan mapping baru
                foreach (var m in newMappings)
                {
                    m.TemplateId = templateId;
                    _context.ScheduleTemplateMappings.Add(m);
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Mapping berhasil disimpan." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: ScheduleTemplates/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var template = await _context.ScheduleTemplates
                .FirstOrDefaultAsync(m => m.Id == id);
            if (template == null) return NotFound();

            return View(template);
        }

        // POST: ScheduleTemplates/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var template = await _context.ScheduleTemplates.FindAsync(id);
            if (template != null)
            {
                _context.ScheduleTemplates.Remove(template);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TemplateExists(int id)
        {
            return _context.ScheduleTemplates.Any(e => e.Id == id);
        }
    }
}
