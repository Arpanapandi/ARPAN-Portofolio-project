using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OeeSystem.Data;
using OeeSystem.Models;
using OeeSystem.Services;
using OeeSystem.Models.ViewModels;

namespace OeeSystem.Controllers;

public class MachineController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IOeeService _oeeService;
    private static readonly List<(string Code, TimeSpan Start, TimeSpan End)> ShiftTemplates = new()
    {
        ("A", new TimeSpan(6, 0, 0), new TimeSpan(14, 0, 0)),
        ("B", new TimeSpan(14, 0, 0), new TimeSpan(22, 0, 0)),
        ("C", new TimeSpan(22, 0, 0), new TimeSpan(6, 0, 0))
    };

    public MachineController(ApplicationDbContext context, IOeeService oeeService)
    {
        _context = context;
        _oeeService = oeeService;
    }

    private static TimeSpan GetOverlap(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd)
    {
        var overlapStart = start < windowStart ? windowStart : start;
        var overlapEnd = end > windowEnd ? windowEnd : end;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }

    private static (DateTime Start, DateTime End, DateTime ShiftDate, string Code, string Key) ResolveShiftWindow(DateTime now, DateTime? shiftDate, string? shiftCode)
    {
        var code = string.IsNullOrWhiteSpace(shiftCode)
            ? null
            : shiftCode!.Trim().ToUpperInvariant();

        // Tentukan kode shift jika tidak dikirim
        if (code == null)
        {
            var tod = now.TimeOfDay;
            if (tod >= ShiftTemplates[0].Start && tod < ShiftTemplates[0].End)
            {
                code = "A";
            }
            else if (tod >= ShiftTemplates[1].Start && tod < ShiftTemplates[1].End)
            {
                code = "B";
            }
            else
            {
                code = "C";
            }
        }

        var template = ShiftTemplates.FirstOrDefault(s => s.Code == code);
        if (template == default)
        {
            template = ShiftTemplates[0];
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

    public async Task<IActionResult> OeeDetail(string id, int? shiftId = null, DateTime? shiftDate = null, string? shiftCode = null)
    {
        var now = DateTime.Now;
        var today = now.Date;
        
        // Ambil semua shifts dari database
        var shifts = await _context.Shifts.ToListAsync();
        
        // Tentukan shift yang dipilih atau shift saat ini
        Shift? selectedShift = null;
        if (shiftId.HasValue)
        {
            selectedShift = shifts.FirstOrDefault(s => s.Id == shiftId.Value);
        }
        
        // Jika tidak ada shift yang dipilih, gunakan shift saat ini
        if (selectedShift == null)
        {
            foreach (var s in shifts)
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;
                
                // Handle shift malam (end < start)
                if (s.EndTime < s.StartTime)
                {
                    if (now >= start || now <= end)
                    {
                        selectedShift = s;
                        break;
                    }
                }
                else
                {
                    if (now >= start && now <= end)
                    {
                        selectedShift = s;
                        break;
                    }
                }
            }
        }
        
        // Jika masih tidak ada shift, gunakan shift pertama sebagai default
        if (selectedShift == null)
        {
            selectedShift = shifts.FirstOrDefault();
        }
        
        // Tentukan periode shift yang dipilih
        DateTime shiftStart;
        DateTime shiftEnd;
        DateTime shiftDateForWindow;
        
        if (selectedShift != null)
        {
            // Handle shift malam (end < start, contoh: 22:00 - 06:00)
            if (selectedShift.EndTime < selectedShift.StartTime)
            {
                // Shift malam: mulai hari ini, selesai besok
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today.AddDays(1) + selectedShift.EndTime;
                
                // Jika shift dipilih secara eksplisit, tentukan shift hari ini atau kemarin
                if (shiftId.HasValue)
                {
                    // Jika sekarang masih dalam periode shift hari ini
                    if (now >= shiftStartToday && now < shiftEndToday)
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today;
                    }
                    else
                    {
                        // Shift kemarin (mulai kemarin, selesai hari ini)
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today.AddDays(-1);
                    }
                }
                else
                {
                    // Auto-detect: jika sekarang sebelum end time, berarti shift kemarin
                    if (now.TimeOfDay < selectedShift.EndTime)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today.AddDays(-1);
                    }
                    else
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today;
                    }
                }
            }
            else
            {
                // Shift normal (start < end)
                shiftStart = today + selectedShift.StartTime;
                shiftEnd = today + selectedShift.EndTime;
                shiftDateForWindow = today;
            }
        }
        else
        {
            // Fallback: gunakan shift template A
            shiftStart = today + new TimeSpan(6, 0, 0);
            shiftEnd = today + new TimeSpan(14, 0, 0);
            shiftDateForWindow = today;
        }
        
        var shiftWindow = (Start: shiftStart, End: shiftEnd, ShiftDate: shiftDateForWindow, Code: selectedShift?.Name ?? "A", Key: $"{shiftDateForWindow:yyyy-MM-dd}|{selectedShift?.Id ?? 0}");
        var effectiveNow = now < shiftWindow.End ? now : shiftWindow.End;

        var machine = await _context.Machines
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
            .FirstOrDefaultAsync(m => m.Id == id);
        
        // Load ManPower secara terpisah untuk menghindari error jika tabel belum ada
        if (machine != null)
        {
            try
            {
                foreach (var jobRun in machine.JobRuns.Where(j => j.ManPowerId.HasValue))
                {
                    await _context.Entry(jobRun)
                        .Reference(j => j.ManPower)
                        .LoadAsync();
                }
            }
            catch
            {
                // Jika tabel ManPower belum ada, abaikan (ManPower akan null)
            }
        }

        if (machine == null)
        {
            return NotFound();
        }

        var activeJob = machine.JobRuns
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefault(j => j.EndTime == null);

        bool hasOpenDowntime = activeJob != null &&
                               activeJob.DowntimeEvents.Any(d => d.EndTime == null);

        var status = _oeeService.GetRealTimeStatus(machine, activeJob, hasOpenDowntime);

        // ========== PERHITUNGAN OEE PER MESIN BERDASAR SHIFT ==========
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

        // 3. Total Downtime = Planned + Unplanned (sesuai formula di gambar)
        TimeSpan downtimeTotal = plannedDowntime + unplannedDowntime;

        // 4. Operating Time = Loading Time - Down Time (sesuai formula di gambar)
        // Loading Time = Total Shift Time
        TimeSpan operatingTime = totalShiftTime - downtimeTotal;
        if (operatingTime.TotalSeconds < 0)
        {
            operatingTime = TimeSpan.Zero;
        }

        // ✅ PERBAIKAN: Planned Production Time tetap dihitung untuk display (tidak digunakan di formula OEE)
        // Planned Production Time = Shift Time - Planned Downtime
        TimeSpan plannedProductionTime = totalShiftTime - plannedDowntime;
        if (plannedProductionTime.TotalSeconds < 0)
        {
            plannedProductionTime = TimeSpan.Zero;
        }

        // Jika belum ada data, gunakan durasi shift sebagai default
        if (plannedProductionTime.TotalSeconds == 0 && shiftJobRuns.Count == 0)
        {
            plannedProductionTime = totalShiftTime;
        }

        // ✅ TAMBAHKAN: Hitung Rest Break Time secara terpisah (untuk display)
        // Rest Break tetap termasuk dalam Planned Downtime untuk formula Planned Production Time
        TimeSpan restBreakTime = TimeSpan.Zero;
        bool hasActiveRestBreak = false;

        foreach (var jr in shiftJobRuns)
        {
            foreach (var d in jr.DowntimeEvents)
            {
                // Cek apakah ini Rest Break (termasuk dalam Planned Downtime)
                if (d.Reason?.Description == "Rest Break" || 
                    (d.Reason?.Category == "Planned" && d.Reason?.Description?.Contains("Rest", StringComparison.OrdinalIgnoreCase) == true))
                {
                    var dEnd = d.EndTime ?? effectiveNow;
                    var overlap = GetOverlap(d.StartTime, dEnd, shiftWindow.Start, shiftWindow.End);
                    restBreakTime += overlap;
                    
                    // Cek apakah ada Rest Break yang masih aktif
                    if (d.EndTime == null)
                    {
                        hasActiveRestBreak = true;
                    }
                }
            }
        }

        // 6. NO LOADING Time = Total Shift Time - (Operating Time + Total Downtime)
        // NO LOADING time adalah waktu idle (tidak ada job aktif) yang tidak masuk ke perhitungan OEE
        TimeSpan noLoadingTime = totalShiftTime - (operatingTime + downtimeTotal);
        if (noLoadingTime.TotalSeconds < 0)
        {
            noLoadingTime = TimeSpan.Zero;
        }

        // Hitung production counts di window shift
        var allCounts = shiftJobRuns
            .SelectMany(j => j.ProductionCounts
                .Where(p => p.Timestamp >= shiftWindow.Start && p.Timestamp <= shiftWindow.End))
            .ToList();

        int totalCount = allCounts.Sum(c => c.GoodCount + c.RejectCount);
        int goodCount = allCounts.Sum(c => c.GoodCount);
        int rejectCount = allCounts.Sum(c => c.RejectCount);

        // Standar cycle time diambil dari produk pada active job (jika ada)
        double standarCycleTime = 0;
        if (activeJob?.WorkOrder?.Product != null)
        {
            standarCycleTime = activeJob.WorkOrder.Product.StandarCycleTime;
        }

        // ✅ TAMBAHKAN: Hitung Nett Operating Time = Cycle Time × Total Produced
        TimeSpan nettOperatingTime = TimeSpan.Zero;
        if (standarCycleTime > 0 && totalCount > 0)
        {
            var nettOperatingSeconds = standarCycleTime * totalCount;
            nettOperatingTime = TimeSpan.FromSeconds(nettOperatingSeconds);
        }

        // ✅ FORMULA SESUAI GAMBAR: Hitung OEE dengan Loading Time dan Down Time
        // Loading Time = Total Shift Time
        // Down Time = Total Downtime (Planned + Unplanned)
        var oeeResult = _oeeService.CalculateOee(
            totalShiftTime,      // Loading Time
            downtimeTotal,        // Down Time (Planned + Unplanned)
            totalCount,
            goodCount,
            standarCycleTime > 0 ? standarCycleTime : 1);

        // Build ViewModel
        var vm = new MachineOeeViewModel
        {
            MachineId = machine.Id,
            MachineName = machine.Name,
            LineId = machine.LineId,
            ShiftCode = shiftWindow.Code,
            ShiftName = selectedShift?.Name ?? shiftWindow.Code,
            ShiftId = selectedShift?.Id,
            ShiftDate = shiftWindow.ShiftDate,
            ShiftKey = shiftWindow.Key,
            ShiftStart = shiftWindow.Start,
            ShiftEnd = shiftWindow.End,
            Status = status,
            StandarCycleTime = standarCycleTime,
            ImageUrl = machine.ImageUrl,
            Oee = oeeResult.Oee,
            Availability = oeeResult.Availability,
            Performance = oeeResult.Performance,
            Quality = oeeResult.Quality,
            PlannedProductionTime = plannedProductionTime,
            OperatingTime = operatingTime,
            DowntimeTotal = downtimeTotal,
            RestBreakTime = restBreakTime, // ✅ Rest Break Time (terpisah untuk display, tetap termasuk Planned Downtime)
            NoLoadingTime = noLoadingTime, // ✅ NO LOADING time (tidak masuk ke OEE)
            NettOperatingTime = nettOperatingTime, // ✅ Nett Operating Time = Cycle Time × Total Produced
            TotalCount = totalCount,
            GoodCount = goodCount,
            RejectCount = rejectCount,
            HasActiveRestBreak = hasActiveRestBreak // ✅ Status Rest Break aktif
        };

        // Active Job Info
        if (activeJob != null)
        {
            var currentQty = activeJob.ProductionCounts.Sum(p => p.GoodCount + p.RejectCount);
            
            // ✅ PERUBAHAN: Hitung lastChangeTime yang sinkron dengan Operating Time untuk OEE
            // - Jika ada downtime aktif (REST/LINE STOP) -> pakai StartTime downtime (RESET ke 00:00:00)
            // - Jika sedang RUNNING (tidak ada downtime aktif) -> hitung dari StartTime job dikurangi total downtime
            DateTime lastChangeTime;
            int sinceLastChangeSeconds;

            var openDowntimeForTimer = activeJob.DowntimeEvents
                .OrderByDescending(d => d.StartTime)
                .FirstOrDefault(d => d.EndTime == null);
            
            if (openDowntimeForTimer != null)
            {
                // Sedang REST / LINE STOP: RESET durasi ke 00:00:00 (mulai hitung dari awal downtime)
                lastChangeTime = openDowntimeForTimer.StartTime;
                var currentTime = DateTime.Now;
                sinceLastChangeSeconds = (int)(currentTime - lastChangeTime).TotalSeconds;
            }
            else
            {
                // Sedang RUNNING: Hitung durasi yang sinkron dengan Operating Time (untuk OEE)
                // Operating Time = JobRun duration - Unplanned Downtime duration (waktu running murni)
                var currentTime = DateTime.Now;
                
                // Hitung JobRun duration (dari start job sampai sekarang, dalam window shift)
                var jobStartInShift = activeJob.StartTime < shiftWindow.Start 
                    ? shiftWindow.Start 
                    : activeJob.StartTime;
                var jobEndInShift = currentTime > shiftWindow.End ? shiftWindow.End : currentTime;
                var jobDuration = jobEndInShift - jobStartInShift;
                if (jobDuration.TotalSeconds < 0) jobDuration = TimeSpan.Zero;
                
                // Hitung total Unplanned downtime yang sudah selesai di job ini (dalam window shift)
                var totalUnplannedDowntimeInShift = activeJob.DowntimeEvents
                    .Where(d => d.EndTime.HasValue && d.Reason?.Category == "Unplanned")
                    .Sum(d =>
                    {
                        var dStart = d.StartTime < shiftWindow.Start ? shiftWindow.Start : d.StartTime;
                        var dEnd = d.EndTime!.Value > shiftWindow.End ? shiftWindow.End : d.EndTime.Value;
                        var overlap = GetOverlap(dStart, dEnd, shiftWindow.Start, shiftWindow.End);
                        return overlap.TotalSeconds;
                    });
                
                // Operating Time murni = JobRun duration - Unplanned Downtime duration (sinkron dengan perhitungan OEE)
                var operatingTimeSeconds = jobDuration.TotalSeconds - totalUnplannedDowntimeInShift;
                sinceLastChangeSeconds = Math.Max(0, (int)operatingTimeSeconds);
                
                // LastChangeTime untuk display (backward compatibility)
                lastChangeTime = currentTime.AddSeconds(-sinceLastChangeSeconds);
            }
            
            // Started time: Jika job dimulai sebelum shift start, gunakan shift start
            DateTime displayStartTime = activeJob.StartTime < shiftWindow.Start 
                ? shiftWindow.Start 
                : activeJob.StartTime;
            
            // Calculate estimated completion time (sama seperti Operator View)
            string? estimatedCompletion = null;
            var targetQuantity = activeJob.WorkOrder?.TargetQuantity ?? 0;
            if (targetQuantity > 0 && goodCount > 0 && activeJob.WorkOrder?.Product != null)
            {
                var remaining = targetQuantity - goodCount;
                if (remaining > 0)
                {
                    var productCycleTime = activeJob.WorkOrder.Product.StandarCycleTime;
                    var currentTime = DateTime.Now;
                    var elapsedTime = (currentTime - activeJob.StartTime).TotalSeconds;
                    var currentRate = elapsedTime > 0 ? goodCount / elapsedTime : 0; // units per second
                    
                    if (currentRate > 0)
                    {
                        var estimatedSeconds = remaining / currentRate;
                        var estimatedCompletionTime = currentTime.AddSeconds(estimatedSeconds);
                        estimatedCompletion = estimatedCompletionTime.ToString("HH:mm");
                    }
                }
            }
            
            vm.ActiveJob = new JobRunViewModel
            {
                Id = activeJob.Id,
                WorkOrderNumber = activeJob.WorkOrder?.OrderNumber ?? "",
                ProductName = activeJob.WorkOrder?.Product?.Name ?? "",
                ProductImageUrl = activeJob.WorkOrder?.Product?.ImageUrl,
                OperatorName = activeJob.Operator?.Username ?? "",
                ManPowerId = activeJob.ManPowerId,
                ManPowerName = activeJob.ManPower?.Name,
                StartTime = displayStartTime, // Sinkron dengan shift
                EndTime = activeJob.EndTime,
                TargetQuantity = targetQuantity,
                CurrentQuantity = currentQty,
                LastStatusChangeTime = lastChangeTime, // Tetap ada untuk backward compatibility
                SinceLastChangeSeconds = sinceLastChangeSeconds // ✅ TAMBAHKAN untuk sinkronisasi dengan Operator View
            };
            
            // Store estimated completion untuk JavaScript
            ViewData["EstimatedCompletion"] = estimatedCompletion;
        }

        // Recent Downtimes (10 terakhir) dalam window shift
        vm.RecentDowntimes = shiftJobRuns
            .SelectMany(j => j.DowntimeEvents)
            .Where(d => d.StartTime < shiftWindow.End && (d.EndTime ?? effectiveNow) > shiftWindow.Start)
            .OrderByDescending(d => d.StartTime)
            .Take(10)
            .Select(d => new DowntimeEventViewModel
            {
                Id = d.Id,
                ReasonCategory = d.Reason?.Category ?? "",
                ReasonDescription = d.Reason?.Description ?? "",
                StartTime = d.StartTime,
                EndTime = d.EndTime,
                DurationSeconds = d.DurationSeconds > 0
                    ? d.DurationSeconds
                    : (d.EndTime ?? effectiveNow).Subtract(d.StartTime).TotalSeconds
            })
            .ToList();

        // Recent Production Counts (20 terakhir) dalam window shift
        vm.RecentProductionCounts = allCounts
            .OrderByDescending(p => p.Timestamp)
            .Take(20)
            .Select(p => new ProductionCountViewModel
            {
                Id = p.Id,
                Timestamp = p.Timestamp,
                GoodCount = p.GoodCount,
                RejectCount = p.RejectCount,
                RejectReason = p.RejectReason
            })
            .ToList();

        // Action Buttons Data
        var plannedRests = await _context.DowntimeReasons
            .Where(r => r.Category == "Planned")
            .ToListAsync();
        vm.RestReason = plannedRests
            .FirstOrDefault(r => r.Description.Contains("Rest", StringComparison.OrdinalIgnoreCase))
            ?? plannedRests.FirstOrDefault();
        
        vm.LineStopReasons = await _context.DowntimeReasons
            .Where(r => r.Category == "Unplanned")
            .OrderBy(r => r.Description)
            .ToListAsync();
        
        // NgTypes untuk modal Add Quantity
        vm.NgTypes = await _context.NgTypes
            .OrderBy(n => n.Code)
            .ToListAsync();
        
        // ManPowers untuk form
        try
        {
            ViewBag.ManPowers = await _context.ManPowers
                .Where(m => m.IsActive)
                .OrderBy(m => m.Value)
                .ToListAsync();
        }
        catch
        {
            // Jika tabel ManPowers belum ada (migration belum dijalankan), gunakan empty list
            ViewBag.ManPowers = new List<ManPower>();
        }
        
        // Status untuk action buttons
        vm.HasActiveJob = activeJob != null;
        vm.HasActiveDowntime = hasOpenDowntime;
        var openDowntimeForStatus = activeJob?.DowntimeEvents
            .OrderByDescending(d => d.StartTime)
            .FirstOrDefault(d => d.EndTime == null);
        vm.ActiveDowntimeDescription = openDowntimeForStatus?.Reason?.Description;
        vm.MachineStatus = machine.Status; // Status dari Admin (Aktif/TidakAktif)

        // Chart Data - gunakan data shift
        var runTimeMinutes = operatingTime.TotalMinutes;
        var idleTimeMinutes = 0;
        var offTimeMinutes = downtimeTotal.TotalMinutes;

        vm.ChartData = new ChartDataViewModel
        {
            RunTimeMinutes = runTimeMinutes,
            IdleTimeMinutes = idleTimeMinutes,
            OffTimeMinutes = offTimeMinutes,
            OeeValue = oeeResult.Oee,
            AvailabilityValue = oeeResult.Availability,
            PerformanceValue = oeeResult.Performance,
            QualityValue = oeeResult.Quality
        };

        // Weekly Trend Data (7 hari terakhir) tetap disajikan per hari untuk konteks historis
        var startOfWeek = now.Date.AddDays(-6);
        var weeklyTrend = new List<WeeklyTrendData>();

        for (int i = 0; i < 7; i++)
        {
            var date = startOfWeek.AddDays(i);
            var dayStart = date;
            var dayEnd = date.AddDays(1);

            var dayJobRuns = machine.JobRuns
                .Where(j => j.StartTime < dayEnd && (j.EndTime ?? effectiveNow) > dayStart)
                .ToList();

            double dayRunTime = 0;
            double dayDowntime = 0;

            foreach (var jr in dayJobRuns)
            {
                var jrEnd = jr.EndTime ?? (jr.StartTime < dayEnd ? effectiveNow : dayEnd);
                dayRunTime += GetOverlap(jr.StartTime, jrEnd, dayStart, dayEnd).TotalMinutes;

                var jrDowntimeMinutes = jr.DowntimeEvents.Sum(d =>
                {
                    var dEnd = d.EndTime ?? (d.StartTime < dayEnd ? effectiveNow : dayEnd);
                    return GetOverlap(d.StartTime, dEnd, dayStart, dayEnd).TotalMinutes;
                });

                dayDowntime += jrDowntimeMinutes;
            }

            var dayPlannedMinutes = dayRunTime + dayDowntime;
            var dayIdleTime = 0;
            var dayOffTime = dayDowntime;

            weeklyTrend.Add(new WeeklyTrendData
            {
                DateLabel = date.ToString("MMM dd"),
                RunTimeMinutes = dayRunTime,
                IdleTimeMinutes = dayIdleTime,
                OffTimeMinutes = dayOffTime
            });
        }

        vm.ChartData.WeeklyTrend = weeklyTrend;

        return View(vm);
    }

    // API endpoint untuk mendapatkan data real-time time metrics
    [HttpGet]
    public async Task<IActionResult> GetTimeMetrics(string id, int? shiftId = null, DateTime? shiftDate = null, string? shiftCode = null)
    {
        var now = DateTime.Now;
        var today = now.Date;
        
        // Ambil semua shifts dari database
        var shifts = await _context.Shifts.ToListAsync();
        
        // Tentukan shift yang dipilih atau shift saat ini
        Shift? selectedShift = null;
        if (shiftId.HasValue)
        {
            selectedShift = shifts.FirstOrDefault(s => s.Id == shiftId.Value);
        }
        
        // Jika tidak ada shift yang dipilih, gunakan shift saat ini
        if (selectedShift == null)
        {
            foreach (var s in shifts)
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;
                
                // Handle shift malam (end < start)
                if (s.EndTime < s.StartTime)
                {
                    if (now >= start || now <= end)
                    {
                        selectedShift = s;
                        break;
                    }
                }
                else
                {
                    if (now >= start && now <= end)
                    {
                        selectedShift = s;
                        break;
                    }
                }
            }
        }
        
        // Jika masih tidak ada shift, gunakan shift pertama sebagai default
        if (selectedShift == null)
        {
            selectedShift = shifts.FirstOrDefault();
        }
        
        // Tentukan periode shift yang dipilih
        DateTime shiftStart;
        DateTime shiftEnd;
        DateTime shiftDateForWindow;
        
        if (selectedShift != null)
        {
            // Handle shift malam (end < start)
            if (selectedShift.EndTime < selectedShift.StartTime)
            {
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today.AddDays(1) + selectedShift.EndTime;
                
                if (shiftId.HasValue)
                {
                    if (now >= shiftStartToday && now < shiftEndToday)
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today;
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today.AddDays(-1);
                    }
                }
                else
                {
                    if (now.TimeOfDay < selectedShift.EndTime)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today.AddDays(-1);
                    }
                    else
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                        shiftDateForWindow = today;
                    }
                }
            }
            else
            {
                shiftStart = today + selectedShift.StartTime;
                shiftEnd = today + selectedShift.EndTime;
                shiftDateForWindow = today;
            }
        }
        else
        {
            shiftStart = today + new TimeSpan(6, 0, 0);
            shiftEnd = today + new TimeSpan(14, 0, 0);
            shiftDateForWindow = today;
        }
        
        var shiftWindow = (Start: shiftStart, End: shiftEnd, ShiftDate: shiftDateForWindow, Code: selectedShift?.Name ?? "A", Key: $"{shiftDateForWindow:yyyy-MM-dd}|{selectedShift?.Id ?? 0}");
        var effectiveNow = now < shiftWindow.End ? now : shiftWindow.End;

        var machine = await _context.Machines
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (machine == null)
        {
            return NotFound();
        }

        // Cek active job dan active downtime
        var activeJob = machine.JobRuns
            .OrderByDescending(j => j.StartTime)
            .FirstOrDefault(j => j.EndTime == null);

        var activeDowntime = activeJob?.DowntimeEvents
            .OrderByDescending(d => d.StartTime)
            .FirstOrDefault(d => d.EndTime == null);

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

        // 3. Total Downtime = Planned + Unplanned (sesuai formula di gambar)
        TimeSpan downtimeTotal = plannedDowntime + unplannedDowntime;

        // 4. Operating Time = Loading Time - Down Time (sesuai formula di gambar)
        // Loading Time = Total Shift Time
        TimeSpan operatingTime = totalShiftTime - downtimeTotal;
        if (operatingTime.TotalSeconds < 0)
        {
            operatingTime = TimeSpan.Zero;
        }

        // ✅ PERBAIKAN: Planned Production Time tetap dihitung untuk display (tidak digunakan di formula OEE)
        // Planned Production Time = Shift Time - Planned Downtime
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

        // ✅ TAMBAHKAN: Hitung Rest Break Time secara terpisah (untuk display)
        // Rest Break tetap termasuk dalam Planned Downtime untuk formula Planned Production Time
        TimeSpan restBreakTime = TimeSpan.Zero;
        bool hasActiveRestBreak = false;

        foreach (var jr in shiftJobRuns)
        {
            foreach (var d in jr.DowntimeEvents)
            {
                // Cek apakah ini Rest Break (termasuk dalam Planned Downtime)
                if (d.Reason?.Description == "Rest Break" || 
                    (d.Reason?.Category == "Planned" && d.Reason?.Description?.Contains("Rest", StringComparison.OrdinalIgnoreCase) == true))
                {
                    var dEnd = d.EndTime ?? effectiveNow;
                    var overlap = GetOverlap(d.StartTime, dEnd, shiftWindow.Start, shiftWindow.End);
                    restBreakTime += overlap;
                    
                    // Cek apakah ada Rest Break yang masih aktif
                    if (d.EndTime == null)
                    {
                        hasActiveRestBreak = true;
                    }
                }
            }
        }

        // 6. NO LOADING Time = Total Shift Time - (Operating Time + Total Downtime)
        // NO LOADING time adalah waktu idle (tidak ada job aktif) yang tidak masuk ke perhitungan OEE
        TimeSpan noLoadingTime = totalShiftTime - (operatingTime + downtimeTotal);
        if (noLoadingTime.TotalSeconds < 0)
        {
            noLoadingTime = TimeSpan.Zero;
        }

        // ✅ TAMBAHKAN: Hitung Nett Operating Time = Cycle Time × Total Produced
        TimeSpan nettOperatingTime = TimeSpan.Zero;
        double standarCycleTime = 0;
        if (activeJob?.WorkOrder?.Product != null)
        {
            standarCycleTime = activeJob.WorkOrder.Product.StandarCycleTime;
        }

        // Hitung production counts di window shift
        var allCounts = shiftJobRuns
            .SelectMany(j => j.ProductionCounts
                .Where(p => p.Timestamp >= shiftWindow.Start && p.Timestamp <= shiftWindow.End))
            .ToList();

        int totalCount = allCounts.Sum(c => c.GoodCount + c.RejectCount);

        if (standarCycleTime > 0 && totalCount > 0)
        {
            var nettOperatingSeconds = standarCycleTime * totalCount;
            nettOperatingTime = TimeSpan.FromSeconds(nettOperatingSeconds);
        }

        // Hitung persentase untuk progress bar
        var operatingPercent = plannedProductionTime.TotalSeconds > 0
            ? (operatingTime.TotalSeconds / plannedProductionTime.TotalSeconds * 100)
            : 0;

        var downtimePercent = plannedProductionTime.TotalSeconds > 0
            ? (downtimeTotal.TotalSeconds / plannedProductionTime.TotalSeconds * 100)
            : 0;

        // ✅ PERUBAHAN: Hitung lastChangeTime yang sinkron dengan Operating Time untuk OEE
        DateTime? lastStatusChangeTime = null;
        int? sinceLastChangeSeconds = null;

        if (activeJob != null)
        {
            if (activeDowntime != null)
            {
                // Sedang REST / LINE STOP: RESET durasi ke 00:00:00
                lastStatusChangeTime = activeDowntime.StartTime;
                var currentTime = DateTime.Now;
                sinceLastChangeSeconds = (int)(currentTime - activeDowntime.StartTime).TotalSeconds;
            }
            else
            {
                // Sedang RUNNING: Hitung durasi yang sinkron dengan Operating Time (untuk OEE)
                var currentTime = DateTime.Now;
                var jobDuration = (currentTime - activeJob.StartTime).TotalSeconds;
                
                // Hitung total Unplanned downtime yang sudah selesai di job ini
                var totalUnplannedDowntimeSeconds = activeJob.DowntimeEvents
                    .Where(d => d.EndTime.HasValue && d.Reason?.Category == "Unplanned")
                    .Sum(d =>
                    {
                        var dStart = d.StartTime < shiftWindow.Start ? shiftWindow.Start : d.StartTime;
                        var dEnd = d.EndTime!.Value > shiftWindow.End ? shiftWindow.End : d.EndTime.Value;
                        var overlap = GetOverlap(dStart, dEnd, shiftWindow.Start, shiftWindow.End);
                        return overlap.TotalSeconds;
                    });
                
                // Operating Time murni = JobRun duration - Unplanned Downtime duration (sinkron dengan perhitungan OEE)
                var operatingTimeSeconds = jobDuration - totalUnplannedDowntimeSeconds;
                sinceLastChangeSeconds = Math.Max(0, (int)operatingTimeSeconds);
                
                // LastStatusChangeTime untuk display (backward compatibility)
                lastStatusChangeTime = currentTime.AddSeconds(-(double)sinceLastChangeSeconds);
            }
        }

        // Hitung persentase NO LOADING (berdasarkan total shift time)
        var noLoadingPercent = totalShiftTime.TotalSeconds > 0
            ? (noLoadingTime.TotalSeconds / totalShiftTime.TotalSeconds * 100)
            : 0;

        // Return info tentang active job dan downtime untuk real-time calculation
        return Json(new
        {
            ShiftKey = shiftWindow.Key,
            ShiftCode = shiftWindow.Code,
            ShiftDate = shiftWindow.ShiftDate.ToString("yyyy-MM-dd"),
            ShiftStart = shiftWindow.Start,
            ShiftEnd = shiftWindow.End,
            PlannedProductionTimeSeconds = Math.Floor(plannedProductionTime.TotalSeconds),
            PlannedProductionTime = plannedProductionTime.ToString(@"hh\:mm\:ss"),
            OperatingTimeSeconds = Math.Floor(operatingTime.TotalSeconds),
            OperatingTime = operatingTime.ToString(@"hh\:mm\:ss"),
            DowntimeTotalSeconds = Math.Floor(downtimeTotal.TotalSeconds),
            DowntimeTotal = downtimeTotal.ToString(@"hh\:mm\:ss"),
            RestBreakTimeSeconds = Math.Floor(restBreakTime.TotalSeconds), // ✅ Rest Break Time (terpisah untuk display, tetap termasuk Planned Downtime)
            RestBreakTime = restBreakTime.ToString(@"hh\:mm\:ss"), // ✅ Rest Break Time
            HasActiveRestBreak = hasActiveRestBreak, // ✅ Status Rest Break aktif
            NoLoadingTimeSeconds = Math.Floor(noLoadingTime.TotalSeconds), // ✅ NO LOADING time (tidak masuk ke OEE)
            NoLoadingTime = noLoadingTime.ToString(@"hh\:mm\:ss"), // ✅ NO LOADING time (tidak masuk ke OEE)
            NettOperatingTimeSeconds = Math.Floor(nettOperatingTime.TotalSeconds), // ✅ Nett Operating Time = Cycle Time × Total Produced
            NettOperatingTime = nettOperatingTime.ToString(@"hh\:mm\:ss"), // ✅ Nett Operating Time
            OperatingPercent = Math.Round(operatingPercent, 1),
            DowntimePercent = Math.Round(downtimePercent, 1),
            NoLoadingPercent = Math.Round(noLoadingPercent, 1), // ✅ NO LOADING percent
            HasActiveJob = activeJob != null,
            ActiveJobStartTime = activeJob?.StartTime.ToString("O"), // ISO 8601 format
            HasActiveDowntime = activeDowntime != null,
            LastStatusChangeTime = lastStatusChangeTime?.ToString("O"), // ISO 8601 format (backward compatibility)
            SinceLastChangeSeconds = sinceLastChangeSeconds, // ✅ TAMBAHKAN untuk sinkronisasi dengan Operator View
            ActiveDowntimeStartTime = activeDowntime?.StartTime.ToString("O") // ISO 8601 format
        });
    }
}

