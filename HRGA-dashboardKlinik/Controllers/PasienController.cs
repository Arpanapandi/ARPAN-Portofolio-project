using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Models;
using dashboardKlinik.Services;

namespace dashboardKlinik.Controllers
{
    [Authorize]
    public class PasienController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;

        public PasienController(ApplicationDbContext context, ActivityLogService activityLog)
        {
            _context = context;
            _activityLog = activityLog;
        }

        // GET: Pasien
        public async Task<IActionResult> Index(string searchString, string plant, string departemen, string jenisKelamin, int page = 1)
        {
            if (User.IsInRole("Guest")) return RedirectToAction("Index", "Home");

            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentPlant"] = plant;
            ViewData["CurrentDepartemen"] = departemen;
            ViewData["CurrentJenisKelamin"] = jenisKelamin;

            var query = _context.Pasien.Include(p => p.Kunjungan).AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(p =>
                    p.NamaPasien.Contains(searchString) ||
                    p.NPK.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(plant))
                query = query.Where(p => p.Plant == plant);

            if (!string.IsNullOrEmpty(departemen))
                query = query.Where(p => p.Departemen == departemen);

            if (!string.IsNullOrEmpty(jenisKelamin))
                query = query.Where(p => p.JenisKelamin == jenisKelamin);

            int pageSize = 10;
            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            ViewData["TotalPages"] = totalPages;
            ViewData["CurrentPage"] = page;
            ViewData["TotalItems"] = totalItems;

            // Dropdown data
            ViewData["Plants"] = await _context.Pasien.Select(p => p.Plant).Distinct().OrderBy(p => p).ToListAsync();
            ViewData["Departemens"] = await _context.Pasien.Select(p => p.Departemen).Distinct().OrderBy(d => d).ToListAsync();

            var pasien = await query
                .OrderByDescending(p => p.TanggalTerdaftar)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pasien);
        }

        // GET: Pasien/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var pasien = await _context.Pasien
                .Include(p => p.Kunjungan.OrderByDescending(k => k.TanggalKunjungan))
                .FirstOrDefaultAsync(p => p.Id == id);

            if (pasien == null) return NotFound();

            return View(pasien);
        }

        // GET: Pasien/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Pasien/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("NamaPasien,NPK,Plant,Departemen,JenisKelamin")] Pasien pasien)
        {
            if (ModelState.IsValid)
            {
                var cleanNpk = pasien.NPK?.Trim() ?? "";
                var exists = await _context.Pasien.AnyAsync(p => p.NPK == cleanNpk);
                if (exists)
                {
                    ModelState.AddModelError("NPK", $"NPK {cleanNpk} sudah terdaftar di sistem. NPK harus unik!");
                    return View(pasien);
                }

                pasien.TanggalTerdaftar = DateTime.Now;
                _context.Add(pasien);
                await _context.SaveChangesAsync();
                await _activityLog.LogAsync("CREATE", $"Pasien baru ditambahkan: {pasien.NamaPasien}", "Pasien", pasien.Id);
                TempData["SuccessMessage"] = "Data pasien berhasil ditambahkan!";
                return RedirectToAction(nameof(Index));
            }
            return View(pasien);
        }

        // GET: Pasien/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var pasien = await _context.Pasien.FindAsync(id);
            if (pasien == null) return NotFound();

            return View(pasien);
        }

        // POST: Pasien/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,NamaPasien,NPK,Plant,Departemen,JenisKelamin,TanggalTerdaftar")] Pasien pasien)
        {
            if (id != pasien.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(pasien);
                    await _context.SaveChangesAsync();
                    await _activityLog.LogAsync("UPDATE", $"Data pasien diupdate: {pasien.NamaPasien}", "Pasien", pasien.Id);
                    TempData["SuccessMessage"] = "Data pasien berhasil diupdate!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PasienExists(pasien.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(pasien);
        }

        // GET: Pasien/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.Id == id);
            if (pasien == null) return NotFound();

            return View(pasien);
        }

        // POST: Pasien/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var pasien = await _context.Pasien.FindAsync(id);
            if (pasien != null)
            {
                await _activityLog.LogAsync("DELETE", $"Data pasien dihapus: {pasien.NamaPasien}", "Pasien", id);
                _context.Pasien.Remove(pasien);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Data pasien berhasil dihapus!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool PasienExists(int id)
        {
            return _context.Pasien.Any(e => e.Id == id);
        }
    }
}
