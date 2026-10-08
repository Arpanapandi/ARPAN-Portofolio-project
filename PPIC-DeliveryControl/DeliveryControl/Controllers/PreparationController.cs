using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Hubs;
using DeliveryControl.Services;
using System.Globalization;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    /// <summary>
    /// Controller khusus untuk Portal Preparation - konfirmasi masuk dock
    /// </summary>
    [Authorize]
    [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader", "User")]
    public class PreparationController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly ActivityLogService _logService;
        private readonly DeliveryControl.Services.StockCacheService _stockCache;

        public PreparationController(ApplicationDbContext context, IHubContext<DeliveryHub> hubContext, ActivityLogService logService, DeliveryControl.Services.StockCacheService stockCache)
        {
            _context = context;
            _hubContext = hubContext;
            _logService = logService;
            _stockCache = stockCache;
        }

        private static DateTime GetBusinessNow()
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static bool IsScheduleCompleted(DeliverySchedule s)
        {
            if (s.ActualEndTime.HasValue) return true;
            if (string.Equals((s.Status ?? string.Empty).Trim(), "Completed", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.DeliveryItems != null && s.DeliveryItems.Any())
            {
                return s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
            }
            return false;
        }

        private static bool IsPreparationH1Schedule(DeliverySchedule s, DateTime currentDate)
        {
            if (s.ScheduledDate.Date != currentDate.AddDays(1)) return false;
            if (IsScheduleCompleted(s)) return false;

            // 1. Jika Pickup pagi (sebelum 12:00 siang), selalu tampilkan di H-1
            if (s.PickupTime.HasValue && s.PickupTime.Value.Hour < 12) return true;

            // 2. Jika Pickup siang/sore (>= 12:00), cek waktu mulai persiapan (StartPrepareTime)
            // Jika jadwal diset untuk mulai persiapan dini hari besok (< 07:00 pagi atau < 420 menit),
            // berarti itu adalah bagian dari pekerjaan Shift 3 hari ini. Tampilkan!
            var startPrepMins = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);
            if (startPrepMins > 0 && startPrepMins < 420) return true;

            return false;
        }

        private DateTime NormalizeTargetOnScheduleDate(DeliverySchedule s, DateTime target)
        {
            var normalized = s.ScheduledDate.Date.Add(target.TimeOfDay);
            var startPrepareMinutes = s.StartPrepareTime > 0 ? s.StartPrepareTime : (s.Customer?.StartPrepareTime ?? 0);

            if (startPrepareMinutes > 0)
            {
                var startPrepAt = s.ScheduledDate.Date.AddMinutes(startPrepareMinutes);
                if (normalized < startPrepAt)
                    normalized = normalized.AddDays(1);
            }

            return normalized;
        }

        private DateTime? GetScheduleDueTime(DeliverySchedule s)
        {
            if (s.PickupTime.HasValue)
                return NormalizeTargetOnScheduleDate(s, s.PickupTime.Value);

            if (s.ETD.HasValue)
                return NormalizeTargetOnScheduleDate(s, s.ETD.Value);

            if (s.EnterDockTime.HasValue)
                return NormalizeTargetOnScheduleDate(s, s.EnterDockTime.Value);

            return s.ScheduledDate.Date.AddDays(1).AddSeconds(-1);
        }

        private bool ShouldBeDelayed(DeliverySchedule s, DateTime now)
        {
            if (s.ActualEndTime.HasValue) return false;
            if (string.Equals(s.Status, "Completed", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)) return false;

            var dueAt = GetScheduleDueTime(s);
            var isCrossDay = dueAt.HasValue && dueAt.Value.Date > s.ScheduledDate.Date;
            if (!isCrossDay && s.ScheduledDate.Date < now.Date) return true;

            return dueAt.HasValue && now > dueAt.Value;
        }

        // GET: Preparation - Daftar schedule hari ini untuk preparation
        public async Task<IActionResult> Index(DateTime? selectedDate, int? customerId, string? status, string? cycle)
        {
            var enterDockDate = selectedDate ?? DateTime.Today;
            var tomorrow = enterDockDate.AddDays(1);
            
            // Normalize status & cycle - trim dan pastikan tidak null
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? string.Empty : status.Trim();
            var normalizedCycle = string.IsNullOrWhiteSpace(cycle) ? string.Empty : cycle!.Trim();
            
            ViewData["SelectedDate"] = enterDockDate.ToString("yyyy-MM-dd");
            ViewData["SelectedCustomerId"] = customerId;
            ViewData["SelectedStatus"] = normalizedStatus;
            ViewData["DayName"] = enterDockDate.ToString("dddd, dd MMMM yyyy", new CultureInfo("id-ID"));
            ViewData["SelectedCycle"] = normalizedCycle;

            // 1. Dapatkan izin dock user
            var userIdStr = HttpContext.Session.GetString("UserId");
            var sessionRole = HttpContext.Session.GetString("Role");
            // Super Admin di-hardcode sama seperti Admin — akses semua dock tanpa filter
            var isUserAdmin = sessionRole == "Admin" || sessionRole == "Super Admin";
            var isLeader = sessionRole == "Leader";
            
            // Cek apakah user punya HasAllDockAccess flag
            bool hasAllDockAccess = false;
            if (int.TryParse(userIdStr, out int userId))
            {
                var user = await _context.Users.FindAsync(userId);
                hasAllDockAccess = user?.HasAllDockAccess ?? false;
            }
            
            List<int> allowedDockIds = new List<int>();
            // Super Admin, Admin, atau user dengan HasAllDockAccess tidak perlu filter dock - bisa lihat semua
            if (!isUserAdmin && !hasAllDockAccess && int.TryParse(userIdStr, out int userIdForDock))
            {
                allowedDockIds = await _context.UserDockAccesses
                    .Where(uda => uda.UserId == userIdForDock)
                    .Select(uda => uda.DockId)
                    .ToListAsync();
            }

            // 2. Ambil SEMUA schedule aktif
            var query = _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled");

            // Filter oleh dock jika bukan admin/superadmin dan tidak HasAllDockAccess
            if (!isUserAdmin && !hasAllDockAccess)
            {
                if (!allowedDockIds.Any())
                {
                    // Tidak ada dock yang di-assign → tampilkan kosong
                    query = query.Where(s => false);
                }
                else
                {
                    // Ambil semua kode & nama dock yang diizinkan (case-insensitive)
                    var dockData = await _context.Docks
                        .Where(d => allowedDockIds.Contains(d.DockId))
                        .Select(d => new { d.DockCode, d.DockName })
                        .ToListAsync();

                    var allowedCodeSet = dockData
                        .SelectMany(d => new[] { d.DockCode ?? "", d.DockName ?? "" })
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s.Trim().ToUpper())
                        .ToHashSet();

                    // Filter: cocokkan schedule ke dock via Area, Docking, CustomerName, atau Customer.Area
                    var allMatching = await query.ToListAsync();
                    var filteredIds = allMatching
                        .Where(s =>
                            allowedCodeSet.Contains((s.Area ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.Docking ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.CustomerName ?? "").Trim().ToUpper()) ||
                            allowedCodeSet.Contains((s.Customer?.Area ?? "").Trim().ToUpper()))
                        .Select(s => s.ScheduleId)
                        .ToHashSet();

                    query = query.Where(s => filteredIds.Contains(s.ScheduleId));
                }
            }


            var allSchedules = await query.ToListAsync();
            
            // Filter berdasarkan TANGGAL OPERASIONAL (ScheduledDate)
            // Sertakan juga:
            // - Jadwal H-1: besok yang perlu di-prepare hari ini (StartPrepareTime dikonfigurasi)
            // - Carry-over: jadwal dari hari sebelumnya yang belum selesai (sama seperti Dashboard Shipping)
            var schedulesForToday = allSchedules
                .Where(s =>
                    s.ScheduledDate.Date == enterDockDate.Date ||
                    // H-1: jadwal besok yang perlu di-prepare hari ini
                    IsPreparationH1Schedule(s, enterDockDate) ||
                    // CARRY OVER: jadwal dari hari sebelumnya yang belum selesai
                    (s.ScheduledDate.Date < enterDockDate.Date &&
                     !IsScheduleCompleted(s)));
            bool anyStatusCorrected = false;
            var now = GetBusinessNow();
            foreach (var s in schedulesForToday)
            {
                // Auto-detect DELAYED dengan aturan tanggal+jam dan pengecualian lintas-hari/H+1
                if (ShouldBeDelayed(s, now))
                {
                    if (s.Status != "Delayed") { s.Status = "Delayed"; anyStatusCorrected = true; }
                }

                if (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                {
                    if (s.PreparationStatus != "Prepared")
                    {
                        s.PreparationStatus = "Prepared";
                        anyStatusCorrected = true;
                    }

                    if (s.ActualEnterDockTime.HasValue)
                    {
                        // Only "Completed" if it has ActualEndTime (Delivery Finished)
                        if (s.ActualEndTime.HasValue)
                        {
                            if (s.Status != "Completed") { s.Status = "Completed"; anyStatusCorrected = true; }
                        }
                        else
                        {
                            if (s.Status != "In Progress") { s.Status = "In Progress"; anyStatusCorrected = true; }
                        }
                    }
                    else
                    {
                        if (s.Status != "In Progress") { s.Status = "In Progress"; anyStatusCorrected = true; }
                    }
                }

                // Schedule tanpa item tidak memerlukan scan apa pun → otomatis Prepared
                if ((s.DeliveryItems == null || !s.DeliveryItems.Any()) &&
                    s.Status != "Cancelled" &&
                    s.PreparationStatus != "Prepared")
                {
                    s.PreparationStatus = "Prepared";
                    s.ReadyToDockTime ??= now;
                    anyStatusCorrected = true;
                }
            }
            // Simpan koreksi status ke DB agar konsisten di semua portal
            if (anyStatusCorrected)
                await _context.SaveChangesAsync();

            // ── AUTO ENTER DOCK: cek group yang sudah semua Prepared + mode aktif ──────────
            {
                var autoDockSetting = await _context.SystemSettings
                    .FirstOrDefaultAsync(ss => ss.Key == "AutoEnterDockMode");
                bool autoEnterDockActive = autoDockSetting != null && autoDockSetting.Value == "1";

                var skipLeaderSetting = await _context.SystemSettings
                    .FirstOrDefaultAsync(ss => ss.Key == "SkipLeaderVerificationMode");
                bool skipLeaderVerifActive = skipLeaderSetting != null && skipLeaderSetting.Value == "1";

                ViewBag.AutoEnterDockActive = autoEnterDockActive;
                ViewBag.SkipLeaderVerificationActive = skipLeaderVerifActive;

                if (autoEnterDockActive)
                {
                    // Kelompokkan jadwal yang belum masuk dock
                    var pendingGroups = schedulesForToday
                        .Where(s => !s.ActualEnterDockTime.HasValue && s.Status != "Cancelled")
                        .GroupBy(s => new {
                            CustId = s.CustomerId,
                            Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                            Route = (s.Route ?? "").Trim().ToUpper(),
                            Area  = (string.IsNullOrWhiteSpace(s.Area)
                                        ? (s.Customer?.Docking ?? "")
                                        : s.Area).Trim().ToUpper(),
                            Date  = s.ScheduledDate.Date
                        });

                    bool anyDockSet = false;
                    foreach (var grp in pendingGroups)
                    {
                        // Semua schedule di group harus Prepared (0-item dianggap Prepared)
                        bool allPrepared = grp.All(s =>
                            s.PreparationStatus == "Prepared" ||
                            s.DeliveryItems == null || !s.DeliveryItems.Any());
                        if (!allPrepared) continue;

                        // Semua schedule dengan item harus kanban 100%
                        bool allKanbanDone = grp.All(s =>
                            s.DeliveryItems == null || !s.DeliveryItems.Any() ||
                            s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity));
                        if (!allKanbanDone) continue;

                        // Jika skip verifikasi leader tidak aktif, wajib tunggu verifikasi leader selesai
                        if (!skipLeaderVerifActive)
                        {
                            bool allLeaderVerified = grp.All(s => s.IsLeaderVerified || s.DeliveryItems == null || !s.DeliveryItems.Any());
                            if (!allLeaderVerified) continue;
                        }

                        foreach (var gs in grp)
                        {
                            if (!gs.ActualEnterDockTime.HasValue)
                            {
                                gs.ActualEnterDockTime = now;
                                gs.Status = "In Progress";
                                gs.DriverStatus = "In Progress";
                                gs.PreparationStatus = "Prepared";
                                gs.ReadyToDockTime ??= now;
                                anyDockSet = true;
                            }
                        }
                    }

                    if (anyDockSet)
                    {
                        await _context.SaveChangesAsync();
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "enterDock" });
                    }
                }
            }
            
            // Filter by customer jika ada
            if (customerId.HasValue && customerId.Value > 0)
            {
                schedulesForToday = schedulesForToday
                    .Where(s => s.CustomerId == customerId.Value)
                    .ToList();
            }

            // Filter by status jika ada
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                schedulesForToday = schedulesForToday
                    .Where(s => (s.PreparationStatus ?? s.Status) == normalizedStatus)
                    .ToList();
            }

            // Filter by cycle jika ada
            if (!string.IsNullOrWhiteSpace(normalizedCycle))
            {
                schedulesForToday = schedulesForToday
                    .Where(s => string.Equals(s.Cycle, normalizedCycle, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Data customer untuk dropdown: SEMUA CUSTOMER AKTIF
            var customers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();
            
            ViewBag.Customers = customers;

            // Data cycle untuk dropdown filter cycle — include jadwal H-1
            var availableCycles = allSchedules
                .Where(s => s.ScheduledDate.Date == enterDockDate.Date ||
                            (s.ScheduledDate.Date == tomorrow.Date &&
                             (s.StartPrepareTime > 0 || (s.Customer != null && s.Customer.StartPrepareTime > 0))))
                .Select(s => s.Cycle)
                .Where(cy => !string.IsNullOrWhiteSpace(cy))
                .Distinct()
                .OrderBy(cy => cy)
                .ToList();
            ViewBag.Cycles = availableCycles;

            // =====================================================================
            // GROUPING LOGIC - Kartu digroup berdasarkan:
            // Cycle + Route + Effective Dock + ScheduledDate
            // KRITIS: ScheduledDate HARUS ikut di key agar jadwal carry-over (hari lama)
            //         tidak digabung dengan jadwal baru (hari ini) walau Cycle+Route+Area sama.
            // KRITIS: s.Area bisa kosong di beberapa record, maka fallback ke Customer.Docking
            // =====================================================================
            var groupedSchedules = schedulesForToday
                .GroupBy(s => new {
                    // Grouping per CUSTOMER per TRIP per TANGGAL.
                    // Setiap customer punya truk sendiri — konsisten dengan Dashboard Shipping (CustId+Cycle+Route+Area+Date).
                    CustId = s.CustomerId,
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    // Effective Dock: gunakan Area jika ada, fallback ke Customer.Docking
                    Area = (string.IsNullOrWhiteSpace(s.Area)
                                ? (s.Customer?.Docking ?? "")
                                : s.Area).Trim().ToUpper(),
                    // Tanggal jadwal — pisahkan carry-over dari jadwal hari ini
                    Date = s.ScheduledDate.Date
                })
                .Select(g => {
                    var first       = g.First();
                    var sortedGroup = g.OrderBy(x => x.ScheduleNumber).ToList();

                    var uniqueCustomers = g.Select(x => x.Customer?.CustomerName ?? "-").Distinct().ToList();
                    var customerDisplay = uniqueCustomers.Count > 1
                        ? string.Join(", ", uniqueCustomers)
                        : (uniqueCustomers.FirstOrDefault() ?? "-");

                    // Status Logic for the Group
                    // "Prepared" (biru) = semua item sudah di-scan, siap masuk dock.
                    // IsLeaderVerified menentukan apakah perlu klik verifikasi leader atau bisa langsung Enter Dock.
                    // Keduanya tetap "Prepared" — Mode Otomatis pakai bypass form, manual pakai Leader Verification.
                    // Schedule tanpa item dianggap Prepared (tidak ada yang perlu di-scan).
                    var isAllPrepared     = g.All(x => x.PreparationStatus == "Prepared" || !x.DeliveryItems.Any());
                    var hasAnyEnterDock   = g.Any(x => x.ActualEnterDockTime.HasValue);
                    var hasAnyScanProgress = g.Any(x => x.PreparationStatus == "In Progress" || x.PreparationStatus == "Prepared");

                    var groupStatus = "Scheduled";
                    if (hasAnyEnterDock)        groupStatus = "Completed";  // Hijau
                    else if (isAllPrepared)      groupStatus = "Prepared";   // Biru
                    else if (hasAnyScanProgress) groupStatus = "In Progress"; // Oren

                    // Carry-over: semua jadwal di group ini tanggalnya sebelum enterDockDate
                    var isCarryOver = sortedGroup.All(x => x.ScheduledDate.Date < enterDockDate.Date);

                    return new DeliveryControl.Models.ViewModels.DriverTripViewModel
                    {
                        RepresentativeScheduleId = first.ScheduleId,
                        CustomerName   = customerDisplay,
                        Cycle          = first.Cycle ?? "",
                        Route          = first.Route ?? "",
                        Area           = first.Area ?? "",
                        PickupTime     = g.Where(x => x.PickupTime.HasValue).Select(x => x.PickupTime).Min(),
                        ETD            = g.Where(x => x.ETD.HasValue).Select(x => x.ETD).Max(),
                        ActualStartTime = g.Where(x => x.ActualStartTime.HasValue).Select(x => x.ActualStartTime).Min(),
                        ActualEndTime  = g.Where(x => x.ActualEndTime.HasValue).Select(x => x.ActualEndTime).Max(),
                        DriverStatus   = first.DriverStatus ?? "Scheduled",
                        OverallStatus  = groupStatus,
                        IsCarryOver    = isCarryOver,
                        ScheduledDate  = first.ScheduledDate,
                        Schedules      = sortedGroup
                    };
                })
                .OrderBy(vm => vm.OverallStatus == "In Progress" ? 0 : (vm.OverallStatus == "Prepared" ? 1 : (vm.OverallStatus == "Scheduled" ? 2 : 3)))
                .ThenBy(vm => vm.PickupTime ?? vm.ETD ?? DateTime.MaxValue)
                .ToList();


            // Kelompokkan menjadi:
            // 1. BUTUH AKSI PREPARATION (BELUM MASUK DOCK/PREPARED)
            // 2. SUDAH MASUK DOCK / PREPARED
            
            // Using logic from previous code: needAction are those NOT completed (not all entered dock)
            // But wait, the previous logic was: !s.ActualEnterDockTime.HasValue
            
            var needAction = groupedSchedules
                .Where(vm => vm.OverallStatus != "Completed")
                .ToList();

            // Role Leader: hanya tampilkan card yang status "Prepared" (butuh verifikasi leader)
            // Card "Scheduled" dan "In Progress" tidak relevan untuk Leader
            if (sessionRole == "Leader")
            {
                needAction = needAction
                    .Where(vm => vm.OverallStatus == "Prepared")
                    .ToList();
            }
            
            var completed = groupedSchedules
                .Where(vm => vm.OverallStatus == "Completed")
                .ToList();
            
            var finalModel = needAction.Concat(completed).ToList();

            // Statistics untuk tampilan (Count of Groups)
            ViewBag.TotalSchedules = finalModel.Count;
            ViewBag.NotEnteredYet = needAction.Count;
            ViewBag.AlreadyEntered = completed.Count;

            ViewBag.NeedActionSchedules = needAction;
            ViewBag.CompletedSchedules = completed;
            
            return View(finalModel);
        }

        // GET: Preparation/EnterDock/5 - Halaman konfirmasi masuk dock
        public async Task<IActionResult> EnterDock(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Cek apakah sudah ada enter dock time
            if (schedule.ActualEnterDockTime.HasValue)
            {
                TempData["WarningMessage"] = $"Schedule ini sudah dikonfirmasi masuk dock pada {schedule.ActualEnterDockTime.Value:dd/MM/yyyy HH:mm}";
            }

            return View(schedule);
        }

        // POST: Preparation/EnterDock/5 - Konfirmasi masuk dock
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> EnterDock(int id, DateTime enterDockTime, string? notes)
        {
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null) return NotFound();

            // Guard: validasi kanban 100% menggunakan QtyLot
            var notComplete = schedule.DeliveryItems
                .Where(di => {
                    var qtyLot = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                    var kanbanTarget = (int)Math.Ceiling((double)di.Quantity / qtyLot);
                    var kanbanActual = (int)Math.Ceiling((double)(di.ActualQuantity ?? 0) / qtyLot);
                    return kanbanTarget > 0 && kanbanActual < kanbanTarget;
                })
                .ToList();
            if (notComplete.Any())
            {
                TempData["ErrorMessage"] = $"❌ Kanban belum 100%. Selesaikan scan semua kanban terlebih dahulu sebelum Enter Dock.";
                return RedirectToAction(nameof(EnterDock), new { id });
            }

            try
            {
                schedule.ActualEnterDockTime = enterDockTime;
                
                // Saat Enter Dock, selalu set PreparationStatus = Prepared
                // agar card muncul di Driver Portal (filter: ActualEnterDockTime.HasValue)
                schedule.PreparationStatus = "Prepared";
                
                // Set Status = In Progress agar card aktif di Driver Portal
                if (schedule.Status != "Completed")
                {
                    schedule.Status = "In Progress";
                }
                
                schedule.UpdatedDate = DateTime.Now;
                schedule.UpdatedBy = User.Identity?.Name ?? "Preparation";
                
                // Tambahkan notes jika ada
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    schedule.Notes = string.IsNullOrWhiteSpace(schedule.Notes) 
                        ? $"[Enter Dock] {notes}" 
                        : schedule.Notes + $"\n[Enter Dock] {notes}";
                }

                await _context.SaveChangesAsync();

                // Log activity
                await _logService.LogConfirm(
                    "Preparation",
                    schedule.ScheduleNumber ?? "UNKNOWN",
                    schedule.ScheduleId,
                    $"Konfirmasi masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                    User.Identity?.Name ?? "Preparation"
                );

                // Broadcast update via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    ScheduleNumber = schedule.ScheduleNumber,
                    Action = "enterDock",
                    Message = $"Truk telah masuk dock untuk {schedule.Customer?.CustomerName} pada {enterDockTime:HH:mm}",
                    Timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Masuk dock berhasil dikonfirmasi pada {enterDockTime:HH:mm}!";
                // Redirect ke tanggal enter dock (bukan scheduled date)
                var redirectDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate;
                return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error: {ex.Message}";
                return RedirectToAction(nameof(EnterDock), new { id });
            }
        }

        // GET: Preparation/Details/5 - Detail schedule untuk preparation
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // LOGIC: Hitung Prepared Qty
            // Ambil tanggal referensi (Enter Dock Time atau Scheduled Date)
            var refDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate.Date;

            // Ambil preparation records pada tanggal tersebut untuk items yang ada di schedule ini
            // Kita ambil semua record hari ini untuk efisiensi query, lalu filter di memori atau query specific tags
            var relevantTags = schedule.DeliveryItems.Select(di => di.Item.ItemCode.ToUpper()).ToList();
            
            var preparationsToday = await _context.PreparationRecords
                .Where(p => p.CreatedDate.Date == refDate)
                .Select(p => p.Tag)
                .ToListAsync();

            // Hitung frequency per Tag (Case Insensitive)
            var prepCounts = preparationsToday
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t.Trim().ToUpper())
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.PrepCounts = prepCounts;

            return View(schedule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> GroupReadyToDock(int id)
        {
            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null) return NotFound();

            // Gunakan CustId+Cycle+Route+Area+Date — konsisten dengan GroupBy portal dan Dashboard Shipping
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.CustomerId == repSchedule.CustomerId &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area)
                .ToListAsync();

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                if (s.PreparationStatus != "Prepared")
                {
                    s.PreparationStatus = "Prepared";
                    s.Status = "In Progress"; // MUST be In Progress for Driver Portal visibility
                }
                
                // Only record ReadyToDockTime when kanban is 100% scanned
                var allItemsDone = s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
                if (!s.ReadyToDockTime.HasValue && allItemsDone)
                {
                    s.ReadyToDockTime = now;
                }
                
                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
                
                // Calculate ActPrepareTime (minutes) from first preparation record if available
                var firstPrep = await _context.PreparationRecords
                    .Where(pr => pr.ScheduleId == s.ScheduleId)
                    .OrderBy(pr => pr.CreatedDate)
                    .FirstOrDefaultAsync();
                
                if (firstPrep != null)
                {
                    s.ActPrepareTime = (now - firstPrep.CreatedDate).TotalMinutes;
                }
            }

            await _context.SaveChangesAsync();
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "readyToDock", manifest = repSchedule.Cycle + "-" + repSchedule.Area });

            return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Preparation", "Leader")]
        public async Task<IActionResult> QuickEnterDock(int id, bool bypassVerification = false, string? returnDate = null)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null)
            {
                if (isAjax) return Json(new { success = false, message = "Schedule tidak ditemukan" });
                return NotFound();
            }

            // Gunakan CustId+Cycle+Route+Area+Date — konsisten dengan GroupBy portal dan Dashboard Shipping
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.CustomerId == repSchedule.CustomerId &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area)
                .ToListAsync();

            // Mode settings check
            var autoDockSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(ss => ss.Key == "AutoEnterDockMode");
            bool autoEnterDockActive = autoDockSetting != null && autoDockSetting.Value == "1";

            var skipLeaderSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(ss => ss.Key == "SkipLeaderVerificationMode");
            bool skipLeaderVerifActive = skipLeaderSetting != null && skipLeaderSetting.Value == "1";

            var totalTarget = schedules.Sum(s => s.DeliveryItems.Sum(di => di.Quantity));
            var totalActual = schedules.Sum(s => s.DeliveryItems.Sum(di => di.ActualQuantity ?? 0));

            // Guard: validasi kanban
            // Jika Mode Otomatis aktif: wajib 100% kanban terpenuhi
            // Jika Mode Otomatis mati (Manual): diizinkan enter dock jika sudah ada minimal 1 kanban yang di-scan (totalActual > 0)
            if (autoEnterDockActive)
            {
                var notComplete = schedules
                    .Where(s => s.DeliveryItems.Any() && !s.DeliveryItems.All(di => {
                        var qtyLot = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                        var kanbanTarget = (int)Math.Ceiling((double)di.Quantity / qtyLot);
                        var kanbanActual = (int)Math.Ceiling((double)(di.ActualQuantity ?? 0) / qtyLot);
                        return kanbanTarget == 0 || kanbanActual >= kanbanTarget;
                    }))
                    .ToList();
                if (notComplete.Any())
                {
                    if (isAjax) return Json(new { success = false, message = "Mode Otomatis aktif: Kanban belum 100%. Selesaikan scan semua kanban terlebih dahulu." });
                    TempData["ErrorMessage"] = $"❌ Mode Otomatis aktif: Kanban belum 100%. Selesaikan scan semua kanban terlebih dahulu sebelum Enter Dock.";
                    var errorRedirectDate = DateTime.TryParse(returnDate, out var parsedErrorReturn)
                        ? parsedErrorReturn
                        : (repSchedule.ScheduledDate.Date > DateTime.Today ? DateTime.Today : repSchedule.ScheduledDate);
                    return RedirectToAction(nameof(Index), new { selectedDate = errorRedirectDate.ToString("yyyy-MM-dd") });
                }
            }
            else
            {
                // Mode Manual: Cek apakah minimal ada 1 kanban discan jika schedule memiliki target item
                if (totalTarget > 0 && totalActual <= 0)
                {
                    if (isAjax) return Json(new { success = false, message = "Belum ada kanban yang di-scan. Minimal scan 1 kanban sebelum Enter Dock." });
                    TempData["ErrorMessage"] = $"❌ Belum ada kanban yang di-scan. Minimal scan 1 kanban sebelum Enter Dock.";
                    var errorRedirectDate = DateTime.TryParse(returnDate, out var parsedErrorReturn)
                        ? parsedErrorReturn
                        : (repSchedule.ScheduledDate.Date > DateTime.Today ? DateTime.Today : repSchedule.ScheduledDate);
                    return RedirectToAction(nameof(Index), new { selectedDate = errorRedirectDate.ToString("yyyy-MM-dd") });
                }
            }

            // Guard: verifikasi leader
            // Bypass diizinkan jika SkipLeaderVerificationMode aktif atau Admin bypass
            var sessionRole = HttpContext.Session.GetString("Role");
            var isAdmin = sessionRole == "Admin" || sessionRole == "Super Admin";
            var shouldBypass = (bypassVerification && isAdmin) || skipLeaderVerifActive;

            if (!shouldBypass)
            {
                // Jadwal dengan item yang sudah mulai discan wajib diverifikasi leader
                var notVerified = schedules.Where(s => !s.IsLeaderVerified && s.DeliveryItems.Any(di => (di.ActualQuantity ?? 0) > 0)).ToList();
                if (notVerified.Any())
                {
                    if (isAjax) return Json(new { success = false, message = $"{notVerified.Count} manifest belum diverifikasi leader." });
                    TempData["ErrorMessage"] = $"❌ {notVerified.Count} manifest belum diverifikasi leader. Lakukan Leader Verification terlebih dahulu.";
                    return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
                }
            }

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                s.ActualEnterDockTime = now;
                s.Status = "In Progress";
                bool isItemComplete = !s.DeliveryItems.Any() || s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
                s.PreparationStatus = isItemComplete ? "Prepared" : "In Progress";

                if (!s.ReadyToDockTime.HasValue)
                    s.ReadyToDockTime = now;

                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
            }

            await _context.SaveChangesAsync();

            // Log activity for the group
            await _logService.LogConfirm(
                "Preparation",
                repSchedule.ScheduleNumber ?? "GROUP",
                repSchedule.ScheduleId,
                $"Group konfirmasi masuk dock untuk {repSchedule.Customer?.CustomerName} pada {now:HH:mm}",
                User.Identity?.Name ?? "Preparation"
            );

            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { 
                action = "enterDock", 
                scheduleNumber = repSchedule.ScheduleNumber,
                timestamp = now 
            });

            if (isAjax) return Json(new { success = true, message = "Enter Dock berhasil", time = now.ToString("HH:mm") });
            return RedirectToAction(nameof(Index), new { selectedDate = repSchedule.ScheduledDate });
        }

        // POST: Preparation/ConfirmCustomEnterDock - Konfirmasi / atur waktu masuk dock sesuai kebutuhan (bisa custom / on-time)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader")]
        public async Task<IActionResult> ConfirmCustomEnterDock(int id, DateTime enterDockTime, string? notes = null, string? returnDate = null)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null)
            {
                if (isAjax) return Json(new { success = false, message = "Schedule tidak ditemukan" });
                return NotFound();
            }

            // Ambil semua schedule dalam grup trip (CustId + Cycle + Route + Area + Date)
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.CustomerId == repSchedule.CustomerId &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area)
                .ToListAsync();

            var now = DateTime.Now;
            foreach (var s in schedules)
            {
                s.ActualEnterDockTime = enterDockTime;
                if (s.Status != "Completed")
                {
                    s.Status = "In Progress";
                }
                bool isItemComplete = !s.DeliveryItems.Any() || s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);
                s.PreparationStatus = isItemComplete ? "Prepared" : "In Progress";

                if (!s.ReadyToDockTime.HasValue)
                    s.ReadyToDockTime = enterDockTime;

                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
            }

            await _context.SaveChangesAsync();

            // Log activity for the group
            await _logService.LogConfirm(
                "Preparation",
                repSchedule.ScheduleNumber ?? "GROUP",
                repSchedule.ScheduleId,
                $"Konfirmasi waktu masuk dock untuk {repSchedule.Customer?.CustomerName} diatur menjadi {enterDockTime:dd/MM/yyyy HH:mm}. {(string.IsNullOrWhiteSpace(notes) ? "" : "Catatan: " + notes)}",
                User.Identity?.Name ?? "Preparation"
            );

            // Broadcast real-time ke Dashboard Shipping & Gantt Chart
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { 
                action = "enterDock", 
                scheduleNumber = repSchedule.ScheduleNumber,
                timestamp = enterDockTime 
            });

            if (isAjax) 
            {
                return Json(new { 
                    success = true, 
                    message = $"Waktu dock berhasil diatur ke {enterDockTime:HH:mm}", 
                    time = enterDockTime.ToString("HH:mm") 
                });
            }

            var redirectDate = DateTime.TryParse(returnDate, out var parsedReturn)
                ? parsedReturn
                : repSchedule.ScheduledDate;
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate.ToString("yyyy-MM-dd") });
        }

        // POST: Preparation/ConfirmCustomPickup - Konfirmasi / atur waktu keberangkatan truk (Pickup) sesuai kebutuhan (bisa custom / on-time)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader", "User")]
        public async Task<IActionResult> ConfirmCustomPickup(int id, DateTime pickupTime, string? notes = null, string? returnDate = null)
        {
            bool isAjax = Request.Headers["X-Requested-With"] == "XMLHttpRequest";

            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null)
            {
                if (isAjax) return Json(new { success = false, message = "Schedule tidak ditemukan" });
                return NotFound();
            }

            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.CustomerId == repSchedule.CustomerId &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area)
                .ToListAsync();

            var groupIds = schedules.Select(s => s.ScheduleId).ToList();
            var allDeliveryItems = await _context.DeliveryItems
                .Where(di => groupIds.Contains(di.ScheduleId))
                .ToListAsync();

            var incompleteItems = allDeliveryItems
                .Where(di => (di.ActualQuantity ?? 0) < di.Quantity)
                .ToList();

            bool isKanbanComplete = !incompleteItems.Any();
            string targetStatus = isKanbanComplete ? "Completed" : "Shortage Delivery";

            var actualPickup = pickupTime != default ? pickupTime : DateTime.Now;
            var now = DateTime.Now;

            foreach (var s in schedules)
            {
                s.ActualPickupTime = actualPickup;
                if (!s.ActualStartTime.HasValue)
                {
                    s.ActualStartTime = actualPickup;
                }
                s.ActualEndTime = actualPickup;
                s.Status = targetStatus;
                s.DriverStatus = targetStatus;
                s.UpdatedDate = now;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";

                if (!string.IsNullOrWhiteSpace(notes))
                {
                    s.Notes = string.IsNullOrWhiteSpace(s.Notes) 
                        ? $"[Pickup] {notes}" 
                        : s.Notes + $"\n[Pickup] {notes}";
                }
            }

            await _context.SaveChangesAsync();

            await _logService.LogConfirm(
                "Preparation",
                repSchedule.ScheduleNumber ?? "GROUP",
                repSchedule.ScheduleId,
                $"Konfirmasi waktu pickup (keberangkatan) untuk {repSchedule.Customer?.CustomerName} diatur menjadi {actualPickup:dd/MM/yyyy HH:mm}. Status: {targetStatus}. {(string.IsNullOrWhiteSpace(notes) ? "" : "Catatan: " + notes)}",
                User.Identity?.Name ?? "Preparation"
            );

            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { 
                action = "pickup", 
                scheduleNumber = repSchedule.ScheduleNumber,
                timestamp = actualPickup 
            });

            if (isAjax) 
            {
                return Json(new { 
                    success = true, 
                    message = $"Waktu Pickup berhasil diatur ke {actualPickup:HH:mm} ({targetStatus})", 
                    time = actualPickup.ToString("HH:mm") 
                });
            }

            var redirectDate = DateTime.TryParse(returnDate, out var parsedReturn)
                ? parsedReturn
                : repSchedule.ScheduledDate;
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate.ToString("yyyy-MM-dd") });
        }

        // GET: Preparation/GetAutoEnterDockMode — ambil status global dari DB
        [HttpGet]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader", "User")]
        public async Task<IActionResult> GetAutoEnterDockMode()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            bool isActive = setting != null && setting.Value == "1";
            return Json(new { isActive });
        }

        // POST: Preparation/SetAutoEnterDockMode — simpan ke DB + broadcast ke semua client
        [HttpPost]
        [AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> SetAutoEnterDockMode([FromBody] SetAutoEnterDockModeRequest req)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            if (sessionRole != "Admin" && sessionRole != "Super Admin")
                return Json(new { success = false, message = "Akses ditolak" });

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            if (setting == null)
            {
                setting = new SystemSetting { Key = "AutoEnterDockMode", Description = "Mode Otomatis Enter Dock (global)" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.IsActive ? "1" : "0";
            await _context.SaveChangesAsync();

            // Jika mode baru diaktifkan: langsung proses semua grup 100% siap secara server-side
            if (req.IsActive)
            {
                var skipLeaderSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
                bool skipLeaderVerifActive = skipLeaderSetting != null && skipLeaderSetting.Value == "1";

                var now = DateTime.Now;
                var today = DateTime.Today;
                var pendingSchedules = await _context.DeliverySchedules
                    .Include(s => s.DeliveryItems)
                    .Where(s => s.ScheduledDate.Date >= today.AddDays(-1) &&
                                s.ScheduledDate.Date <= today.AddDays(1) &&
                                !s.ActualEnterDockTime.HasValue &&
                                s.Status != "Cancelled")
                    .ToListAsync();

                var groups = pendingSchedules.GroupBy(s => new {
                    CustId = s.CustomerId,
                    Date = s.ScheduledDate.Date,
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Area  = (s.Area  ?? "").Trim().ToUpper()
                });

                bool anyDockSet = false;
                foreach (var grp in groups)
                {
                    bool allReady = grp.All(s =>
                        !s.DeliveryItems.Any() ||
                        s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity));
                    if (!allReady) continue;

                    // Jika skip verifikasi leader tidak aktif, wajib tunggu verifikasi leader selesai
                    if (!skipLeaderVerifActive)
                    {
                        bool allLeaderVerified = grp.All(s => s.IsLeaderVerified || !s.DeliveryItems.Any());
                        if (!allLeaderVerified) continue;
                    }

                    foreach (var gs in grp)
                    {
                        gs.ActualEnterDockTime = now;
                        gs.Status = "In Progress";
                        gs.DriverStatus = "In Progress";
                        gs.PreparationStatus = "Prepared";
                        gs.ReadyToDockTime ??= now;
                        gs.UpdatedDate = now;
                        anyDockSet = true;
                    }
                }

                if (anyDockSet)
                {
                    await _context.SaveChangesAsync();
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new { action = "enterDock" });
                }
            }

            // Broadcast ke SEMUA browser/client yang terhubung (sync UI toggle)
            await _hubContext.Clients.All.SendAsync("autoEnterDockModeChanged", req.IsActive);

            return Json(new { success = true, isActive = req.IsActive });
        }

        // GET: Preparation/GetSkipLeaderVerificationMode — ambil status global dari DB
        [HttpGet]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader", "User")]
        public async Task<IActionResult> GetSkipLeaderVerificationMode()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            bool isActive = setting != null && setting.Value == "1";
            return Json(new { isActive });
        }

        // POST: Preparation/SetSkipLeaderVerificationMode — simpan ke DB + broadcast ke semua client
        [HttpPost]
        [AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> SetSkipLeaderVerificationMode([FromBody] SetSkipLeaderVerificationModeRequest req)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            if (sessionRole != "Admin" && sessionRole != "Super Admin")
                return Json(new { success = false, message = "Akses ditolak" });

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            if (setting == null)
            {
                setting = new SystemSetting { Key = "SkipLeaderVerificationMode", Description = "Mode Skip Verifikasi Leader (global)" };
                _context.SystemSettings.Add(setting);
            }
            setting.Value = req.IsActive ? "1" : "0";
            await _context.SaveChangesAsync();

            // Broadcast ke SEMUA browser/client yang terhubung (sync UI toggle)
            await _hubContext.Clients.All.SendAsync("skipLeaderVerificationModeChanged", req.IsActive);

            return Json(new { success = true, isActive = req.IsActive });
        }

        // POST: Preparation/ResetEnterDock/5 - Batalkan enter dock (Admin/Super Admin only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Super Admin")]
        public async Task<IActionResult> ResetEnterDock(int id, string? returnDate = null)
        {
            var repSchedule = await _context.DeliverySchedules.FindAsync(id);
            if (repSchedule == null) return NotFound();

            // Gunakan CustId+Cycle+Route+Area+Date — konsisten dengan GroupBy portal dan Dashboard Shipping.
            // Setiap customer punya truk sendiri, jadi reset hanya berlaku untuk customer yang sama.
            var schedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Where(s => s.ScheduledDate.Date == repSchedule.ScheduledDate.Date &&
                            s.CustomerId == repSchedule.CustomerId &&
                            s.Cycle == repSchedule.Cycle &&
                            s.Route == repSchedule.Route &&
                            s.Area == repSchedule.Area)
                .ToListAsync();

            foreach (var s in schedules)
            {
                s.ActualEnterDockTime = null;
                // Reset LeaderVerified agar auto-dock tidak langsung re-trigger setelah reset.
                // Leader harus verifikasi ulang sebelum bisa Enter Dock lagi.
                s.IsLeaderVerified = false;
                s.LeaderVerifiedKanbanCount = 0;
                // Setelah Reset Enter Dock, status TIDAK boleh kembali ke "Prepared" —
                // leader harus verifikasi ulang sebelum bisa Enter Dock lagi.
                // "Prepared" hanya dicapai via alur verifikasi normal, bukan langsung dari scan data.
                bool anyItemScanned = s.DeliveryItems.Any(di => (di.ActualQuantity ?? 0) > 0);
                s.PreparationStatus = anyItemScanned ? "In Progress" : "Scheduled";
                s.Status = anyItemScanned ? "In Progress" : "Scheduled";
                s.UpdatedDate = DateTime.Now;
                s.UpdatedBy = User.Identity?.Name ?? "Admin";
            }

            await _context.SaveChangesAsync();

            await _logService.LogConfirm(
                "Preparation",
                repSchedule.ScheduleNumber ?? "GROUP",
                repSchedule.ScheduleId,
                $"RESET Enter Dock untuk {repSchedule.Customer?.CustomerName} — scan ulang diizinkan",
                User.Identity?.Name ?? "Admin"
            );

            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new {
                action = "update",
                scheduleNumber = repSchedule.ScheduleNumber
            });

            TempData["SuccessMessage"] = $"✅ Enter Dock dibatalkan. Jadwal dapat di-scan ulang.";
            var redirectDate = DateTime.TryParse(returnDate, out var parsedReturn)
                ? parsedReturn
                : repSchedule.ScheduledDate;
            // noAutoDock=1 mencegah auto-dock bypass langsung re-fire pada page load setelah reset
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate.ToString("yyyy-MM-dd"), noAutoDock = "1" });
        }

        // POST: Preparation/ConfirmPickup/5 - Quick action untuk konfirmasi keberangkatan truk (Pickup)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader")]
        public async Task<IActionResult> ConfirmPickup(int id)
        {
            var schedule = await _context.DeliverySchedules.FindAsync(id);

            if (schedule == null)
            {
                return NotFound();
            }

            if (schedule.ActualPickupTime.HasValue)
            {
                TempData["ErrorMessage"] = "Schedule ini sudah dikonfirmasi Pickup!";
                return RedirectToAction(nameof(Index));
            }

            var pickupTime = DateTime.Now;
            
            // Cari schedule dan semua schedule saudara dalam grup pengiriman yang sama (tanggal, customer, cycle)
            var schedulesInGroup = await _context.DeliverySchedules
                .Where(s => s.ScheduleId == id || 
                            (s.ScheduledDate.Date == schedule.ScheduledDate.Date && 
                             s.CustomerId == schedule.CustomerId && 
                             s.Cycle == schedule.Cycle))
                .ToListAsync();

            foreach (var s in schedulesInGroup)
            {
                s.ActualPickupTime = pickupTime;
                if (!s.ActualStartTime.HasValue)
                {
                    s.ActualStartTime = pickupTime;
                }
                if (!s.ActualEndTime.HasValue)
                {
                    s.ActualEndTime = pickupTime;
                }
                if (s.Status != "Completed")
                {
                    s.Status = "In Progress";
                }
                s.UpdatedDate = pickupTime;
                s.UpdatedBy = User.Identity?.Name ?? "Preparation";
            }

            await _context.SaveChangesAsync();

            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();

            // Log activity
            await _logService.LogConfirm(
                "Preparation",
                schedule.ScheduleNumber,
                schedule.ScheduleId,
                $"Konfirmasi Pickup (Truk Berangkat) untuk {schedule.Customer?.CustomerName} pada {pickupTime:HH:mm}",
                User.Identity?.Name ?? "Preparation"
            );

            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "pickup",
                message = $"Truk telah berangkat (Pickup) untuk {schedule.Customer?.CustomerName} pada {pickupTime:HH:mm}",
                timestamp = pickupTime
            });

            TempData["SuccessMessage"] = $"✅ Pickup berhasil dikonfirmasi pada {pickupTime:HH:mm}!";
            return RedirectToActionResultOrCurrent(schedule);
        }

        private IActionResult RedirectToActionResultOrCurrent(DeliverySchedule schedule)
        {
            // Jika request datang dari referer dashboard, balik ke dashboard
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && (referer.Contains("/Home") || referer.Contains("/DeliverySchedules")))
            {
                return Redirect(referer);
            }
            
            var redirectDate = schedule.EnterDockTime?.Date ?? schedule.ScheduledDate;
            return RedirectToAction(nameof(Index), new { selectedDate = redirectDate });
        }

        // ─────────────────────────────────────────────────────────────────────
        // LEADER VERIFICATION
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// GET: Ambil data verifikasi untuk modal leader verification
        /// Mengembalikan list manifest + kanban + status scan preparation untuk satu group card
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetVerificationData(int id)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            if (sessionRole != "Admin" && sessionRole != "Leader" && sessionRole != "Super Admin")
                return Json(new { success = false, message = "Akses ditolak. Hanya Admin, Super Admin, atau Leader." });

            // 1. Fetch data repSchedule dasar (AsNoTracking cukup untuk pembacaan)
            var repSchedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (repSchedule == null)
            {
                Console.WriteLine($"[LeaderVerification] ERROR: Schedule ID {id} not found.");
                return Json(new { success = false, message = "Schedule tidak ditemukan." });
            }

            // 2. Ambil nilai dasar untuk grouping (samakan dengan Index)
            var repDate     = repSchedule.ScheduledDate.Date;
            var repCycle    = (repSchedule.Cycle ?? "").Trim().ToUpper();
            var repRoute    = (repSchedule.Route ?? "").Trim().ToUpper();
            var repCustName = (repSchedule.Customer?.CustomerName ?? "").Trim().ToUpper();
            
            // Area atau Docking
            var repArea = (repSchedule.Area ?? "").Trim().ToUpper();
            var repDock = (repSchedule.Customer?.Docking ?? "").Trim().ToUpper();
            var effectiveArea = !string.IsNullOrWhiteSpace(repArea) ? repArea : repDock;

            Console.WriteLine($"[LeaderVerification] ID: {id}, Date: {repDate:yyyy-MM-dd}, Cycle: '{repCycle}', Route: '{repRoute}', Customer: '{repCustName}', Area: '{effectiveArea}'");

            // 3. Query utama dengan eager loading
            // Gunakan AsNoTracking untuk performa maksimal pada read-only data
            var allSchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate >= repDate && s.ScheduledDate < repDate.AddDays(1)
                         && s.Status != "Cancelled")
                .ToListAsync();

            // 4. Filter di memory untuk akurasi grouping (ToUpper & Trim)
            var schedules = allSchedules
                .Where(s => (s.Cycle ?? "").Trim().ToUpper() == repCycle
                         && (s.Route ?? "").Trim().ToUpper() == repRoute
                         && (s.Customer?.CustomerName ?? "").Trim().ToUpper() == repCustName
                         && ((!string.IsNullOrWhiteSpace(s.Area) 
                                ? s.Area.Trim().ToUpper() 
                                : (s.Customer?.Docking ?? "").Trim().ToUpper()) == effectiveArea))
                .OrderBy(s => s.ScheduleNumber)
                .ToList();

            Console.WriteLine($"[LeaderVerification] Found {schedules.Count} schedules in group.");

            // Hitung kanban per manifest + detail parts
            var manifestList = schedules.Select(s =>
            {
                double kanbanTarget = 0;
                var partsList = new List<object>();
                
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        var partKanban = qpc > 0 ? (int)Math.Ceiling((double)di.Quantity / qpc) : 0;
                        kanbanTarget += partKanban;
                        
                        // Gunakan CustomerPartNumber jika ada, jika tidak pakai ItemCode
                        var displayPartNo = !string.IsNullOrWhiteSpace(di.Item?.CustomerPartNumber) 
                            ? di.Item.CustomerPartNumber 
                            : di.Item?.ItemCode ?? "-";
                        
                        partsList.Add(new
                        {
                            itemId = di.ItemId,
                            partNumber = displayPartNo,
                            partName = di.Item?.ItemName ?? "-",
                            quantity = di.Quantity,
                            qtyLot = qpc,
                            kanbanCount = partKanban,
                            unit = di.Unit ?? "PCS"
                        });
                    }
                }
                var kanbanTarget_i = (int)Math.Round(kanbanTarget);
                var kanbanScanned  = s.LeaderVerifiedKanbanCount;
                // Manifest tanpa item (kanbanTarget == 0) dianggap otomatis terverifikasi
                var isVerified     = s.IsLeaderVerified || kanbanTarget_i == 0 || kanbanScanned >= kanbanTarget_i;
                var mnKey          = ParseManifestKey(s.ScheduleNumber ?? "");
                return new
                {
                    scheduleId     = s.ScheduleId,
                    scheduleNumber = s.ScheduleNumber ?? "",
                    manifestKey    = mnKey,
                    kanbanTarget   = kanbanTarget_i,
                    kanbanScanned,
                    isPrepared     = s.PreparationStatus == "Prepared",
                    isVerified,
                    parts          = partsList
                };
            }).ToList();

            // Progress: total kanban di semua manifest, berapa yang sudah di-scan Leader
            var totalKanban    = manifestList.Sum(m => m.kanbanTarget);
            var scannedKanban  = manifestList.Sum(m => Math.Min(m.kanbanScanned, m.kanbanTarget));
            var totalManifests = manifestList.Count;
            var verifiedManifests = manifestList.Count(m => m.isVerified);

            return Json(new
            {
                success          = true,
                representativeId = id,
                dockName         = repSchedule.Area ?? repSchedule.Customer?.CustomerName ?? "—",
                customerName     = repSchedule.Customer?.CustomerName ?? "—",
                cycle            = repSchedule.Cycle ?? "",
                totalKanban,
                scannedKanban,
                totalManifests,
                verifiedManifests,
                isComplete       = verifiedManifests >= totalManifests && totalManifests > 0,
                manifests        = manifestList
            });
        }

        /// <summary>
        /// POST: Proses scan manifest saat Leader Verification
        /// scanInput format: "1231454334/2234" → key = "1231454334" (sebelum /)
        /// Setiap scan menambah LeaderVerifiedKanbanCount. Manifest dianggap VERIFIED
        /// ketika LeaderVerifiedKanbanCount >= kanbanTarget manifest tersebut.
        /// </summary>
        [HttpPost]
        [AuthorizeRoles("Admin", "Super Admin", "Preparation", "Leader")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScanManifestVerification(int representativeId, string scanInput)
        {
            var sessionRole = HttpContext.Session.GetString("Role");
            var sessionUser = HttpContext.Session.GetString("Username") ?? "Leader";

            if (sessionRole != "Admin" && sessionRole != "Leader" && sessionRole != "Super Admin")
                return Json(new { success = false, message = "Akses ditolak. Hanya Admin, Super Admin, atau Leader." });

            if (string.IsNullOrWhiteSpace(scanInput))
                return Json(new { success = false, message = "Input scan kosong." });

            var repSchedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .FirstOrDefaultAsync(s => s.ScheduleId == representativeId);
            if (repSchedule == null)
                return Json(new { success = false, message = "Schedule tidak ditemukan." });

            // Bersihkan input scan (bisa berupa QR code lengkap)
            var scannedPartNo = ExtractPartNo(scanInput);

            // Effective area untuk grouping (sama seperti di Index)
            var effectiveArea = !string.IsNullOrWhiteSpace(repSchedule.Area)
                ? repSchedule.Area.Trim().ToUpper()
                : (repSchedule.Customer?.Docking ?? "").Trim().ToUpper();

            // Cari semua schedule dalam TRIP yang sama (Cycle + Route + Area + Tanggal + Customer)
            var schedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate >= repSchedule.ScheduledDate.Date && s.ScheduledDate < repSchedule.ScheduledDate.Date.AddDays(1)
                         && (s.Cycle ?? "").Trim().ToUpper() == (repSchedule.Cycle ?? "").Trim().ToUpper()
                         && (s.Route ?? "").Trim().ToUpper() == (repSchedule.Route ?? "").Trim().ToUpper()
                         && (s.Customer != null && s.Customer.CustomerName != null ? s.Customer.CustomerName.Trim().ToUpper() : "") == (repSchedule.Customer != null && repSchedule.Customer.CustomerName != null ? repSchedule.Customer.CustomerName.Trim().ToUpper() : "")
                         && ((!string.IsNullOrWhiteSpace(s.Area) ? s.Area.Trim().ToUpper() : (s.Customer != null ? s.Customer.Docking ?? "" : "").Trim().ToUpper()) == effectiveArea))
                .ToListAsync();

            // Cari schedule yang mengandung Part Number yang di-scan
            // Match berdasarkan: ItemCode, CustomerPartNumber - dengan partial match & Contains untuk QR robustness
            DeliverySchedule? matched = null;
            string matchedPartName = "";
            
            foreach (var s in schedules)
            {
                if (s.DeliveryItems != null)
                {
                    var matchedItem = s.DeliveryItems.FirstOrDefault(di => 
                        di.Item != null && (
                            // Exact match
                            (di.Item.ItemCode ?? "").ToUpper() == scannedPartNo ||
                            (di.Item.CustomerPartNumber ?? "").ToUpper() == scannedPartNo ||
                            // Robust match for QR string vs Substring
                            scannedPartNo.Contains((di.Item.ItemCode ?? "").ToUpper()) ||
                            scannedPartNo.Contains((di.Item.CustomerPartNumber ?? "").ToUpper()) ||
                            // Reverse match if needed
                            (!string.IsNullOrEmpty(di.Item.ItemCode) && (di.Item.ItemCode ?? "").ToUpper().Contains(scannedPartNo)) ||
                            (!string.IsNullOrEmpty(di.Item.CustomerPartNumber) && (di.Item.CustomerPartNumber ?? "").ToUpper().Contains(scannedPartNo))
                        ));
                    
                    if (matchedItem != null)
                    {
                        matched = s;
                        matchedPartName = matchedItem.Item?.ItemName ?? matchedItem.Item?.ItemCode ?? scannedPartNo;
                        break;
                    }
                }
            }

            if (matched == null)
                return Json(new { success = false, message = $"Part \"{scannedPartNo}\" tidak ditemukan di manifest ini." });

            // Validasi: preparation harus sudah Prepared
            if (matched.PreparationStatus != "Prepared")
                return Json(new { success = false, message = $"Manifest \"{matched.ScheduleNumber}\" belum selesai preparation (status: {matched.PreparationStatus ?? "Scheduled"})." });

            // Hitung kanbanTarget untuk manifest ini
            double kanbanTargetDouble = 0;
            if (matched.DeliveryItems != null)
            {
                foreach (var di in matched.DeliveryItems)
                {
                    var qpc = di.Item?.QtyLot ?? 1;
                    if (qpc > 0) kanbanTargetDouble += Math.Ceiling((double)di.Quantity / qpc);
                }
            }
            var kanbanTarget = (int)Math.Round(kanbanTargetDouble);

            // Cek apakah sudah fully verified (semua kanban terpenuhi)
            var manifestKey = ParseManifestKey(matched.ScheduleNumber ?? "");
            if (matched.IsLeaderVerified || (kanbanTarget > 0 && matched.LeaderVerifiedKanbanCount >= kanbanTarget))
                return Json(new { success = false, isDuplicate = true, message = $"Manifest \"{manifestKey}\" sudah sepenuhnya diverifikasi ({kanbanTarget}/{kanbanTarget} kanban)." });

            // Tambah count
            matched.LeaderVerifiedKanbanCount += 1;
            matched.UpdatedDate = DateTime.Now;
            matched.UpdatedBy   = sessionUser;

            var newCount   = matched.LeaderVerifiedKanbanCount;
            var remaining  = kanbanTarget - newCount;
            var isManifestComplete = kanbanTarget > 0
                ? newCount >= kanbanTarget
                : true; // jika tidak ada kanban target, anggap langsung verified

            // Jika kanban manifest ini terpenuhi → tandai IsLeaderVerified
            if (isManifestComplete)
            {
                matched.IsLeaderVerified = true;
                matched.LeaderVerifiedAt = DateTime.Now;
                matched.LeaderVerifiedBy = sessionUser;
            }

            await _context.SaveChangesAsync();

            // Hitung total kanban progress di seluruh group
            // (ulang hitung karena data sudah ter-update in-memory)
            int totalKanban   = 0;
            int scannedKanban = 0;
            int totalManifests    = schedules.Count;
            int verifiedManifests = 0;

            foreach (var s in schedules)
            {
                double kt = 0;
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        if (qpc > 0) kt += Math.Ceiling((double)di.Quantity / qpc);
                    }
                }
                var ktInt = (int)Math.Round(kt);
                totalKanban   += ktInt;
                scannedKanban += Math.Min(s.LeaderVerifiedKanbanCount, ktInt);
                if (s.IsLeaderVerified || (ktInt > 0 && s.LeaderVerifiedKanbanCount >= ktInt))
                    verifiedManifests++;
            }

            var isGroupComplete = verifiedManifests >= totalManifests && totalManifests > 0;

            if (isGroupComplete)
            {
                var autoDockSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
                bool autoEnterDockActive = autoDockSetting != null && autoDockSetting.Value == "1";

                // Pastikan semua ter-flag IsLeaderVerified dan set Enter Dock time jika mode otomatis aktif
                var enterDockTime = DateTime.Now;
                foreach (var s in schedules)
                {
                    if (!s.IsLeaderVerified)
                    {
                        s.IsLeaderVerified = true;
                        s.LeaderVerifiedAt = DateTime.Now;
                        s.LeaderVerifiedBy = sessionUser;
                    }
                    
                    // Otomatis set Enter Dock saat verifikasi selesai HANYA jika AutoEnterDockMode aktif
                    if (autoEnterDockActive && s.ActualEnterDockTime == null)
                    {
                        s.ActualEnterDockTime = enterDockTime;
                        s.ReadyToDockTime ??= enterDockTime;
                    }
                    
                    // Update status ke In Progress
                    if (s.Status != "Completed")
                    {
                        s.Status = "In Progress";
                    }
                    
                    s.UpdatedDate = DateTime.Now;
                    s.UpdatedBy = sessionUser;
                }
                await _context.SaveChangesAsync();

                await _logService.LogConfirm(
                    "LeaderVerification",
                    repSchedule.ScheduleNumber ?? "GROUP",
                    repSchedule.ScheduleId,
                    autoEnterDockActive
                        ? $"Leader Verification selesai - semua {totalManifests} manifest ({totalKanban} kanban) terverifikasi oleh {sessionUser}. Otomatis Dock In."
                        : $"Leader Verification selesai - semua {totalManifests} manifest ({totalKanban} kanban) terverifikasi oleh {sessionUser}.",
                    sessionUser
                );

                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action         = "leaderVerificationComplete",
                    scheduleNumber = repSchedule.ScheduleNumber,
                    message        = $"Leader Verification selesai untuk {repSchedule.Area}.",
                    timestamp      = DateTime.Now
                });
            }
            else
            {
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action         = "manifestVerified",
                    scheduleNumber = repSchedule.ScheduleNumber,
                    manifestKey    = manifestKey,
                    scanned        = scannedKanban,
                    total          = totalKanban,
                    timestamp      = DateTime.Now
                });
            }

            // Kembalikan data progress terbaru
            var updatedManifests = schedules.Select(s =>
            {
                double kt = 0;
                if (s.DeliveryItems != null)
                {
                    foreach (var di in s.DeliveryItems)
                    {
                        var qpc = di.Item?.QtyLot ?? 1;
                        if (qpc > 0) kt += Math.Ceiling((double)di.Quantity / qpc);
                    }
                }
                var ktInt  = (int)Math.Round(kt);
                var isVer  = s.IsLeaderVerified || (ktInt > 0 && s.LeaderVerifiedKanbanCount >= ktInt);
                return new
                {
                    scheduleId    = s.ScheduleId,
                    manifestKey   = ParseManifestKey(s.ScheduleNumber ?? ""),
                    scheduleNumber= s.ScheduleNumber ?? "",
                    kanbanTarget  = ktInt,
                    kanbanScanned = s.LeaderVerifiedKanbanCount,
                    isVerified    = isVer
                };
            }).ToList();

            // Pesan feedback berdasarkan kondisi
            string feedbackMessage;
            if (isGroupComplete)
                feedbackMessage = $"✅ Semua {totalManifests} manifest ({totalKanban} kanban) terverifikasi!";
            else if (isManifestComplete)
                feedbackMessage = $"✅ Manifest \"{manifestKey}\" selesai ({kanbanTarget}/{kanbanTarget} kanban).";
            else
                feedbackMessage = $"✅ Kanban {newCount}/{kanbanTarget} — Part \"{matchedPartName}\" ({remaining} lagi).";

            return Json(new
            {
                success        = true,
                message        = feedbackMessage,
                scannedKey     = scannedPartNo,
                kanbanTarget,
                kanbanScanned  = newCount,
                isManifestComplete,
                totalKanban,
                scannedKanban,
                totalManifests,
                verifiedManifests,
                isComplete     = isGroupComplete,
                manifests      = updatedManifests
            });
        }

        /// <summary>
        /// Helper: Parse key manifest dari format "PREFIX/SUFFIX" → ambil PREFIX (sebelum '/')
        /// </summary>
        private static string ParseManifestKey(string scheduleNumber)
        {
            if (string.IsNullOrWhiteSpace(scheduleNumber)) return string.Empty;
            var idx = scheduleNumber.IndexOf('/');
            return idx >= 0
                ? scheduleNumber.Substring(0, idx).Trim()
                : scheduleNumber.Trim();
        }
        /// <summary>
        /// Helper untuk mengekstraksi Part No dari input scanner (Robust Scanner Parsing)
        /// Menangani QR industri: [)>06 GS P(PartNo) GS Q(Qty)...
        /// </summary>
        private static string ExtractPartNo(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = input.Trim().ToUpper();

            // Handle common industrial QR formats [)>06 GS P(PartNo) GS Q(Qty) ...
            // ASCII GS = 29, RS = 30, EOT = 4, US = 31
            if (input.Contains("[)>") || input.Contains("\u001d") || input.Contains("\u001e") || input.Contains("P"))
            {
                // Try Regex for P identifier (typical for part number)
                // Also handles separators like GS (\u001d) or space
                var match = System.Text.RegularExpressions.Regex.Match(input, @"P\s*([A-Z0-9\-\.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value.ToUpper().Trim();
            }

            return input;
        }
    }

    public class SetAutoEnterDockModeRequest
    {
        public bool IsActive { get; set; }
    }

    public class SetSkipLeaderVerificationModeRequest
    {
        public bool IsActive { get; set; }
    }
}

