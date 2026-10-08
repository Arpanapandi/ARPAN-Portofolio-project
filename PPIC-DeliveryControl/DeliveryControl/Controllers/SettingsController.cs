using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Pengaturan aplikasi (Cutoff, Header Mapping Import).
    /// </summary>
    [AuthorizeRoles("Super Admin")]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var cutoffSetting = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == "AutoSchedulerCutoffTime");

            var mappings = await _context.SmartImportHeaderMaps
                .OrderBy(m => m.CustomerName)
                .ThenBy(m => m.TargetField)
                .ToListAsync();

            var twoPointSetting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "TwoPointCheck_CustomerCodes");

            if (twoPointSetting == null)
            {
                twoPointSetting = new SystemSetting
                {
                    Key = "TwoPointCheck_CustomerCodes",
                    Value = "AHM,MKM,KTB,TJY,MII,MAJ",
                    Description = "Daftar Kode Customer yang menggunakan Two Point Check (Rak+Label) dipisahkan koma."
                };
                _context.SystemSettings.Add(twoPointSetting);
                await _context.SaveChangesAsync();
            }

            var customerCodesFromMaster = await _context.Customers
                .AsNoTracking()
                .Where(c => !string.IsNullOrEmpty(c.CustomerCode))
                .Select(c => c.CustomerCode)
                .Distinct()
                .ToListAsync();

            var customerCodesFromItems = await _context.Items
                .AsNoTracking()
                .Where(i => !string.IsNullOrEmpty(i.Customer))
                .Select(i => i.Customer!)
                .Distinct()
                .ToListAsync();

            // Gabungkan dan urutkan
            var allCustomers = customerCodesFromMaster
                .Union(customerCodesFromItems)
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            // Tambahkan force-add untuk customer penting jika masih belum ada
            var essentialCodes = new[] { "MAJ", "MKM", "KTB", "TJY", "MII", "AHM", "ADM" };
            foreach (var code in essentialCodes)
            {
                if (!allCustomers.Any(c => c.Equals(code, StringComparison.OrdinalIgnoreCase)))
                {
                    allCustomers.Add(code);
                }
            }

            // Exclude dummy data if desired
            allCustomers = allCustomers
                .Where(c => !c.StartsWith("CUST00", StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c)
                .ToList();

            var model = new SettingsPageViewModel
            {
                AllCustomers = allCustomers,
                AutoScheduler = new AutoSchedulerSettingViewModel
                {
                    CutoffTime = cutoffSetting?.Value ?? "18:00"
                },
                HeaderMappings = mappings,
                TwoPointCheckCustomerCodes = twoPointSetting.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SettingsPageViewModel model)
        {
            if (!TimeSpan.TryParse(model.AutoScheduler.CutoffTime, out _))
            {
                ModelState.AddModelError("AutoScheduler.CutoffTime", "Format waktu harus HH:mm, contoh 18:00");
            }

            if (!ModelState.IsValid)
            {
                model.HeaderMappings = await _context.SmartImportHeaderMaps
                    .OrderBy(m => m.CustomerName)
                    .ThenBy(m => m.TargetField)
                    .ToListAsync();
                return View(model);
            }

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoSchedulerCutoffTime");

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    Key = "AutoSchedulerCutoffTime",
                    Value = model.AutoScheduler.CutoffTime ?? "18:00",
                    Description = "Jam cutoff auto generate jadwal (format HH:mm)"
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = model.AutoScheduler.CutoffTime ?? "18:00";
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Jam cutoff AutoScheduler disimpan: {model.AutoScheduler.CutoffTime}";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddHeaderMapping(string customerName, string targetField, string keywords)
        {
            if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(targetField) || string.IsNullOrWhiteSpace(keywords))
            {
                TempData["ErrorMessage"] = "Semua field mapping harus diisi!";
                return RedirectToAction(nameof(Index));
            }

            var mapping = new SmartImportHeaderMap
            {
                CustomerName = customerName.Trim().ToUpper(),
                TargetField = targetField.Trim(),
                HeaderKeywords = keywords.Trim(),
                CreatedDate = DateTime.Now
            };

            _context.SmartImportHeaderMaps.Add(mapping);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Mapping header '{targetField}' untuk '{customerName}' berhasil ditambahkan.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHeaderMapping(int id)
        {
            var mapping = await _context.SmartImportHeaderMaps.FindAsync(id);
            if (mapping != null)
            {
                _context.SmartImportHeaderMaps.Remove(mapping);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Mapping header berhasil dihapus.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateScanMethods(string[] selectedCustomers)
        {
            var codes = string.Join(",", selectedCustomers ?? Array.Empty<string>());
            
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "TwoPointCheck_CustomerCodes");

            if (setting == null)
            {
                setting = new SystemSetting
                {
                    Key = "TwoPointCheck_CustomerCodes",
                    Value = codes,
                    Description = "Daftar Kode Customer yang menggunakan Two Point Check (Rak+Label) dipisahkan koma."
                };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = codes;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Konfigurasi metode scan preparation diperbarui.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class SettingsPageViewModel
    {
        public AutoSchedulerSettingViewModel AutoScheduler { get; set; } = new();
        public List<SmartImportHeaderMap> HeaderMappings { get; set; } = new();
        
        // Scan Method Settings
        public List<string> AllCustomers { get; set; } = new();
        public List<string> TwoPointCheckCustomerCodes { get; set; } = new();
    }
}
