using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OeeSystem.Data;
using OeeSystem.Models;
using OeeSystem.Services;
using OeeSystem.Models.ViewModels;
using ViewModels = OeeSystem.Models.ViewModels;

namespace OeeSystem.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IOeeService _oeeService;
    private readonly IWebHostEnvironment _environment;

    public HomeController(ApplicationDbContext context, IOeeService oeeService, IWebHostEnvironment environment)
    {
        _context = context;
        _oeeService = oeeService;
        _environment = environment;
    }
    
    // ✅ Helper method untuk memverifikasi file image ada di filesystem (optional, untuk debugging)
    private bool ImageFileExists(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return false;
        
        try
        {
            // Hapus leading slash jika ada
            var imagePath = imageUrl.TrimStart('/');
            var fullPath = Path.Combine(_environment.WebRootPath, imagePath);
            var exists = System.IO.File.Exists(fullPath);
            
            if (!exists)
            {
                System.Diagnostics.Debug.WriteLine($"Image file not found: {fullPath}");
            }
            
            return exists;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking image file: {ex.Message}");
            return false;
        }
    }

    public async Task<IActionResult> Index(int? shiftId = null, int? plantId = null, string? machineId = null)
    {
        try
        {
            var now = DateTime.Now;
            var today = now.Date;

            // Ambil semua shifts untuk dropdown
            var shifts = await _context.Shifts.ToListAsync();
        
        // Ambil semua plants untuk dropdown dari tabel Plants, diurutkan berdasarkan Name
        var plants = await _context.Plants
            .OrderBy(p => p.Name)
            .ToListAsync();
        
        // Ambil SEMUA machines untuk dropdown (untuk cascade filtering di JavaScript)
        // JavaScript akan melakukan filtering berdasarkan plantId yang dipilih
        var allMachinesForDropdown = await _context.Machines
            .OrderBy(m => m.Name)
            .ToListAsync();
        
        // Filter machines untuk dropdown berdasarkan plantId jika dipilih (untuk initial load)
        var machinesForDropdown = plantId.HasValue 
            ? allMachinesForDropdown.Where(m => m.PlantId == plantId.Value).ToList()
            : allMachinesForDropdown;
        
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
        
        string currentShiftName = selectedShift?.Name ?? "No Shift";
        
        // Tentukan periode shift yang dipilih
        DateTime shiftStart;
        DateTime shiftEnd;
        
        if (selectedShift != null)
        {
            // Handle shift malam (end < start, contoh: 19:30 - 07:30)
            if (selectedShift.EndTime < selectedShift.StartTime)
            {
                // Shift malam: mulai hari ini, selesai besok
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today.AddDays(1) + selectedShift.EndTime;
                
                // Jika shift dipilih secara eksplisit, tentukan shift hari ini atau kemarin
                if (shiftId.HasValue)
                {
                    // Jika sekarang masih dalam periode shift hari ini (setelah 19:30 hari ini dan sebelum 07:30 besok)
                    if (now >= shiftStartToday && now < shiftEndToday)
                    {
                        // Tampilkan shift hari ini (periode penuh)
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                    }
                    else if (now < shiftStartToday)
                    {
                        // Sebelum shift hari ini dimulai, tampilkan shift kemarin
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    else
                    {
                        // Setelah shift hari ini selesai, tampilkan shift kemarin (periode penuh)
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                }
                else
                {
                    // Shift otomatis (tidak dipilih, gunakan shift saat ini)
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    // Jika shift sudah lewat hari ini, tampilkan shift kemarin
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    // Jika shift sedang berjalan, batasi sampai sekarang
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                    // Jika shift sudah selesai, tampilkan shift penuh
                }
            }
            else
            {
                // Shift normal: mulai dan selesai hari yang sama
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today + selectedShift.EndTime;
                
                // Jika shift dipilih secara eksplisit, selalu tampilkan periode penuh
                if (shiftId.HasValue)
                {
                    // Jika sekarang masih dalam periode shift hari ini
                    if (now >= shiftStartToday && now <= shiftEndToday)
                    {
                        // Tampilkan shift hari ini (periode penuh)
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                    }
                    else if (now < shiftStartToday)
                    {
                        // Sebelum shift hari ini dimulai, tampilkan shift kemarin
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else
                    {
                        // Setelah shift hari ini selesai, tampilkan shift kemarin (periode penuh)
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                }
                else
                {
                    // Shift otomatis (tidak dipilih, gunakan shift saat ini)
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    // Jika shift sudah lewat hari ini, tampilkan shift kemarin
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    // Jika shift sedang berjalan, batasi sampai sekarang
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                    // Jika shift sudah selesai, tampilkan shift penuh
                }
            }
        }
        else
        {
            // Default: 8 jam terakhir
            shiftStart = now.AddHours(-8);
            shiftEnd = now;
        }

        // Filter machines berdasarkan plantId dan machineId
        var machinesQuery = _context.Machines
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.WorkOrder)
                    .ThenInclude(w => w.Product)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
                    .ThenInclude(d => d.Reason)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.ProductionCounts)
            .Include(m => m.Plant)
            .AsQueryable();
        
        // Apply filters
        if (plantId.HasValue)
        {
            machinesQuery = machinesQuery.Where(m => m.PlantId == plantId.Value);
        }
        
        if (!string.IsNullOrEmpty(machineId))
        {
            machinesQuery = machinesQuery.Where(m => m.Id == machineId);
        }
        
        var machines = await machinesQuery.ToListAsync();
        
        // ✅ PERBAIKAN: Pastikan ImageUrl ter-load dengan benar dari database
        // Reload ImageUrl untuk setiap machine dari database untuk memastikan data fresh
        try
        {
            var machineIds = machines.Select(m => m.Id).ToList();
            if (machineIds.Any())
            {
                var machineImageUrls = await _context.Machines
                    .AsNoTracking()
                    .Where(m => machineIds.Contains(m.Id))
                    .Select(m => new { m.Id, m.ImageUrl })
                    .ToDictionaryAsync(m => m.Id, m => m.ImageUrl);
                
                foreach (var machine in machines)
                {
                    // Ambil ImageUrl langsung dari dictionary (data fresh dari database)
                    if (machineImageUrls.TryGetValue(machine.Id, out var imageUrl) && !string.IsNullOrWhiteSpace(imageUrl))
                    {
                        machine.ImageUrl = imageUrl.Trim();
                    }
                    else
                    {
                        machine.ImageUrl = null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error tapi jangan stop proses
            System.Diagnostics.Debug.WriteLine($"Warning: Could not reload ImageUrl from database: {ex.Message}");
            // Biarkan ImageUrl dari query utama digunakan
        }

        var machineCards = new List<MachineCardViewModel>();

        // ✅ PERBAIKAN: Hitung Planned dan Unplanned Downtime dari semua mesin
        TimeSpan totalShiftTime = shiftEnd - shiftStart;
        TimeSpan totalPlannedDowntime = TimeSpan.Zero;
        TimeSpan totalUnplannedDowntime = TimeSpan.Zero;
        TimeSpan operatingTime = TimeSpan.Zero;
        int totalCount = 0;
        int goodCount = 0;
        double avgStandarCycle = 0;
        int standarCycleCount = 0;

        foreach (var machine in machines)
        {
            // Filter JobRuns berdasarkan periode shift
            var shiftJobRuns = machine.JobRuns
                .Where(j => (j.StartTime >= shiftStart && j.StartTime <= shiftEnd) ||
                           (j.EndTime.HasValue && j.EndTime >= shiftStart && j.EndTime <= shiftEnd) ||
                           (j.StartTime <= shiftStart && (j.EndTime ?? now) >= shiftEnd))
                .ToList();

            var activeJob = shiftJobRuns
                .OrderByDescending(j => j.StartTime)
                .FirstOrDefault(j => j.EndTime == null);

            bool hasOpenDowntime = activeJob != null &&
                                   activeJob.DowntimeEvents.Any(d => d.EndTime == null);

            var status = _oeeService.GetRealTimeStatus(machine, activeJob, hasOpenDowntime);

            var currentProduct = activeJob?.WorkOrder?.Product;
            var currentWo = activeJob?.WorkOrder;
            
            // SELALU gunakan gambar mesin dari Admin Panel → Machines (kolom Image)
            // Gambar produk di Dashboard harus sama dengan gambar mesin yang ada di Admin Panel
            string? productImageUrl = null;
            
            // Prioritas 1: Gunakan gambar mesin saat ini (langsung dari machine.ImageUrl)
            // ImageUrl sudah di-ensure ter-load di loop sebelumnya dari database
            if (!string.IsNullOrWhiteSpace(machine.ImageUrl))
            {
                productImageUrl = machine.ImageUrl.Trim();
                System.Diagnostics.Debug.WriteLine($"Using machine ImageUrl for {machine.Id}: '{productImageUrl}'");
            }
            
            // Fallback: Jika mesin tidak punya gambar, cari dari mesin lain yang terhubung dengan produk ini
            if (string.IsNullOrEmpty(productImageUrl) && currentProduct != null)
            {
                try
                {
                    var productMachine = await _context.ProductMachines
                        .AsNoTracking()
                        .Include(pm => pm.Machine)
                        .Where(pm => pm.ProductId == currentProduct.Id && !string.IsNullOrEmpty(pm.Machine.ImageUrl))
                        .Select(pm => pm.Machine.ImageUrl)
                        .FirstOrDefaultAsync();
                    
                    if (!string.IsNullOrWhiteSpace(productMachine))
                    {
                        productImageUrl = productMachine.Trim();
                        System.Diagnostics.Debug.WriteLine($"Using product machine ImageUrl: '{productImageUrl}'");
                    }
                }
                catch (Exception ex)
                {
                    // Log error tapi jangan stop proses
                    System.Diagnostics.Debug.WriteLine($"Warning: Could not load product machine image: {ex.Message}");
                }
            }
            
            // Debug: Log final ImageUrl yang akan digunakan
            System.Diagnostics.Debug.WriteLine($"Final ProductImageUrl for machine {machine.Id}: '{productImageUrl ?? "NULL"}'");

            // Filter ProductionCounts berdasarkan periode shift
            var allCounts = shiftJobRuns
                .SelectMany(j => j.ProductionCounts)
                .Where(c => c.Timestamp >= shiftStart && c.Timestamp <= shiftEnd)
                .ToList();

            int mTotal = allCounts.Sum(c => c.GoodCount + c.RejectCount);
            int mGood = allCounts.Sum(c => c.GoodCount);

            totalCount += mTotal;
            goodCount += mGood;

            // ✅ PERBAIKAN: Pisahkan Planned dan Unplanned Downtime, lalu hitung Operating Time
            foreach (var jr in shiftJobRuns)
            {
                // Potong JobRun sesuai periode shift
                var jrStart = jr.StartTime > shiftStart ? jr.StartTime : shiftStart;
                var jrEnd = (jr.EndTime ?? now) < shiftEnd ? (jr.EndTime ?? now) : shiftEnd;
                
                if (jrStart >= jrEnd) continue;
                
                var jrDuration = jrEnd - jrStart;

                // ✅ PERBAIKAN: Pisahkan Planned dan Unplanned Downtime
                var jrPlannedDowntimeSeconds = jr.DowntimeEvents
                    .Where(d => d.Reason != null && d.Reason.Category != "Unplanned")
                    .Where(d => d.StartTime < jrEnd && (d.EndTime ?? now) > jrStart)
                    .Sum(d =>
                    {
                        var dStart = d.StartTime > jrStart ? d.StartTime : jrStart;
                        var dEnd = (d.EndTime ?? now) < jrEnd ? (d.EndTime ?? now) : jrEnd;
                        return (dEnd - dStart).TotalSeconds;
                    });

                var jrUnplannedDowntimeSeconds = jr.DowntimeEvents
                    .Where(d => d.Reason != null && d.Reason.Category == "Unplanned")
                    .Where(d => d.StartTime < jrEnd && (d.EndTime ?? now) > jrStart)
                    .Sum(d =>
                    {
                        var dStart = d.StartTime > jrStart ? d.StartTime : jrStart;
                        var dEnd = (d.EndTime ?? now) < jrEnd ? (d.EndTime ?? now) : jrEnd;
                        return (dEnd - dStart).TotalSeconds;
                    });

                // Accumulate total Planned dan Unplanned Downtime
                totalPlannedDowntime += TimeSpan.FromSeconds(jrPlannedDowntimeSeconds);
                totalUnplannedDowntime += TimeSpan.FromSeconds(jrUnplannedDowntimeSeconds);

                // Operating Time = JobRun duration - Unplanned Downtime (Planned tidak mengurangi Operating Time)
                var netSeconds = Math.Max(0, jrDuration.TotalSeconds - jrUnplannedDowntimeSeconds);
                operatingTime += TimeSpan.FromSeconds(netSeconds);
            }

            // Standar cycle time diambil dari produk yang sedang jalan (jika ada)
            var currentStandarCycle = currentProduct?.StandarCycleTime ?? 0;
            if (currentStandarCycle > 0)
            {
                avgStandarCycle += currentStandarCycle;
                standarCycleCount++;
            }

            machineCards.Add(new MachineCardViewModel
            {
                MachineId = machine.Id,
                MachineName = machine.Name,
                LineId = machine.LineId,
                Status = status,
                ProductName = currentProduct?.Name,
                ProductImageUrl = productImageUrl,
                WorkOrderNumber = currentWo?.OrderNumber
            });
        }

        if (standarCycleCount > 0)
        {
            avgStandarCycle /= standarCycleCount;
        }

        // ✅ FORMULA SESUAI GAMBAR: Total Downtime = Planned + Unplanned
        TimeSpan totalDowntime = totalPlannedDowntime + totalUnplannedDowntime;

        // ✅ FORMULA SESUAI GAMBAR: Operating Time = Loading Time - Down Time
        // Loading Time = Total Shift Time
        // Down Time = Total Downtime (Planned + Unplanned)
        TimeSpan operatingTimeNew = totalShiftTime - totalDowntime;
        if (operatingTimeNew.TotalSeconds < 0)
        {
            operatingTimeNew = TimeSpan.Zero;
        }

        // ✅ PERBAIKAN: Planned Production Time tetap dihitung untuk display (tidak digunakan di formula OEE)
        TimeSpan plannedProductionTime = totalShiftTime - totalPlannedDowntime;
        if (plannedProductionTime.TotalSeconds < 0)
        {
            plannedProductionTime = TimeSpan.Zero;
        }

        // Jika belum ada data, gunakan durasi shift sebagai default
        if (plannedProductionTime.TotalSeconds == 0 && machines.Count == 0)
        {
            plannedProductionTime = totalShiftTime;
        }

        // ✅ FORMULA SESUAI GAMBAR: Hitung OEE dengan Loading Time dan Down Time
        var oeeResult = _oeeService.CalculateOee(
            totalShiftTime,      // Loading Time
            totalDowntime,        // Down Time (Planned + Unplanned)
            totalCount,
            goodCount,
            avgStandarCycle <= 0 ? 1 : avgStandarCycle);

        // Generate Machine State Timeline untuk setiap mesin
        var machineStateTimelines = new List<MachineStateTimelineViewModel>();
        
        // shiftStart dan shiftEnd sudah ditentukan di atas berdasarkan shift yang dipilih

        foreach (var machine in machines)
        {
            var timeline = new MachineStateTimelineViewModel
            {
                MachineId = machine.Id,
                MachineName = machine.Name
            };

            // Generate time labels setiap 15 menit
            var currentLabel = shiftStart;
            while (currentLabel <= shiftEnd)
            {
                timeline.TimeLabels.Add(currentLabel.ToString("HH:mm"));
                currentLabel = currentLabel.AddMinutes(15);
            }

            // Ambil semua JobRuns hari ini untuk mesin ini
            var todayJobRuns = machine.JobRuns
                .Where(j => (j.StartTime.Date == today || (j.EndTime.HasValue && j.EndTime.Value.Date == today)) ||
                           (j.StartTime <= shiftEnd && (j.EndTime ?? now) >= shiftStart))
                .OrderBy(j => j.StartTime)
                .ToList();

            var segments = new List<StateSegmentData>();
            var currentTime = shiftStart;

            foreach (var jr in todayJobRuns)
            {
                var jrStart = jr.StartTime > shiftStart ? jr.StartTime : shiftStart;
                var jrEnd = jr.EndTime ?? now;
                if (jrEnd > shiftEnd) jrEnd = shiftEnd;
                if (jrStart >= shiftEnd) break;

                // Jika ada gap antara currentTime dan jrStart, itu adalah Idle
                if (currentTime < jrStart)
                {
                    segments.Add(new StateSegmentData
                    {
                        State = "Idle",
                        StartTime = currentTime.ToString("HH:mm"),
                        EndTime = jrStart.ToString("HH:mm"),
                        DurationMinutes = (jrStart - currentTime).TotalMinutes
                    });
                }

                // Proses JobRun dengan DowntimeEvents
                var downtimeEvents = jr.DowntimeEvents
                    .Where(d => d.StartTime >= jrStart && d.StartTime <= jrEnd)
                    .OrderBy(d => d.StartTime)
                    .ToList();

                var segmentStart = jrStart;
                foreach (var dt in downtimeEvents)
                {
                    // Sebelum downtime = Run
                    if (segmentStart < dt.StartTime)
                    {
                        segments.Add(new StateSegmentData
                        {
                            State = "Run",
                            StartTime = segmentStart.ToString("HH:mm"),
                            EndTime = dt.StartTime.ToString("HH:mm"),
                            DurationMinutes = (dt.StartTime - segmentStart).TotalMinutes
                        });
                    }

                    // Downtime = Stop
                    var dtEnd = dt.EndTime ?? now;
                    if (dtEnd > jrEnd) dtEnd = jrEnd;
                    
                    segments.Add(new StateSegmentData
                    {
                        State = "Stop",
                        StartTime = dt.StartTime.ToString("HH:mm"),
                        EndTime = dtEnd.ToString("HH:mm"),
                        DurationMinutes = (dtEnd - dt.StartTime).TotalMinutes
                    });

                    segmentStart = dtEnd;
                }

                // Setelah downtime terakhir sampai jrEnd = Run
                if (segmentStart < jrEnd)
                {
                    segments.Add(new StateSegmentData
                    {
                        State = "Run",
                        StartTime = segmentStart.ToString("HH:mm"),
                        EndTime = jrEnd.ToString("HH:mm"),
                        DurationMinutes = (jrEnd - segmentStart).TotalMinutes
                    });
                }

                currentTime = jrEnd;
            }

            // Jika masih ada waktu sampai shiftEnd, itu adalah Idle
            if (currentTime < shiftEnd)
            {
                segments.Add(new StateSegmentData
                {
                    State = "Idle",
                    StartTime = currentTime.ToString("HH:mm"),
                    EndTime = shiftEnd.ToString("HH:mm"),
                    DurationMinutes = (shiftEnd - currentTime).TotalMinutes
                });
            }
            
            // Jika tidak ada segments sama sekali, buat satu segment Idle untuk seluruh periode
            if (segments.Count == 0 && timeline.TimeLabels.Count > 0)
            {
                segments.Add(new StateSegmentData
                {
                    State = "Idle",
                    StartTime = timeline.TimeLabels.First(),
                    EndTime = timeline.TimeLabels.Last(),
                    DurationMinutes = (shiftEnd - shiftStart).TotalMinutes
                });
            }

            timeline.StateData = segments;
            machineStateTimelines.Add(timeline);
        }

        var vm = new DashboardViewModel
        {
            CurrentTime = now,
            CurrentShiftName = currentShiftName,
            SelectedShiftId = selectedShift?.Id,
            SelectedPlantId = plantId,
            SelectedMachineId = machineId,
            Shifts = shifts.Select(s => new ShiftOption
            {
                Id = s.Id,
                Name = s.Name,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList(),
            Plants = plants.Select(p => new PlantOption
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name
            }).ToList(),
            // Kirim SEMUA machines untuk cascade filtering di JavaScript
            MachineOptions = allMachinesForDropdown.Select(m => new MachineOption
            {
                Id = m.Id,
                Name = m.Name,
                PlantId = m.PlantId
            }).ToList(),
            Availability = oeeResult.Availability,
            Performance = oeeResult.Performance,
            Quality = oeeResult.Quality,
            Oee = oeeResult.Oee,
            Machines = machineCards,
            MachineStateTimelines = machineStateTimelines
        };

            return View(vm);
        }
        catch (Exception ex)
        {
            // Log error untuk debugging
            System.Diagnostics.Debug.WriteLine($"Error di HomeController.Index: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
            }
            
            // Jika development, throw exception untuk melihat detail error
            var env = HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            if (env.IsDevelopment())
            {
                // Return error view dengan detail error untuk development
                return View("Error", new ViewModels.ErrorViewModel 
                { 
                    RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message,
                    StackTrace = ex.StackTrace
                });
            }
            
            // Jika production, redirect ke error page
            return RedirectToAction("Error");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboardStatistics(int? shiftId = null, int? plantId = null, string? machineId = null)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // Ambil shifts
        var shifts = await _context.Shifts.ToListAsync();
        
        // Tentukan shift yang dipilih atau shift saat ini
        Shift? selectedShift = null;
        if (shiftId.HasValue)
        {
            selectedShift = shifts.FirstOrDefault(s => s.Id == shiftId.Value);
        }
        
        if (selectedShift == null)
        {
            foreach (var s in shifts)
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;
                
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
        
        // Tentukan periode shift
        DateTime shiftStart;
        DateTime shiftEnd;
        
        if (selectedShift != null)
        {
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
                    }
                    else if (now < shiftStartToday)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
            else
            {
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today + selectedShift.EndTime;
                
                if (shiftId.HasValue)
                {
                    if (now >= shiftStartToday && now <= shiftEndToday)
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                    }
                    else if (now < shiftStartToday)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
        }
        else
        {
            shiftStart = now.AddHours(-8);
            shiftEnd = now;
        }

        // Filter machines
        var machinesQuery = _context.Machines
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.WorkOrder)
                    .ThenInclude(w => w.Product)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
                    .ThenInclude(d => d.Reason)
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.ProductionCounts)
            .AsQueryable();
        
        if (plantId.HasValue)
        {
            machinesQuery = machinesQuery.Where(m => m.PlantId == plantId.Value);
        }
        
        if (!string.IsNullOrEmpty(machineId))
        {
            machinesQuery = machinesQuery.Where(m => m.Id == machineId);
        }
        
        var machines = await machinesQuery.ToListAsync();

        // ✅ PERBAIKAN: Hitung Planned dan Unplanned Downtime dari semua mesin
        TimeSpan totalShiftTime = shiftEnd - shiftStart;
        TimeSpan totalPlannedDowntime = TimeSpan.Zero;
        TimeSpan totalUnplannedDowntime = TimeSpan.Zero;
        TimeSpan operatingTime = TimeSpan.Zero;
        int totalCount = 0;
        int goodCount = 0;
        double avgStandarCycle = 0;
        int standarCycleCount = 0;

        foreach (var machine in machines)
        {
            var shiftJobRuns = machine.JobRuns
                .Where(j => (j.StartTime >= shiftStart && j.StartTime <= shiftEnd) ||
                           (j.EndTime.HasValue && j.EndTime >= shiftStart && j.EndTime <= shiftEnd) ||
                           (j.StartTime <= shiftStart && (j.EndTime ?? now) >= shiftEnd))
                .ToList();

            var allCounts = shiftJobRuns
                .SelectMany(j => j.ProductionCounts)
                .Where(c => c.Timestamp >= shiftStart && c.Timestamp <= shiftEnd)
                .ToList();

            int mTotal = allCounts.Sum(c => c.GoodCount + c.RejectCount);
            int mGood = allCounts.Sum(c => c.GoodCount);

            totalCount += mTotal;
            goodCount += mGood;

            // ✅ PERBAIKAN: Pisahkan Planned dan Unplanned Downtime, lalu hitung Operating Time
            foreach (var jr in shiftJobRuns)
            {
                var jrStart = jr.StartTime > shiftStart ? jr.StartTime : shiftStart;
                var jrEnd = (jr.EndTime ?? now) < shiftEnd ? (jr.EndTime ?? now) : shiftEnd;
                
                if (jrStart >= jrEnd) continue;
                
                var jrDuration = jrEnd - jrStart;

                // ✅ PERBAIKAN: Pisahkan Planned dan Unplanned Downtime
                var jrPlannedDowntimeSeconds = jr.DowntimeEvents
                    .Where(d => d.Reason != null && d.Reason.Category != "Unplanned")
                    .Where(d => d.StartTime < jrEnd && (d.EndTime ?? now) > jrStart)
                    .Sum(d =>
                    {
                        var dStart = d.StartTime > jrStart ? d.StartTime : jrStart;
                        var dEnd = (d.EndTime ?? now) < jrEnd ? (d.EndTime ?? now) : jrEnd;
                        return (dEnd - dStart).TotalSeconds;
                    });

                var jrUnplannedDowntimeSeconds = jr.DowntimeEvents
                    .Where(d => d.Reason != null && d.Reason.Category == "Unplanned")
                    .Where(d => d.StartTime < jrEnd && (d.EndTime ?? now) > jrStart)
                    .Sum(d =>
                    {
                        var dStart = d.StartTime > jrStart ? d.StartTime : jrStart;
                        var dEnd = (d.EndTime ?? now) < jrEnd ? (d.EndTime ?? now) : jrEnd;
                        return (dEnd - dStart).TotalSeconds;
                    });

                // Accumulate total Planned dan Unplanned Downtime
                totalPlannedDowntime += TimeSpan.FromSeconds(jrPlannedDowntimeSeconds);
                totalUnplannedDowntime += TimeSpan.FromSeconds(jrUnplannedDowntimeSeconds);

                // Operating Time = JobRun duration - Unplanned Downtime (Planned tidak mengurangi Operating Time)
                var netSeconds = Math.Max(0, jrDuration.TotalSeconds - jrUnplannedDowntimeSeconds);
                operatingTime += TimeSpan.FromSeconds(netSeconds);
            }

            // Standar cycle time dari product
            var currentProduct = shiftJobRuns
                .OrderByDescending(j => j.StartTime)
                .FirstOrDefault(j => j.EndTime == null)?
                .WorkOrder?.Product;

            var currentStandarCycle = currentProduct?.StandarCycleTime ?? 0;
            if (currentStandarCycle > 0)
            {
                avgStandarCycle += currentStandarCycle;
                standarCycleCount++;
            }
        }

        if (standarCycleCount > 0)
        {
            avgStandarCycle /= standarCycleCount;
        }

        // ✅ FORMULA SESUAI GAMBAR: Total Downtime = Planned + Unplanned
        TimeSpan totalDowntime = totalPlannedDowntime + totalUnplannedDowntime;

        // ✅ FORMULA SESUAI GAMBAR: Operating Time = Loading Time - Down Time
        // Loading Time = Total Shift Time
        // Down Time = Total Downtime (Planned + Unplanned)
        TimeSpan operatingTimeNew = totalShiftTime - totalDowntime;
        if (operatingTimeNew.TotalSeconds < 0)
        {
            operatingTimeNew = TimeSpan.Zero;
        }

        // ✅ PERBAIKAN: Planned Production Time tetap dihitung untuk display (tidak digunakan di formula OEE)
        TimeSpan plannedProductionTime = totalShiftTime - totalPlannedDowntime;
        if (plannedProductionTime.TotalSeconds < 0)
        {
            plannedProductionTime = TimeSpan.Zero;
        }

        // Jika belum ada data, gunakan durasi shift sebagai default
        if (plannedProductionTime.TotalSeconds == 0 && machines.Count == 0)
        {
            plannedProductionTime = totalShiftTime;
        }

        // ✅ FORMULA SESUAI GAMBAR: Hitung OEE dengan Loading Time dan Down Time
        var oeeResult = _oeeService.CalculateOee(
            totalShiftTime,      // Loading Time
            totalDowntime,        // Down Time (Planned + Unplanned)
            totalCount,
            goodCount,
            avgStandarCycle <= 0 ? 1 : avgStandarCycle);

        return Json(new
        {
            Oee = oeeResult.Oee,
            Availability = oeeResult.Availability,
            Performance = oeeResult.Performance,
            Quality = oeeResult.Quality,
            TotalGood = goodCount,
            TotalReject = totalCount - goodCount,
            TotalCount = totalCount,
            Machines = machines.Select(m => new
            {
                Id = m.Id,
                Name = m.Name,
                Status = m.Status.ToString(),
                LineId = m.LineId
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetHourlyProductionData(int? shiftId = null, int? plantId = null, string? machineId = null)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // Ambil shifts
        var shifts = await _context.Shifts.ToListAsync();
        
        // Tentukan shift yang dipilih atau shift saat ini
        Shift? selectedShift = null;
        if (shiftId.HasValue)
        {
            selectedShift = shifts.FirstOrDefault(s => s.Id == shiftId.Value);
        }
        
        if (selectedShift == null)
        {
            foreach (var s in shifts)
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;
                
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
        
        // Tentukan periode shift
        DateTime shiftStart;
        DateTime shiftEnd;
        
        if (selectedShift != null)
        {
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
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
            else
            {
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today + selectedShift.EndTime;
                
                if (shiftId.HasValue)
                {
                    if (now >= shiftStartToday && now <= shiftEndToday)
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                    }
                    else if (now < shiftStartToday)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
        }
        else
        {
            shiftStart = now.AddHours(-8);
            shiftEnd = now;
        }

        // Filter machines
        var machinesQuery = _context.Machines
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.ProductionCounts)
            .AsQueryable();
        
        if (plantId.HasValue)
        {
            machinesQuery = machinesQuery.Where(m => m.PlantId == plantId.Value);
        }
        
        if (!string.IsNullOrEmpty(machineId))
        {
            machinesQuery = machinesQuery.Where(m => m.Id == machineId);
        }
        
        var machines = await machinesQuery.ToListAsync();

        // Ambil semua ProductionCounts dalam periode shift
        var allCounts = machines
            .SelectMany(m => m.JobRuns)
            .SelectMany(j => j.ProductionCounts)
            .Where(c => c.Timestamp >= shiftStart && c.Timestamp <= shiftEnd)
            .ToList();

        // Group by hour
        var hourlyData = allCounts
            .GroupBy(c => new { c.Timestamp.Date, c.Timestamp.Hour })
            .Select(g => new
            {
                Hour = g.Key.Hour.ToString("00"),
                Label = $"{g.Key.Hour:00}:00",
                Output = g.Sum(c => c.GoodCount + c.RejectCount),
                GoodCount = g.Sum(c => c.GoodCount),
                RejectCount = g.Sum(c => c.RejectCount)
            })
            .OrderBy(x => x.Hour)
            .ToList();

        // Generate labels untuk semua jam dalam shift
        var labels = new List<string>();
        var outputData = new List<int>();
        var goodData = new List<int>();
        var rejectData = new List<int>();

        var startHour = shiftStart.Hour;
        var endHour = shiftEnd.Hour;

        // Handle shift malam
        if (selectedShift != null && selectedShift.EndTime < selectedShift.StartTime)
        {
            // Shift malam: dari startHour sampai 23, lalu 0 sampai endHour
            for (int h = startHour; h < 24; h++)
            {
                labels.Add($"{h:00}:00");
                var hourData = hourlyData.FirstOrDefault(x => x.Hour == h.ToString("00"));
                outputData.Add(hourData?.Output ?? 0);
                goodData.Add(hourData?.GoodCount ?? 0);
                rejectData.Add(hourData?.RejectCount ?? 0);
            }
            for (int h = 0; h <= endHour; h++)
            {
                labels.Add($"{h:00}:00");
                var hourData = hourlyData.FirstOrDefault(x => x.Hour == h.ToString("00"));
                outputData.Add(hourData?.Output ?? 0);
                goodData.Add(hourData?.GoodCount ?? 0);
                rejectData.Add(hourData?.RejectCount ?? 0);
            }
        }
        else
        {
            // Shift normal
            for (int h = startHour; h <= endHour; h++)
            {
                labels.Add($"{h:00}:00");
                var hourData = hourlyData.FirstOrDefault(x => x.Hour == h.ToString("00"));
                outputData.Add(hourData?.Output ?? 0);
                goodData.Add(hourData?.GoodCount ?? 0);
                rejectData.Add(hourData?.RejectCount ?? 0);
            }
        }

        return Json(new
        {
            Labels = labels,
            Output = outputData,
            GoodCount = goodData,
            RejectCount = rejectData
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetDowntimeReasonsData(int? shiftId = null, int? plantId = null, string? machineId = null)
    {
        var now = DateTime.Now;
        var today = now.Date;

        // Ambil shifts
        var shifts = await _context.Shifts.ToListAsync();
        
        // Tentukan shift yang dipilih atau shift saat ini
        Shift? selectedShift = null;
        if (shiftId.HasValue)
        {
            selectedShift = shifts.FirstOrDefault(s => s.Id == shiftId.Value);
        }
        
        if (selectedShift == null)
        {
            foreach (var s in shifts)
            {
                var start = today + s.StartTime;
                var end = today + s.EndTime;
                
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
        
        // Tentukan periode shift
        DateTime shiftStart;
        DateTime shiftEnd;
        
        if (selectedShift != null)
        {
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
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
            else
            {
                var shiftStartToday = today + selectedShift.StartTime;
                var shiftEndToday = today + selectedShift.EndTime;
                
                if (shiftId.HasValue)
                {
                    if (now >= shiftStartToday && now <= shiftEndToday)
                    {
                        shiftStart = shiftStartToday;
                        shiftEnd = shiftEndToday;
                    }
                    else if (now < shiftStartToday)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                }
                else
                {
                    shiftStart = shiftStartToday;
                    shiftEnd = shiftEndToday;
                    
                    if (now < shiftStart)
                    {
                        shiftStart = today.AddDays(-1) + selectedShift.StartTime;
                        shiftEnd = today.AddDays(-1) + selectedShift.EndTime;
                    }
                    else if (now < shiftEnd)
                    {
                        shiftEnd = now;
                    }
                }
            }
        }
        else
        {
            shiftStart = now.AddHours(-8);
            shiftEnd = now;
        }

        // Filter machines
        var machinesQuery = _context.Machines
            .Include(m => m.JobRuns)
                .ThenInclude(j => j.DowntimeEvents)
                    .ThenInclude(d => d.Reason)
            .AsQueryable();
        
        if (plantId.HasValue)
        {
            machinesQuery = machinesQuery.Where(m => m.PlantId == plantId.Value);
        }
        
        if (!string.IsNullOrEmpty(machineId))
        {
            machinesQuery = machinesQuery.Where(m => m.Id == machineId);
        }
        
        var machines = await machinesQuery.ToListAsync();

        // Ambil semua DowntimeEvents dalam periode shift (hanya Unplanned)
        var allDowntimes = machines
            .SelectMany(m => m.JobRuns)
            .SelectMany(j => j.DowntimeEvents)
            .Where(d => d.Reason != null && d.Reason.Category == "Unplanned")
            .Where(d => d.StartTime < shiftEnd && (d.EndTime ?? now) > shiftStart)
            .ToList();

        // Hitung durasi untuk setiap downtime event dalam periode shift
        var downtimeReasonsList = new List<(int ReasonId, string ReasonDescription, double DurationMinutes)>();
        
        foreach (var d in allDowntimes)
        {
            var endTime = (d.EndTime ?? now) < shiftEnd ? (d.EndTime ?? now) : shiftEnd;
            var startTime = d.StartTime > shiftStart ? d.StartTime : shiftStart;
            var duration = endTime - startTime;
            var durationMinutes = duration.TotalMinutes > 0 ? duration.TotalMinutes : 0;
            
            downtimeReasonsList.Add((
                d.ReasonId,
                d.Reason?.Description ?? "Unknown",
                durationMinutes
            ));
        }
        
        var downtimeReasons = downtimeReasonsList
            .GroupBy(d => new { d.ReasonId, d.ReasonDescription })
            .Select(g => new
            {
                ReasonDescription = g.Key.ReasonDescription,
                DurationMinutes = g.Sum(d => d.DurationMinutes)
            })
            .OrderByDescending(d => d.DurationMinutes)
            .Take(5)
            .ToList();

        return Json(new
        {
            Labels = downtimeReasons.Select(d => d.ReasonDescription).ToList(),
            Data = downtimeReasons.Select(d => (int)Math.Round(d.DurationMinutes)).ToList()
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var errorViewModel = new ViewModels.ErrorViewModel 
        { 
            RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier 
        };
        
        // Coba ambil error dari exception handler middleware
        var exceptionHandlerPathFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (exceptionHandlerPathFeature != null)
        {
            var exception = exceptionHandlerPathFeature.Error;
            errorViewModel.ErrorMessage = exception.Message;
            errorViewModel.StackTrace = exception.StackTrace;
            
            if (exception.InnerException != null)
            {
                errorViewModel.ErrorMessage += $"\n\nInner Exception: {exception.InnerException.Message}";
            }
        }
        
        return View(errorViewModel);
    }
}


