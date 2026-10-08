using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using OeeSystem.Data;
using OeeSystem.Models;
using OeeSystem.Models.ViewModels;
using OeeSystem.Hubs;

namespace OeeSystem.Controllers;

public class OperatorController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<OeeHub> _hubContext;

    public OperatorController(ApplicationDbContext context, IHubContext<OeeHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    public async Task<IActionResult> Index(string machineId)
    {
        var now = DateTime.Now;

        // ✅ PERBAIKAN: Gunakan AsNoTracking() untuk memastikan data fresh dari database
        var machine = await _context.Machines
            .AsNoTracking()
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.WorkOrder)
                    .ThenInclude(w => w.Product)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.Operator)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
                    .ThenInclude(d => d.Reason)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.ProductionCounts)
            .FirstOrDefaultAsync(m => m.Id == machineId);

        if (machine == null)
        {
            return NotFound();
        }

        var activeJob = machine.JobRuns
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefault(j => j.EndTime == null);

        var allCounts = activeJob?.ProductionCounts ?? Array.Empty<ProductionCount>();

        var openDowntime = activeJob?.DowntimeEvents
            .OrderByDescending(d => d.StartTime)
            .FirstOrDefault(d => d.EndTime == null);

        DateTime lastChangeTime = activeJob?.StartTime ?? machine.JobRuns
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefault()?.StartTime ?? now;

        if (openDowntime != null)
        {
            lastChangeTime = openDowntime.StartTime;
        }

        var plannedRests = await _context.DowntimeReasons
            .Where(r => r.Category == "Planned")
            .ToListAsync();

        var restReason = plannedRests
            .FirstOrDefault(r => r.Description.Contains("Rest", StringComparison.OrdinalIgnoreCase))
                         ?? plannedRests.FirstOrDefault();

        var lineStops = await _context.DowntimeReasons
            .Where(r => r.Category == "Unplanned")
            .OrderBy(r => r.Description)
            .ToListAsync();

        var ngTypes = await _context.NgTypes
            .OrderBy(n => n.Code)
            .ToListAsync();

        var vm = new OperatorViewModel
        {
            MachineId = machine.Id,
            MachineName = machine.Name,
            OperatorName = activeJob?.Operator?.Username,
            ProductName = activeJob?.WorkOrder?.Product?.Name,
            ProductImageUrl = activeJob?.WorkOrder?.Product?.ImageUrl,
            WorkOrderNumber = activeJob?.WorkOrder?.OrderNumber,
            TargetQuantity = activeJob?.WorkOrder?.TargetQuantity ?? 0,
            TotalGood = allCounts.Sum(c => c.GoodCount),
            TotalReject = allCounts.Sum(c => c.RejectCount),
            HasActiveJob = activeJob != null,
            HasActiveDowntime = openDowntime != null,
            ActiveDowntimeDescription = openDowntime?.Reason?.Description,
            SinceLastChange = now - lastChangeTime,
            MachineStatus = machine.Status, // Status dari Admin (Aktif/Tidak Aktif)
            LineStopReasons = lineStops,
            RestReason = restReason,
            NgTypes = ngTypes
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string machineId, string? returnUrl = null)
    {
        var now = DateTime.Now;

        // Cek apakah sudah ada job aktif
        var existingJob = await _context.JobRuns
            .Include(j => j.DowntimeEvents)
            .Include(j => j.Machine)
            .Where(j => j.MachineId == machineId)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync(j => j.EndTime == null);

        if (existingJob != null)
        {
            // ✅ PERBAIKAN: Jika ada downtime aktif, end downtime terlebih dahulu
            var openDowntime = existingJob.DowntimeEvents
                .OrderByDescending(d => d.StartTime)
                .FirstOrDefault(d => d.EndTime == null);
                
            if (openDowntime != null)
            {
                // End downtime yang aktif
                openDowntime.EndTime = now;
                openDowntime.DurationSeconds = (int)(now - openDowntime.StartTime).TotalSeconds;
                
                await _context.SaveChangesAsync();
                
                // Broadcast SignalR update untuk downtime ended
                await _hubContext.Clients.All.SendAsync("OeeUpdated", new
                {
                    Type = "DowntimeEnded",
                    MachineId = machineId,
                    MachineName = existingJob.Machine?.Name,
                    Message = $"Downtime berakhir pada mesin {existingJob.Machine?.Name}",
                    Timestamp = now,
                    RefreshOperatorData = true
                });
                
                // Setelah end downtime, machine akan running (job sudah aktif, downtime sudah di-end)
                // Redirect kembali ke operator view
                if (!string.IsNullOrEmpty(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction(nameof(Index), new { machineId });
            }
            else
            {
                // Sudah ada job aktif TANPA downtime, tidak bisa start lagi
                TempData["OperationError"] = "Sudah ada job aktif. Tidak bisa start job baru.";
                if (!string.IsNullOrEmpty(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction(nameof(Index), new { machineId });
            }
        }

        // Tidak ada job aktif, START job baru
        var activeWorkOrder = await _context.WorkOrders
            .Include(w => w.Product)
            .Where(w => w.Status == WorkOrderStatus.InProgress)
            .FirstOrDefaultAsync();

        if (activeWorkOrder == null)
        {
            TempData["OperationError"] = "Tidak ada Work Order aktif. Silakan buat Work Order terlebih dahulu.";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index), new { machineId });
        }

        var operatorUser = await _context.Users
            .Where(u => u.Role == UserRole.Operator)
            .FirstOrDefaultAsync();

        var machine = await _context.Machines.FindAsync(machineId);
        var newJob = new JobRun
        {
            MachineId = machineId,
            WorkOrderId = activeWorkOrder.Id,
            OperatorId = operatorUser?.Id ?? 0,
            StartTime = now,
            EndTime = null
        };

        _context.JobRuns.Add(newJob);
        await _context.SaveChangesAsync();

        // Reload job dengan Include untuk memastikan data lengkap
        var reloadedJob = await _context.JobRuns
            .Include(j => j.WorkOrder)
                .ThenInclude(w => w.Product)
            .Include(j => j.Operator)
            .Include(j => j.DowntimeEvents)
            .Include(j => j.ProductionCounts)
            .FirstOrDefaultAsync(j => j.Id == newJob.Id);

        // Broadcast SignalR update
        await _hubContext.Clients.All.SendAsync("OeeUpdated", new
        {
            Type = "MachineStarted",
            MachineId = machineId,
            MachineName = machine?.Name,
            Message = $"Mesin {machine?.Name} telah dimulai",
            Timestamp = now,
            RefreshTimeMetrics = true, // Flag untuk trigger refresh Time Metrics di OEE View
            RefreshOperatorData = true // Flag untuk trigger refresh Operator Data
        });

        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index), new { machineId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rest(string machineId, int reasonId, string? returnUrl = null)
    {
        return await StartDowntime(machineId, reasonId, returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LineStop(string machineId, int reasonId, string? returnUrl = null)
    {
        return await StartDowntime(machineId, reasonId, returnUrl);
    }

    private async Task<IActionResult> StartDowntime(string machineId, int reasonId, string? returnUrl = null)
    {
        var now = DateTime.Now;

        var job = await _context.JobRuns
            .Include(j => j.DowntimeEvents)
            .Include(j => j.Machine)
            .Where(j => j.MachineId == machineId)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync(j => j.EndTime == null);

        if (job == null)
        {
            TempData["OperationError"] = "Tidak ada job aktif. Silakan start RUNNING PROCESS terlebih dahulu.";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index), new { machineId });
        }

        // Cek apakah sudah ada downtime aktif
        var openDowntime = job.DowntimeEvents.FirstOrDefault(d => d.EndTime == null);
        if (openDowntime != null)
        {
            TempData["OperationError"] = "Sudah ada downtime aktif. Tidak bisa start downtime baru.";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index), new { machineId });
        }

        // Tidak ada downtime aktif, START downtime baru
        var reason = await _context.DowntimeReasons.FindAsync(reasonId);
        if (reason == null)
        {
            TempData["OperationError"] = "Alasan downtime tidak ditemukan.";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index), new { machineId });
        }

        var newDowntime = new DowntimeEvent
        {
            JobRunId = job.Id,
            ReasonId = reasonId,
            StartTime = now,
            EndTime = null,
            DurationSeconds = 0
        };

        _context.DowntimeEvents.Add(newDowntime);
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("OeeUpdated", new
        {
            Type = "DowntimeStarted",
            MachineId = machineId,
            MachineName = job.Machine?.Name,
            Reason = reason.Description,
            Category = reason.Category,
            Message = $"Downtime: {reason.Description} pada mesin {job.Machine?.Name}",
            Timestamp = now,
            RefreshTimeMetrics = true, // Flag untuk trigger refresh Time Metrics di OEE View
            RefreshOperatorData = true // ✅ TAMBAHKAN untuk trigger refresh Operator Data
        });

        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index), new { machineId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NoLoading(string machineId, string? returnUrl = null)
    {
        var now = DateTime.Now;

        // Cari job aktif untuk mesin ini
        var activeJob = await _context.JobRuns
            .Include(j => j.Machine)
            .Include(j => j.DowntimeEvents)
            .Where(j => j.MachineId == machineId)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync(j => j.EndTime == null);

        if (activeJob == null)
        {
            TempData["OperationError"] = "Tidak ada job aktif. Tidak bisa set NO LOADING.";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(nameof(Index), new { machineId });
        }

        // Cek apakah ada downtime aktif, jika ada end terlebih dahulu
        var openDowntime = activeJob.DowntimeEvents
            .OrderByDescending(d => d.StartTime)
            .FirstOrDefault(d => d.EndTime == null);

        if (openDowntime != null)
        {
            // End downtime yang aktif
            openDowntime.EndTime = now;
            openDowntime.DurationSeconds = (int)(now - openDowntime.StartTime).TotalSeconds;
        }

        // End job aktif (NO LOADING = menghentikan mesin)
        activeJob.EndTime = now;

        await _context.SaveChangesAsync();

        // Broadcast SignalR update
        await _hubContext.Clients.All.SendAsync("OeeUpdated", new
        {
            Type = "NoLoadingStarted",
            MachineId = machineId,
            MachineName = activeJob.Machine?.Name,
            Message = $"NO LOADING: Mesin {activeJob.Machine?.Name} dihentikan (tidak masuk perhitungan OEE)",
            Timestamp = now,
            RefreshTimeMetrics = true, // Flag untuk trigger refresh Time Metrics di OEE View
            RefreshOperatorData = true // Flag untuk trigger refresh Operator Data
        });

        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction(nameof(Index), new { machineId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuantity(
        string machineId,
        int goodQty,
        int rejectQty,
        string? rejectReason,
        int? ngTypeId = null,
        string? injection = null,
        string? returnUrl = null)
    {
        var now = DateTime.Now;

        // Validasi input
        if (goodQty < 0 || rejectQty < 0)
        {
            TempData["OperationError"] = "Quantity tidak boleh negatif";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("OeeDetail", "Machine", new { id = machineId });
        }

        if (goodQty == 0 && rejectQty == 0)
        {
            TempData["OperationError"] = "Minimal harus ada input quantity (good atau reject)";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("OeeDetail", "Machine", new { id = machineId });
        }

        var job = await _context.JobRuns
            .Include(j => j.Machine)
            .Include(j => j.WorkOrder)
                .ThenInclude(w => w.Product)
            .Where(j => j.MachineId == machineId)
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefaultAsync(j => j.EndTime == null);

        if (job == null)
        {
            TempData["OperationError"] = "Tidak ada job run aktif untuk mesin ini";
            if (!string.IsNullOrEmpty(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("OeeDetail", "Machine", new { id = machineId });
        }

        if (goodQty > 0 || rejectQty > 0)
        {
            var count = new ProductionCount
            {
                JobRunId = job.Id,
                Timestamp = now,
                GoodCount = goodQty,
                RejectCount = rejectQty,
                RejectReason = string.IsNullOrWhiteSpace(rejectReason) ? null : rejectReason,
                NgTypeId = ngTypeId,
                InjectionGroup = string.IsNullOrWhiteSpace(injection) ? null : injection.Trim().ToLower()
            };

            _context.ProductionCounts.Add(count);
            await _context.SaveChangesAsync();

            // Broadcast SignalR update
            await _hubContext.Clients.All.SendAsync("OeeUpdated", new
            {
                Type = "ProductionCountAdded",
                MachineId = machineId,
                MachineName = job.Machine?.Name,
                ProductName = job.WorkOrder?.Product?.Name,
                GoodCount = goodQty,
                RejectCount = rejectQty,
                Message = $"Produksi: +{goodQty} Good, +{rejectQty} Reject pada mesin {job.Machine?.Name}",
                Timestamp = now
            });
        }

        // Jika ada returnUrl (dari OEE Detail), utamakan redirect ke sana
        if (!string.IsNullOrEmpty(returnUrl))
            return Redirect(returnUrl);

        // Fallback: kembali ke OEE Detail untuk mesin ini
        return RedirectToAction("OeeDetail", "Machine", new { id = machineId });
    }

    private static TimeSpan GetOverlap(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd)
    {
        var overlapStart = start < windowStart ? windowStart : start;
        var overlapEnd = end > windowEnd ? windowEnd : end;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }

    private static (DateTime Start, DateTime End, DateTime ShiftDate, string Code, string Key) ResolveShiftWindow(DateTime now, DateTime? shiftDate, string? shiftCode)
    {
        var shiftTemplates = new List<(string Code, TimeSpan Start, TimeSpan End)>
        {
            ("A", new TimeSpan(6, 0, 0), new TimeSpan(14, 0, 0)),
            ("B", new TimeSpan(14, 0, 0), new TimeSpan(22, 0, 0)),
            ("C", new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0))
        };

        var code = string.IsNullOrWhiteSpace(shiftCode)
            ? null
            : shiftCode!.Trim().ToUpperInvariant();

        // Tentukan kode shift jika tidak dikirim
        if (code == null)
        {
            var tod = now.TimeOfDay;
            if (tod >= shiftTemplates[0].Start && tod < shiftTemplates[0].End)
            {
                code = "A";
            }
            else if (tod >= shiftTemplates[1].Start && tod < shiftTemplates[1].End)
            {
                code = "B";
            }
            else
            {
                code = "C";
            }
        }

        var template = shiftTemplates.FirstOrDefault(s => s.Code == code);
        if (template == default)
        {
            template = shiftTemplates[0];
            code = template.Code;
        }

        var baseDate = shiftDate?.Date ?? now.Date;

        // Jika tidak ada shiftDate dan shift C berjalan lewat tengah malam, tarik ke hari sebelumnya
        if (!shiftDate.HasValue && code == "C" && now.TimeOfDay < template.End)
        {
            baseDate = baseDate.AddDays(-1);
        }

        var start = baseDate.Add(template.Start);
        var end = template.End > template.Start
            ? baseDate.Add(template.End)
            : baseDate.AddDays(1).Add(template.End);

        var key = $"{baseDate:yyyy-MM-dd}|{code}";
        return (start, end, baseDate, code!, key);
    }

    [HttpGet]
    public async Task<IActionResult> GetOperatorData(string machineId)
    {
        // ✅ PERBAIKAN: Validasi machineId
        if (string.IsNullOrEmpty(machineId))
        {
            return Json(new { error = "machineId is required" });
        }
        
        // ✅ PERBAIKAN: Log untuk debugging
        System.Diagnostics.Debug.WriteLine($"GetOperatorData called with machineId: '{machineId}'");
        
        var now = DateTime.Now;

        // ✅ PERBAIKAN: Gunakan AsNoTracking() untuk memastikan data fresh dari database
        var machine = await _context.Machines
            .AsNoTracking()
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.WorkOrder)
                    .ThenInclude(w => w.Product)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.ProductionCounts)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
                    .ThenInclude(d => d.Reason)
            .FirstOrDefaultAsync(m => m.Id == machineId);

        if (machine == null)
        {
            // ✅ PERBAIKAN: Return JSON error instead of NotFound untuk AJAX call
            return Json(new { 
                error = $"Machine with ID '{machineId}' not found",
                machineId = machineId 
            });
        }

        var activeJob = machine.JobRuns
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefault(j => j.EndTime == null);

        var allCounts = activeJob?.ProductionCounts ?? Array.Empty<ProductionCount>();

        var openDowntime = activeJob?.DowntimeEvents
            .OrderByDescending(d => d.StartTime)
            .FirstOrDefault(d => d.EndTime == null);

        // ✅ PERBAIKAN: Hitung LastStatusChangeTime yang sinkron dengan Operating Time untuk OEE
        // - Jika ada downtime aktif (REST/LINE STOP) -> pakai StartTime downtime (RESET ke 00:00:00)
        // - Jika sedang RUNNING (tidak ada downtime aktif) -> hitung dari StartTime job dikurangi total downtime
        DateTime lastStatusChangeTime;
        int sinceLastChangeSeconds;

        if (openDowntime != null)
        {
            // Sedang REST / LINE STOP: RESET durasi ke 00:00:00 (mulai hitung dari awal downtime)
            lastStatusChangeTime = openDowntime.StartTime;
            sinceLastChangeSeconds = (int)(now - openDowntime.StartTime).TotalSeconds;
        }
        else if (activeJob != null)
        {
            // Sedang RUNNING: Hitung durasi yang sinkron dengan Operating Time (untuk OEE)
            // Operating Time = JobRun duration - Unplanned Downtime duration (waktu running murni)
            
            // Hitung JobRun duration
            var jobDuration = (now - activeJob.StartTime).TotalSeconds;
            
            // Hitung total Unplanned downtime yang sudah selesai di job ini
            var totalUnplannedDowntimeSeconds = activeJob.DowntimeEvents
                .Where(d => d.EndTime.HasValue && d.Reason?.Category == "Unplanned")
                .Sum(d => d.DurationSeconds > 0 
                    ? d.DurationSeconds 
                    : (d.EndTime!.Value - d.StartTime).TotalSeconds);
            
            // Operating Time murni = JobRun duration - Unplanned Downtime duration (sinkron dengan perhitungan OEE)
            var operatingTimeSeconds = jobDuration - totalUnplannedDowntimeSeconds;
            sinceLastChangeSeconds = Math.Max(0, (int)operatingTimeSeconds);
            
            // LastStatusChangeTime untuk display (backward compatibility)
            lastStatusChangeTime = now.AddSeconds(-sinceLastChangeSeconds);
        }
        else
        {
            // Tidak ada job aktif: fallback
            lastStatusChangeTime = machine.JobRuns
                .OrderByDescending(j => j.StartTime)
                .FirstOrDefault()?.StartTime ?? now;
            sinceLastChangeSeconds = 0;
        }

        // Keep lastChangeTime untuk backward compatibility
        DateTime lastChangeTime = lastStatusChangeTime;

        var totalGood = allCounts.Sum(c => c.GoodCount);
        var totalReject = allCounts.Sum(c => c.RejectCount);
        var targetQuantity = activeJob?.WorkOrder?.TargetQuantity ?? 0;
        var progressPercent = targetQuantity > 0
            ? Math.Min(100, (double)totalGood / targetQuantity * 100)
            : 0;

        // Calculate estimated completion time
        string? estimatedCompletion = null;
        if (targetQuantity > 0 && totalGood > 0 && activeJob != null && activeJob.WorkOrder?.Product != null)
        {
            var remaining = targetQuantity - totalGood;
            if (remaining > 0)
            {
                var standarCycleTime = activeJob.WorkOrder.Product.StandarCycleTime;
                var elapsedTime = (now - activeJob.StartTime).TotalSeconds;
                var currentRate = elapsedTime > 0 ? totalGood / elapsedTime : 0; // units per second
                
                if (currentRate > 0)
                {
                    var estimatedSeconds = remaining / currentRate;
                    var estimatedCompletionTime = now.AddSeconds(estimatedSeconds);
                    estimatedCompletion = estimatedCompletionTime.ToString("HH:mm");
                }
            }
        }

        // sinceLastChangeSeconds sudah dihitung di atas

        // ✅ PERBAIKAN: Tampilkan status sesuai dengan Machine.Status dari Admin (AKTIF atau TIDAK AKTIF)
        // Machine.Status adalah sumber kebenaran dari Admin Machines panel
        string displayStatus = machine.Status == MachineStatus.Aktif ? "AKTIF" : "TIDAK AKTIF";
        
        // ✅ PERBAIKAN: Log untuk debugging - pastikan MachineStatus dikembalikan dengan benar
        System.Diagnostics.Debug.WriteLine($"GetOperatorData - MachineId: '{machineId}', MachineStatus: '{machine.Status}', MachineStatus.ToString(): '{machine.Status.ToString()}'");

        // ========== CALCULATE TIME METRICS ==========
        var shiftWindow = ResolveShiftWindow(now, null, null);
        var effectiveNow = now < shiftWindow.End ? now : shiftWindow.End;
        
        var shiftJobRuns = machine.JobRuns
            .Where(j => j.StartTime < shiftWindow.End && (j.EndTime ?? effectiveNow) > shiftWindow.Start)
            .ToList();

        // 1. Total Shift Time
        TimeSpan totalShiftTime = shiftWindow.End - shiftWindow.Start;

        // 2. Pisahkan Planned dan Unplanned Downtime
        TimeSpan plannedDowntime = TimeSpan.Zero;
        TimeSpan unplannedDowntime = TimeSpan.Zero;

        foreach (var jr in shiftJobRuns)
        {
            foreach (var d in jr.DowntimeEvents)
            {
                var dEnd = d.EndTime ?? effectiveNow;
                var overlap = GetOverlap(d.StartTime, dEnd, shiftWindow.Start, shiftWindow.End);
                
                // Pisahkan berdasarkan kategori
                if (d.Reason?.Category == "Unplanned")
                {
                    unplannedDowntime += overlap;
                }
                else
                {
                    // Planned: Rest Break, Setup, dll
                    plannedDowntime += overlap;
                }
            }
        }

        // 3. Planned Production Time = Shift Time - Planned Downtime
        TimeSpan plannedProductionTime = totalShiftTime - plannedDowntime;
        if (plannedProductionTime.TotalSeconds < 0)
        {
            plannedProductionTime = TimeSpan.Zero;
        }

        // Jika belum ada data, gunakan durasi shift
        if (plannedProductionTime.TotalSeconds == 0 && shiftJobRuns.Count == 0)
        {
            plannedProductionTime = totalShiftTime;
        }

        // 4. Operating Time = Planned Production Time - Unplanned Downtime
        TimeSpan operatingTime = plannedProductionTime - unplannedDowntime;
        if (operatingTime.TotalSeconds < 0)
        {
            operatingTime = TimeSpan.Zero;
        }

        // 5. Total Downtime untuk display = Planned + Unplanned
        TimeSpan downtimeTotal = plannedDowntime + unplannedDowntime;

        // ✅ PERBAIKAN: Pastikan MachineStatus selalu di-return dengan explicit variable
        var machineStatusString = machine.Status.ToString(); // 'Aktif' atau 'TidakAktif'
        
        // ✅ PERBAIKAN: Log untuk debugging
        System.Diagnostics.Debug.WriteLine($"GetOperatorData RETURN - MachineId: '{machineId}', MachineStatus: '{machineStatusString}'");

        return Json(new
        {
            // Production Data
            TotalGood = totalGood,
            TotalReject = totalReject,
            TotalCount = totalGood + totalReject,
            TargetQuantity = targetQuantity,
            ProgressPercent = Math.Round(progressPercent, 1),
            
            // Status & Timing
            SinceLastChangeSeconds = sinceLastChangeSeconds,
            SinceLastChange = (now - lastChangeTime).ToString(@"hh\:mm\:ss"),
            LastStatusChangeTime = lastStatusChangeTime.ToString("O"), // ✅ ISO 8601 format untuk sinkronisasi timer
            
            // Job & Downtime Status
            HasActiveJob = activeJob != null,
            HasActiveDowntime = openDowntime != null,
            ActiveDowntimeDescription = openDowntime?.Reason?.Description,
            
            // ✅ PERBAIKAN: MachineStatus SELALU di-return (sumber kebenaran dari Admin)
            MachineStatus = machineStatusString, // 'Aktif' atau 'TidakAktif'
            Status = displayStatus, // 'AKTIF' atau 'TIDAK AKTIF' untuk display
            
            // Product Info
            EstimatedCompletion = estimatedCompletion,
            ProductName = activeJob?.WorkOrder?.Product?.Name,
            WorkOrderNumber = activeJob?.WorkOrder?.OrderNumber,
            ProductImageUrl = activeJob?.WorkOrder?.Product?.ImageUrl,
            
            // Time Metrics
            PlannedProductionTimeSeconds = plannedProductionTime.TotalSeconds,
            OperatingTimeSeconds = operatingTime.TotalSeconds,
            DowntimeTotalSeconds = downtimeTotal.TotalSeconds
        });
    }
}

