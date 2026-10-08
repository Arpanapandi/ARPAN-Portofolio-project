using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OeeSystem.Data;
using OeeSystem.Models;
using OeeSystem.Models.ViewModels;

namespace OeeSystem.Controllers;

public class OperationController : Controller
{
    private readonly ApplicationDbContext _context;

    public OperationController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? machineId)
    {
        try
        {
            var vm = new OperationViewModel
            {
                Machines = await _context.Machines.OrderBy(m => m.Name).ToListAsync(),
                WorkOrders = await _context.WorkOrders
                    .Include(w => w.Product)
                    .Where(w => w.Status == WorkOrderStatus.Planned || w.Status == WorkOrderStatus.InProgress)
                    .OrderBy(w => w.OrderNumber)
                    .ToListAsync(),
                Operators = await _context.Users
                    .Where(u => u.Role == UserRole.Operator)
                    .OrderBy(u => u.Username)
                    .ToListAsync(),
                DowntimeReasons = await _context.DowntimeReasons
                    .OrderBy(r => r.Category)
                    .ThenBy(r => r.Description)
                    .ToListAsync(),
                NgTypes = await _context.NgTypes
                    .OrderBy(n => n.Code)
                    .ToListAsync(),
                SelectedMachineId = machineId
            };

            return View(vm);
        }
        catch (Exception ex)
        {
            // Log error untuk debugging
            TempData["OperationError"] = $"Error loading data: {ex.Message}";
            return View(new OperationViewModel
            {
                Machines = new List<Machine>(),
                WorkOrders = new List<WorkOrder>(),
                Operators = new List<User>(),
                DowntimeReasons = new List<DowntimeReason>(),
                NgTypes = new List<NgType>(),
                SelectedMachineId = machineId
            });
        }
    }

    // === Input Downtime ===

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDowntime(string machineId, int jobRunId, int reasonId, DateTime startTime, DateTime? endTime)
    {
        var job = await _context.JobRuns
            .Include(j => j.DowntimeEvents)
            .FirstOrDefaultAsync(j => j.Id == jobRunId && j.MachineId == machineId);

        if (job == null)
        {
            TempData["OperationError"] = "Job Run tidak ditemukan.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        // Validasi kombinasi mesin-reason berdasarkan mapping (jika ada)
        bool hasMapping = await _context.MachineDowntimeReasons
            .AnyAsync(md => md.MachineId == machineId);
        if (hasMapping)
        {
            bool allowed = await _context.MachineDowntimeReasons
                .AnyAsync(md => md.MachineId == machineId && md.DowntimeReasonId == reasonId);
            if (!allowed)
            {
                TempData["OperationError"] = "Downtime reason ini tidak diizinkan untuk mesin yang dipilih.";
                return RedirectToAction(nameof(Index), new { machineId });
            }
        }

        if (endTime.HasValue && endTime < startTime)
        {
            TempData["OperationError"] = "End Time tidak boleh lebih kecil dari Start Time.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        var downtime = new DowntimeEvent
        {
            JobRunId = job.Id,
            ReasonId = reasonId,
            StartTime = startTime,
            EndTime = endTime,
            DurationSeconds = endTime.HasValue
                ? (endTime.Value - startTime).TotalSeconds
                : 0
        };

        _context.DowntimeEvents.Add(downtime);
        await _context.SaveChangesAsync();

        TempData["OperationSuccess"] = "Downtime berhasil disimpan.";
        return RedirectToAction(nameof(Index), new { machineId });
    }

    [HttpGet]
    public async Task<IActionResult> GetDowntimeReasonsForMachine(string machineId)
    {
        // Ambil mapping khusus mesin
        var mappedReasons = await _context.MachineDowntimeReasons
            .Where(md => md.MachineId == machineId)
            .Select(md => new
            {
                DowntimeReasonId = md.DowntimeReasonId,
                md.DowntimeReason!.Category,
                md.DowntimeReason.Description
            })
            .ToListAsync();

        // Jika tidak ada mapping, fallback ke semua reason
        if (!mappedReasons.Any())
        {
            mappedReasons = await _context.DowntimeReasons
                .Select(r => new
                {
                    DowntimeReasonId = r.Id,
                    r.Category,
                    r.Description
                })
                .ToListAsync();
        }

        return Json(mappedReasons);
    }

    // === Tambah Produk ===

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(string name, string materialCode, string uom, string sloc, string plant, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(materialCode))
        {
            TempData["OperationError"] = "Nama dan Material Code produk wajib diisi.";
            return RedirectToAction(nameof(Index));
        }

        var defaultPlant = await _context.Plants.FirstOrDefaultAsync();
        var product = new Product
        {
            Name = name.Trim(),
            MaterialCode = materialCode.Trim(),
            UoM = string.IsNullOrWhiteSpace(uom) ? "" : uom.Trim(),
            SLOC = string.IsNullOrWhiteSpace(sloc) ? "" : sloc.Trim(),
            PlantId = defaultPlant?.Id ?? 0,
            ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim()
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        TempData["OperationSuccess"] = "Produk baru berhasil ditambahkan.";
        return RedirectToAction(nameof(Index));
    }

    // === Job Run ===

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateJobRun(
        string machineId,
        int workOrderId,
        int operatorId,
        DateTime startTime,
        DateTime? endTime)
    {
        var machine = await _context.Machines.FindAsync(machineId);
        if (machine == null)
        {
            TempData["OperationError"] = "Machine tidak ditemukan.";
            return RedirectToAction(nameof(Index));
        }

        if (endTime.HasValue && endTime < startTime)
        {
            TempData["OperationError"] = "End Time tidak boleh lebih kecil dari Start Time.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        // Tutup job run aktif sebelumnya (jika ada) untuk mesin ini
        var activeJob = await _context.JobRuns
            .Where(j => j.MachineId == machineId && j.EndTime == null)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync();

        if (activeJob != null)
        {
            activeJob.EndTime = startTime;
        }

        var newJob = new JobRun
        {
            MachineId = machineId,
            WorkOrderId = workOrderId,
            OperatorId = operatorId,
            StartTime = startTime,
            EndTime = endTime
        };

        _context.JobRuns.Add(newJob);
        await _context.SaveChangesAsync();

        TempData["OperationSuccess"] = "Job Run berhasil dibuat.";
        return RedirectToAction(nameof(Index), new { machineId });
    }

    // === Get Active Job Runs for Dropdown ===

    [HttpGet]
    public async Task<IActionResult> GetActiveJobRuns(string machineId)
    {
        var activeJobRuns = await _context.JobRuns
            .Include(j => j.WorkOrder)
                .ThenInclude(w => w.Product)
            .Include(j => j.Operator)
            .Where(j => j.MachineId == machineId && j.EndTime == null)
            .OrderByDescending(j => j.StartTime)
            .Select(j => new
            {
                Id = j.Id,
                WorkOrderNumber = j.WorkOrder != null ? j.WorkOrder.OrderNumber : "N/A",
                ProductName = j.WorkOrder != null && j.WorkOrder.Product != null ? j.WorkOrder.Product.Name : "N/A",
                OperatorName = j.Operator != null ? j.Operator.Username : "N/A",
                StartTime = j.StartTime
            })
            .ToListAsync();

        return Json(activeJobRuns);
    }

    // === Input Production Count ===

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProductionCount(
        string machineId,
        int jobRunId,
        int goodQty,
        int rejectQty,
        int? ngTypeId,
        string? rejectReason)
    {
        var job = await _context.JobRuns
            .Include(j => j.Machine)
            .FirstOrDefaultAsync(j => j.Id == jobRunId && j.MachineId == machineId);

        if (job == null)
        {
            TempData["OperationError"] = "Job Run tidak ditemukan.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        if (goodQty < 0 || rejectQty < 0)
        {
            TempData["OperationError"] = "Good Qty dan Reject Qty tidak boleh negatif.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        if (goodQty == 0 && rejectQty == 0)
        {
            TempData["OperationError"] = "Minimal salah satu (Good Qty atau Reject Qty) harus lebih dari 0.";
            return RedirectToAction(nameof(Index), new { machineId });
        }

        var productionCount = new ProductionCount
        {
            JobRunId = job.Id,
            Timestamp = DateTime.Now,
            GoodCount = goodQty,
            RejectCount = rejectQty,
            NgTypeId = ngTypeId,
            RejectReason = string.IsNullOrWhiteSpace(rejectReason) ? null : rejectReason.Trim()
        };

        _context.ProductionCounts.Add(productionCount);
        await _context.SaveChangesAsync();

        TempData["OperationSuccess"] = $"Production Count berhasil disimpan: {goodQty} Good, {rejectQty} Reject.";
        return RedirectToAction(nameof(Index), new { machineId });
    }
}


