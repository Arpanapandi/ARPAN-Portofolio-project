
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.ViewModels;
using DeliveryControl.Hubs;
using System.Globalization;
using DeliveryControl.Filters;
using DeliveryControl.Services;

namespace DeliveryControl.Controllers
{
    [AuthorizeRoles("Admin", "User")]
    public class DeliverySchedulesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<DeliveryHub> _hubContext;
        private readonly IHubContext<StockHub> _stockHubContext;
        private readonly PreparationSyncService _syncService;

        public DeliverySchedulesController(
            ApplicationDbContext context, 
            IHubContext<DeliveryHub> hubContext, 
            IHubContext<StockHub> stockHubContext,
            PreparationSyncService syncService)
        {
            _context = context;
            _hubContext = hubContext;
            _stockHubContext = stockHubContext;
            _syncService = syncService;
        }

        // GET: DeliverySchedules/GanttChart - Visualisasi Gantt Chart
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> GanttChart(DateTime? startDate, DateTime? endDate, int? customerId)
        {
            var scheduleDate = startDate ?? DateTime.Today;
            var endScheduleDate = endDate ?? DateTime.Today;
            
            ViewData["StartDate"] = scheduleDate.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endScheduleDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            // Ambil semua active customers dari master
            var allCustomers = await _context.Customers
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            // Filter berdasarkan customerId jika ada
            if (customerId.HasValue)
            {
                allCustomers = allCustomers.Where(c => c.CustomerId == customerId.Value).ToList();
            }

            // Ambil schedules untuk tanggal yang dipilih + H-1 & H+1 (karena window 42 jam mencakup H-1 18:00 s/d H+1 12:00)
            var scheduleDateMin = scheduleDate.Date.AddDays(-1);
            var scheduleDateMax = endScheduleDate.Date.AddDays(1);
            var windowStart = scheduleDate.Date.AddDays(-1).AddHours(18);
            var windowEnd = endScheduleDate.Date.AddDays(1).AddHours(12);

            var rawSchedules = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= scheduleDateMin && s.ScheduledDate.Date <= scheduleDateMax)
                .ToListAsync();

            if (customerId.HasValue)
            {
                rawSchedules = rawSchedules.Where(s => s.CustomerId == customerId.Value).ToList();
            }

            // Filter schedules agar tidak mencampur hari kemarin yang sudah selesai ke hari ini:
            // 1. Jadwal hari ini (ScheduledDate antara scheduleDate s/d endScheduleDate)
            // 2. Jadwal H-1 HANYA JIKA belum selesai (!ActualEndTime && Status != "Completed") ATAU planned time-nya menyentuh window 42 jam (>= windowStart)
            // 3. Jadwal H+1 HANYA JIKA planned time-nya menyentuh window 42 jam (<= windowEnd)
            var relevantSchedules = rawSchedules.Where(s =>
            {
                var sDate = s.ScheduledDate.Date;
                if (sDate >= scheduleDate.Date && sDate <= endScheduleDate.Date)
                    return true;
                if (sDate < scheduleDate.Date)
                {
                    bool isOngoing = !s.ActualEndTime.HasValue && s.Status != "Completed";
                    bool inWindow = (s.EnterDockTime.HasValue && s.EnterDockTime.Value >= windowStart) ||
                                    (s.PickupTime.HasValue && s.PickupTime.Value >= windowStart) ||
                                    (s.ETD.HasValue && s.ETD.Value >= windowStart);
                    return isOngoing || inWindow;
                }
                if (sDate > endScheduleDate.Date)
                {
                    bool inWindow = (s.EnterDockTime.HasValue && s.EnterDockTime.Value <= windowEnd) ||
                                    (s.PickupTime.HasValue && s.PickupTime.Value <= windowEnd);
                    return inWindow;
                }
                return false;
            }).ToList();

            // Buat dictionary: CustomerId → list schedules
            // Jika ada jadwal untuk hari target, prioritaskan jadwal hari target agar tidak bercampur dengan H-1/H+1
            var schedulesByCustomer = relevantSchedules
                .Where(s => s.CustomerId.HasValue)
                .GroupBy(s => s.CustomerId.Value)
                .ToDictionary(g => g.Key, g =>
                {
                    var list = g.ToList();
                    var targetDayList = list.Where(x => x.ScheduledDate.Date >= scheduleDate.Date && x.ScheduledDate.Date <= endScheduleDate.Date).ToList();
                    if (targetDayList.Any())
                        return targetDayList;
                    return list;
                });

            // Helper: parse "HH:mm" string ke double jam (untuk sorting)
            static double ParseTimeString(string? timeStr)
            {
                if (string.IsNullOrWhiteSpace(timeStr)) return 0;
                if (TimeSpan.TryParse(timeStr, out var ts))
                    return ts.TotalHours;
                return 0;
            }

            // Buat GanttRow untuk setiap customer
            var rows = allCustomers.Select(customer =>
            {
                var schedules = schedulesByCustomer.TryGetValue(customer.CustomerId, out var s) ? s : new List<DeliverySchedule>();

                // Tentukan earliest meaningful time (non-zero) untuk sorting
                double pickupHour = 0;
                double dockingHour = 0;
                double prepStartHour = 0;

                if (schedules.Any())
                {
                    var earliestPickup = schedules
                        .Where(x => x.PickupTime.HasValue)
                        .Select(x => x.PickupTime!.Value.Hour + x.PickupTime!.Value.Minute / 60.0)
                        .DefaultIfEmpty(0)
                        .Min();
                    var earliestDock = schedules
                        .Where(x => x.EnterDockTime.HasValue)
                        .Select(x => x.EnterDockTime!.Value.Hour + x.EnterDockTime!.Value.Minute / 60.0)
                        .DefaultIfEmpty(0)
                        .Min();
                    pickupHour = earliestPickup;
                    dockingHour = earliestDock;
                }
                else
                {
                    pickupHour = ParseTimeString(customer.Pickup);
                    dockingHour = ParseTimeString(customer.Docking);
                }
                prepStartHour = customer.StartPrepareTime / 60.0;

                double sortHour = 9999;
                if (customer.StartPrepareTime > 0) sortHour = customer.StartPrepareTime / 60.0;
                else if (dockingHour > 0) sortHour = dockingHour;
                else if (pickupHour > 0) sortHour = pickupHour;

                return new GanttRow
                {
                    Customer = customer,
                    Schedules = schedules
                        .OrderBy(x => x.ActualEndTime.HasValue ? 2 :
                                     (x.Status == "In Progress" || x.PreparationStatus == "In Progress" ||
                                      (x.ActualStartTime.HasValue && !x.ActualEndTime.HasValue) ||
                                      (x.ActualEnterDockTime.HasValue && !x.ActualEndTime.HasValue)) ? 0 : 1)
                        .ThenBy(x => x.PickupTime ?? x.ETD ?? x.ScheduledDate)
                        .ToList(),
                    SortKey = sortHour,
                    HasCompleteData = pickupHour > 0 || dockingHour > 0 || prepStartHour > 0
                };
            })
            .OrderByDescending(r => r.Schedules.Any())
            .ThenBy(r => r.SortKey)
            .ThenBy(r => r.Customer.CustomerName)
            .ToList();

            // Hitung statistik dari schedules hari target - Sync dengan Dashboard Delay
            var now = DateTime.Now;
            var targetDaySchedules = rawSchedules.Where(s => s.ScheduledDate.Date >= scheduleDate.Date && s.ScheduledDate.Date <= endScheduleDate.Date).ToList();
            var totalScheduled = targetDaySchedules.Count;
            var totalCompleted = targetDaySchedules.Count(s => s.ActualEndTime.HasValue || s.Status == "Completed");
            var totalInProgress = targetDaySchedules.Count(s =>
                s.Status == "In Progress" || s.PreparationStatus == "In Progress" ||
                (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) ||
                (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue));
            
            var totalLate = targetDaySchedules.Count(s =>
            {
                bool isCompleted = s.Status == "Completed" || s.ActualEndTime.HasValue;
                
                // 1. Sudah terlambat pickup
                if (s.ActualStartTime.HasValue && s.PickupTime.HasValue && s.ActualStartTime.Value > s.PickupTime.Value)
                    return true;

                // 2. Sedang terlambat pickup
                if (!isCompleted && !s.ActualStartTime.HasValue && s.PickupTime.HasValue && now > s.PickupTime.Value)
                    return true;
                
                // 3. Sedang terlambat masuk dock
                if (!isCompleted && !s.ActualEnterDockTime.HasValue && s.EnterDockTime.HasValue && now > s.EnterDockTime.Value)
                    return true;

                return false;
            });

            var viewModel = new GanttChartViewModel
            {
                Rows = rows,
                SelectedDate = scheduleDate,
                TotalScheduled = totalScheduled,
                TotalCompleted = totalCompleted,
                TotalInProgress = totalInProgress,
                TotalLate = totalLate
            };

            return View(viewModel);
        }

        // GET: DeliverySchedules/DelayChart - Grafik batang total delay per jam + tren harian
        public async Task<IActionResult> DelayChart(DateTime? date, int? customerId)
        {
            var selectedDate = (date ?? DateTime.Today).Date;

            ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            // Ambil data untuk rentang 14 hari terakhir (termasuk selectedDate) untuk grafik harian
            var rangeEnd = selectedDate;
            var rangeStart = selectedDate.AddDays(-13); // 14 hari ke belakang

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= rangeStart && s.ScheduledDate.Date <= rangeEnd);

            if (customerId.HasValue)
            {
                query = query.Where(s => s.CustomerId == customerId.Value);
            }

            var schedules = await query.ToListAsync();

            // ================== DATA PER JAM (HANYA UNTUK selectedDate) ==================

            var schedulesForSelectedDate = schedules
                .Where(s => s.ScheduledDate.Date == selectedDate)
                .ToList();

            // Siapkan bucket 24 jam (00:00 - 23:59) untuk Pickup dan Dock In (Delay)
            var pickupBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartHourData
                {
                    Hour = h,
                    TotalDelayMinutes = 0,
                    Customers = new List<string>()
                })
                .ToList();

            var dockInBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartHourData
                {
                    Hour = h,
                    TotalDelayMinutes = 0,
                    Customers = new List<string>()
                })
                .ToList();

            // Bucket untuk ON TIME (jumlah order, bukan menit)
            var onTimePickupBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartViewModel.OnTimeChartHourData
                {
                    Hour = h,
                    TotalCount = 0,
                    Customers = new List<string>()
                })
                .ToList();

            var onTimeDockBuckets = Enumerable.Range(0, 24)
                .Select(h => new DelayChartViewModel.OnTimeChartHourData
                {
                    Hour = h,
                    TotalCount = 0,
                    Customers = new List<string>()
                })
                .ToList();

            foreach (var s in schedulesForSelectedDate)
            {
                // ====== DELAY PICKUP (ActualStartTime vs PickupTime) ======
                if (s.PickupTime.HasValue && s.ActualStartTime.HasValue)
                {
                    var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                    if (diff.TotalMinutes > 0)
                    {
                        int hourIndex = s.PickupTime.Value.Hour;
                        var bucket = pickupBuckets[hourIndex];

                        var minutes = (int)Math.Round(diff.TotalMinutes);
                        bucket.TotalDelayMinutes += minutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = minutes
                                });
                            }
                            else
                            {
                                existingDetail.DelayMinutes += minutes;
                            }
                        }
                    }
                    else if (diff.TotalMinutes < 0)
                    {
                        // ON TIME / lebih cepat (advance) - simpan selisih menit positif
                        int hourIndex = s.PickupTime.Value.Hour;
                        var bucket = onTimePickupBuckets[hourIndex];

                        var advanceMinutes = (int)Math.Round(Math.Abs(diff.TotalMinutes));
                        bucket.TotalCount += advanceMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = advanceMinutes
                                });
                            }
                            else
                            {
                                existingDetail.DelayMinutes += advanceMinutes;
                            }
                        }
                    }
                }

                // ====== DELAY DOCK IN (ActualEnterDockTime vs EnterDockTime) ======
                if (s.EnterDockTime.HasValue && s.ActualEnterDockTime.HasValue)
                {
                    var dockDiff = s.ActualEnterDockTime.Value - s.EnterDockTime.Value;
                    if (dockDiff.TotalMinutes > 0)
                    {
                        int hourIndexDock = s.EnterDockTime.Value.Hour;
                        var dockBucket = dockInBuckets[hourIndexDock];

                        var dockMinutes = (int)Math.Round(dockDiff.TotalMinutes);
                        dockBucket.TotalDelayMinutes += dockMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!dockBucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                dockBucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDockDetail = dockBucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDockDetail == null)
                            {
                                dockBucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = dockMinutes
                                });
                            }
                            else
                            {
                                existingDockDetail.DelayMinutes += dockMinutes;
                            }
                        }
                    }
                    else if (dockDiff.TotalMinutes < 0)
                    {
                        // ON TIME / lebih cepat Dock In - selisih menit positif sebagai advance
                        int hourIndexDock = s.EnterDockTime.Value.Hour;
                        var bucket = onTimeDockBuckets[hourIndexDock];

                        var advanceMinutes = (int)Math.Round(Math.Abs(dockDiff.TotalMinutes));
                        bucket.TotalCount += advanceMinutes;

                        if (!string.IsNullOrWhiteSpace(s.Customer?.CustomerName))
                        {
                            if (!bucket.Customers.Contains(s.Customer.CustomerName))
                            {
                                bucket.Customers.Add(s.Customer.CustomerName);
                            }

                            var existingDockDetail = bucket.CustomerDetails
                                .FirstOrDefault(d => d.CustomerName == s.Customer.CustomerName);
                            if (existingDockDetail == null)
                            {
                                bucket.CustomerDetails.Add(new DelayChartCustomerDetail
                                {
                                    CustomerName = s.Customer.CustomerName,
                                    DelayMinutes = advanceMinutes
                                });
                            }
                            else
                            {
                                existingDockDetail.DelayMinutes += advanceMinutes;
                            }
                        }
                    }
                }
            }

            // ================== DATA HARIAN DELAY PICKUP (UNTUK TREND) ==================

            var dailyDelayDict = new Dictionary<DateTime, DailyPickupDelayData>();

            foreach (var s in schedules)
            {
                if (!s.PickupTime.HasValue || !s.ActualStartTime.HasValue)
                    continue;

                var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                if (diff.TotalMinutes <= 0)
                    continue; // hanya hitung yang delay (positif)

                var scheduleDate = s.ScheduledDate.Date;
                var minutes = (int)Math.Round(diff.TotalMinutes);
                var customerName = s.Customer?.CustomerName ?? "(Unknown)";

                if (!dailyDelayDict.TryGetValue(scheduleDate, out var dailyData))
                {
                    dailyData = new DailyPickupDelayData
                    {
                        Date = scheduleDate,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    };
                    dailyDelayDict[scheduleDate] = dailyData;
                }

                dailyData.TotalDelayMinutes += minutes;

                var existingCustomer = dailyData.CustomerDetails
                    .FirstOrDefault(c => c.CustomerName == customerName);
                if (existingCustomer == null)
                {
                    dailyData.CustomerDetails.Add(new DelayChartCustomerDetail
                    {
                        CustomerName = customerName,
                        DelayMinutes = minutes
                    });
                }
                else
                {
                    existingCustomer.DelayMinutes += minutes;
                }
            }

            // Pastikan semua hari dalam range muncul di grafik (meski tanpa delay)
            var dailyPickupDelays = new List<DailyPickupDelayData>();
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                if (dailyDelayDict.TryGetValue(d.Date, out var data))
                {
                    // Urutkan detail customer descending by delay
                    data.CustomerDetails = data.CustomerDetails
                        .OrderByDescending(c => c.DelayMinutes)
                        .ToList();
                    dailyPickupDelays.Add(data);
                }
                else
                {
                    dailyPickupDelays.Add(new DailyPickupDelayData
                    {
                        Date = d.Date,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    });
                }
            }

            var viewModel = new DelayChartViewModel
            {
                SelectedDate = selectedDate,
                CustomerId = customerId,
                StartDate = rangeStart,
                EndDate = rangeEnd,
                PickupHourlyData = pickupBuckets,
                DockInHourlyData = dockInBuckets,
                TotalPickupDelayMinutes = pickupBuckets.Sum(x => x.TotalDelayMinutes),
                TotalDockInDelayMinutes = dockInBuckets.Sum(x => x.TotalDelayMinutes),
                OnTimePickupHourlyData = onTimePickupBuckets,
                OnTimeDockInHourlyData = onTimeDockBuckets,
                TotalOnTimePickupCount = onTimePickupBuckets.Sum(x => x.TotalCount),
                TotalOnTimeDockInCount = onTimeDockBuckets.Sum(x => x.TotalCount),
                DailyPickupDelays = dailyPickupDelays
            };

            return View(viewModel);
        }

        // GET: DeliverySchedules/DelayPickupTrend - Grafik harian + tabel schedule delay pickup
        public async Task<IActionResult> DelayPickupTrend(DateTime? date, int? customerId)
        {
            var selectedDate = (date ?? DateTime.Today).Date;

            // Range 1 bulan penuh berdasarkan bulan dari selectedDate
            var rangeStart = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            var rangeEnd = rangeStart.AddMonths(1).AddDays(-1);

            ViewData["Date"] = selectedDate.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Customers"] = new SelectList(_context.Customers.Where(c => c.IsActive), "CustomerId", "CustomerName");

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.ScheduledDate.Date >= rangeStart && s.ScheduledDate.Date <= rangeEnd);

            if (customerId.HasValue)
            {
                query = query.Where(s => s.CustomerId == customerId.Value);
            }

            var schedules = await query.ToListAsync();

            // ================== DATA HARIAN DELAY PICKUP (UNTUK TREND) ==================
            var dailyDelayDict = new Dictionary<DateTime, DailyPickupDelayData>();
            var delayedSchedules = new List<DeliverySchedule>();

            foreach (var s in schedules)
            {
                if (!s.PickupTime.HasValue || !s.ActualStartTime.HasValue)
                    continue;

                var diff = s.ActualStartTime.Value - s.PickupTime.Value;
                if (diff.TotalMinutes <= 0)
                    continue; // hanya hitung yang delay (positif)

                var minutes = (int)Math.Round(diff.TotalMinutes);
                var scheduleDate = s.ScheduledDate.Date;
                var customerName = s.Customer?.CustomerName ?? "(Unknown)";

                // Masuk list schedule delay
                delayedSchedules.Add(s);

                if (!dailyDelayDict.TryGetValue(scheduleDate, out var dailyData))
                {
                    dailyData = new DailyPickupDelayData
                    {
                        Date = scheduleDate,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    };
                    dailyDelayDict[scheduleDate] = dailyData;
                }

                dailyData.TotalDelayMinutes += minutes;

                var existingCustomer = dailyData.CustomerDetails
                    .FirstOrDefault(c => c.CustomerName == customerName);
                if (existingCustomer == null)
                {
                    dailyData.CustomerDetails.Add(new DelayChartCustomerDetail
                    {
                        CustomerName = customerName,
                        DelayMinutes = minutes
                    });
                }
                else
                {
                    existingCustomer.DelayMinutes += minutes;
                }
            }

            // Pastikan semua hari dalam range muncul di grafik (meski tanpa delay)
            var dailyPickupDelays = new List<DailyPickupDelayData>();
            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                if (dailyDelayDict.TryGetValue(d.Date, out var data))
                {
                    data.CustomerDetails = data.CustomerDetails
                        .OrderByDescending(c => c.DelayMinutes)
                        .ToList();
                    dailyPickupDelays.Add(data);
                }
                else
                {
                    dailyPickupDelays.Add(new DailyPickupDelayData
                    {
                        Date = d.Date,
                        TotalDelayMinutes = 0,
                        CustomerDetails = new List<DelayChartCustomerDetail>()
                    });
                }
            }

            // Urutkan schedule delay berdasarkan tanggal dan jam pickup
            delayedSchedules = delayedSchedules
                .OrderBy(s => s.ScheduledDate.Date)
                .ThenBy(s => s.PickupTime ?? s.ETD ?? s.ScheduledDate)
                .ToList();

            var vm = new DailyPickupTrendViewModel
            {
                SelectedDate = selectedDate,
                StartDate = rangeStart,
                EndDate = rangeEnd,
                CustomerId = customerId,
                DailyPickupDelays = dailyPickupDelays,
                DelayedSchedules = delayedSchedules
            };

            return View(vm);
        }

        // GET: DeliverySchedules
        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate, int? customerId, string status, int pageNumber = 1)
        {
            var customerList = await _context.Customers
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToListAsync();

            ViewData["Customers"] = new SelectList(customerList, "CustomerId", "DisplayName");
            ViewData["Statuses"] = new List<string> { "Scheduled", "In Progress", "Completed", "Cancelled", "Delayed" };
            
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            ViewData["CustomerId"] = customerId;
            ViewData["Status"] = status;

            var schedules = _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .AsQueryable();

            // Default filter - tampilkan schedule hari ini jika tidak ada filter
            if (!startDate.HasValue && !endDate.HasValue)
            {
                startDate = DateTime.Today;
                endDate = DateTime.Today.AddDays(7);
            }

            if (startDate.HasValue)
            {
                schedules = schedules.Where(s => s.ScheduledDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                schedules = schedules.Where(s => s.ScheduledDate <= endDate.Value);
            }

            if (customerId.HasValue)
            {
                schedules = schedules.Where(s => s.CustomerId == customerId.Value);
            }

            if (!string.IsNullOrEmpty(status))
            {
                schedules = schedules.Where(s => s.Status == status);
            }

            var rawResults = await schedules.ToListAsync();

            // Paginate by GROUP (Dock+Route+Cycle+Date), bukan raw records
            // Supaya total kanban per group selalu lengkap (tidak terpotong pagination)
            int pageSize = 20; // jumlah GROUP per halaman

            var orderedResults = rawResults
                .OrderBy(s =>
                {
                    if (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue))
                        return 0;
                    if (s.Status == "Completed" || s.ActualEndTime.HasValue)
                        return 2;
                    if (s.Status == "Cancelled")
                        return 3;
                    return 1;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ETD ?? s.PickupTime ?? s.EnterDockTime ?? DateTime.MaxValue)
                .ThenBy(s => s.ScheduleId)
                .ToList();

            // Group dulu, lalu paginate groups
            var allGroups = orderedResults
                .GroupBy(s => new {
                    Dock  = (string.IsNullOrEmpty(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                })
                .ToList();

            var totalGroups = allGroups.Count;
            var pagedGroups = allGroups
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Flatten kembali ke list schedules — tapi setiap group LENGKAP
            var items = pagedGroups.SelectMany(g => g).ToList();

            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalGroups / (double)pageSize);
            ViewBag.TotalItems = totalGroups; // tampilkan count group, bukan raw rows
            ViewBag.RouteData = new Dictionary<string, string> { 
                { "startDate", startDate?.ToString("yyyy-MM-dd") },
                { "endDate", endDate?.ToString("yyyy-MM-dd") },
                { "customerId", customerId?.ToString() },
                { "status", status }
            };

            // Hitung pending preparations (belum terikat jadwal)
            ViewBag.PendingPrepCount = await _context.PreparationRecords.CountAsync(p => p.ScheduleId == null);

            return View(items);
        }

        // POST: DeliverySchedules/SyncPendingNow — manual trigger untuk sinkronisasi pending preparations
        [HttpPost]
        [AuthorizeAdmin]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncPendingNow()
        {
            int synced = await _syncService.SyncAllPendingAsync();
            TempData["SuccessMessage"] = synced > 0
                ? $"✅ {synced} record preparation PENDING berhasil disinkronisasi ke jadwal."
                : "ℹ️ Tidak ada pending preparation yang bisa disinkronisasi saat ini (belum ada jadwal yang sesuai, atau memang sudah kosong).";
            return RedirectToAction(nameof(Index));
        }

        // GET: DeliverySchedules/Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var today = DateTime.Today;
            
            // Statistics
            ViewBag.TodaySchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.ScheduledDate.Date == today && s.Status != "Cancelled");
            
            ViewBag.InProgressCount = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.Status == "In Progress");
            
            ViewBag.CompletedToday = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.ScheduledDate.Date == today && s.Status == "Completed");
            
            ViewBag.DelayedCount = await _context.DeliverySchedules
                .AsNoTracking()
                .CountAsync(s => s.Status == "Delayed");

            // Today's schedules
            var todaySchedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Where(s => s.ScheduledDate.Date == today)
                .OrderBy(s => s.ETD)
                .ToListAsync();

            return View(todaySchedules);
        }

        // GET: DeliverySchedules/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            // Fetch candidate schedules for the same Customer, Date
            var candidates = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.CustomerId == schedule.CustomerId && 
                           s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                           s.ScheduleId != schedule.ScheduleId)
                .ToListAsync();

            // Perform matching grouping filter client-side to ensure 100% consistency with Dashboard grouping
            var scheduleArea = (string.IsNullOrWhiteSpace(schedule.Area) ? (schedule.Customer?.Docking ?? "") : schedule.Area).Trim().ToUpper();
            var scheduleCycle = (schedule.Cycle ?? "").Trim().ToUpper();
            var scheduleRoute = (string.IsNullOrWhiteSpace(schedule.Route) ? "" : schedule.Route).Trim().ToUpper();
            var today = DateTime.Today;

            var groupSchedules = candidates
                .Where(s => {
                    var sArea = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper();
                    var sCycle = (s.Cycle ?? "").Trim().ToUpper();
                    var sRoute = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper();
                    if (sArea != scheduleArea || sCycle != scheduleCycle || sRoute != scheduleRoute)
                        return false;

                    if (s.Status == "Cancelled")
                        return false;

                    return true;
                })
                .ToList();

            ViewBag.GroupSchedules = groupSchedules;

            var allSchedules = new List<DeliverySchedule> { schedule };
            allSchedules.AddRange(groupSchedules);

            var combinedItems = allSchedules
                .SelectMany(s => s.DeliveryItems.Select(di => new { 
                    ManifestNumber = s.ScheduleNumber, 
                    ScheduleId = s.ScheduleId,
                    ItemTag = !string.IsNullOrWhiteSpace(di.ExternalPartNo) 
                        ? di.ExternalPartNo.Trim() 
                        : (di.Item?.VIN ?? di.Item?.ItemCode ?? di.Item?.CustomerPartNumber ?? di.Item?.ItemName ?? ""),
                    di.DeliveryItemId
                }))
                .ToList();

            var scanDetailsMap = new Dictionary<int, object>();
            foreach (var item in combinedItems)
            {
                var scanLogsList = await GetScanDetailsListInternal(item.ManifestNumber, item.ItemTag, item.ScheduleId);
                scanDetailsMap[item.DeliveryItemId] = scanLogsList;
            }
            ViewBag.ScanDetailsMap = scanDetailsMap;

            return View(schedule);
        }

        // GET: DeliverySchedules/BulkCreate - Bulk scheduling dengan tabel checklist
        [AuthorizeAdmin]
        public async Task<IActionResult> BulkCreate(DateTime? selectedDate)
        {
            var scheduledDate = selectedDate ?? DateTime.Today;
            
            // Get all active customers
            var customers = await _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .OrderBy(c => c.CustomerCode)
                .ToListAsync();
            
            var customerItems = customers.Select(c => new CustomerScheduleItem
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Route = c.Route,
                Cycle = c.Cycle,
                Docking = c.Docking,
                Pickup = c.Pickup,
                ETD = c.ETD,
                Range = c.Range,
                SKID = c.SKID,
                Area = c.Area,
                IsSelected = false,
                // Check apakah customer seharusnya dijadwalkan di hari ini (berdasarkan Cycle)
                IsMatchingDay = ShouldScheduleCustomerOnDate(c, scheduledDate)
            }).ToList();
            
            var viewModel = new BulkScheduleViewModel
            {
                ScheduledDate = scheduledDate,
                AvailableCustomers = customerItems,
                IsAutoGenerate = true
            };
            
            return View(viewModel);
        }
        
        // POST: DeliverySchedules/BulkCreate - Process bulk scheduling
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAdmin]
        public async Task<IActionResult> BulkCreate(BulkScheduleViewModel model)
        {
            try
            {
                var schedules = new List<DeliverySchedule>();
                List<Customer> customers;

                // MODE OTOMATIS: generate berdasarkan Cycle & hari, hanya untuk hari kerja
                if (model.IsAutoGenerate)
                {
                    if (model.ScheduledDate.DayOfWeek == DayOfWeek.Saturday || model.ScheduledDate.DayOfWeek == DayOfWeek.Sunday)
                    {
                        TempData["ErrorMessage"] = "Mode otomatis hanya berlaku untuk hari Senin–Jumat. Untuk Sabtu/Minggu silakan gunakan mode manual.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .AsNoTracking() // Added AsNoTracking
                        .Where(c => c.IsActive)
                        .ToListAsync();

                    customers = customers
                        .Where(c => ShouldScheduleCustomerOnDate(c, model.ScheduledDate))
                        .ToList();

                    if (!customers.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang memenuhi kriteria Cycle untuk tanggal ini.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }
                }
                else
                {
                    if (model.SelectedCustomerIds == null || !model.SelectedCustomerIds.Any())
                    {
                        TempData["ErrorMessage"] = "Tidak ada customer yang dipilih! Silakan checklist minimal 1 customer.";
                        return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
                    }

                    customers = await _context.Customers
                        .AsNoTracking() // Added AsNoTracking
                        .Where(c => model.SelectedCustomerIds.Contains(c.CustomerId))
                        .ToListAsync();
                }
                
                // Get starting sequence
                int sequenceNumber = await GetNextSequenceInternal(model.ScheduledDate);
                
                foreach (var customer in customers)
                {
                    var prefix = $"SCH-{model.ScheduledDate:yyyyMMdd}";
                    var scheduleNumber = $"{prefix}{sequenceNumber:D3}";
                    sequenceNumber++;

                    // Parse waktu berdasarkan tanggal jadwal (Pickup dianggap sebagai hari H)
                    // Parse waktu berdasarkan tanggal jadwal (H0)
                    var pickupTime = ParseTimeToDateTime(customer.Pickup, model.ScheduledDate);
                    var enterDockTime = ParseTimeToDateTime(customer.Docking, model.ScheduledDate);
                    var etdTime = ParseTimeToDateTime(customer.ETD, model.ScheduledDate);
                    
                    // Gunakan StartPrepare sebagai anchor untuk menentukan H+1
                    // Jika jam milestone < jam mulai persiapan, maka itu adalah besok harinya
                    var startPrepMinutes = customer.StartPrepareTime;
                    var startPrepTime = model.ScheduledDate.Date.AddMinutes(startPrepMinutes);

                    if (enterDockTime.HasValue && enterDockTime.Value < startPrepTime)
                    {
                        enterDockTime = enterDockTime.Value.AddDays(1);
                    }

                    if (pickupTime.HasValue && pickupTime.Value < startPrepTime)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }

                    if (etdTime.HasValue && etdTime.Value < startPrepTime)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }

                    // Double Checks: Pastikan urutan logis tetap terjaga (Dock -> Pickup -> ETD)
                    if (pickupTime.HasValue && enterDockTime.HasValue && pickupTime.Value < enterDockTime.Value)
                    {
                        pickupTime = pickupTime.Value.AddDays(1);
                    }
                    if (etdTime.HasValue && pickupTime.HasValue && etdTime.Value < pickupTime.Value)
                    {
                        etdTime = etdTime.Value.AddDays(1);
                    }
                    
                    var schedule = new DeliverySchedule
                    {
                        ScheduleNumber = scheduleNumber,
                        CustomerId = customer.CustomerId,
                        ScheduledDate = model.ScheduledDate,
                        Route = customer.Route,
                        Cycle = customer.Cycle,
                        EnterDockTime = enterDockTime,
                        PickupTime = pickupTime,
                        ETD = etdTime,
                        Range = customer.Range,
                        SKID = ParseSKID(customer.SKID),
                        Area = customer.Area,
                        StartPrepareTime = customer.StartPrepareTime,
                        StdPrepareTime = customer.StdPrepareTime,
                        Status = "Scheduled",
                        CreatedDate = DateTime.Now,
                        CreatedBy = User.Identity?.Name ?? "System"
                    };
                    
                    schedules.Add(schedule);
                }
                
                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // Auto-sync pending preparations ke jadwal yang baru dibuat
                        var newScheduleIds = schedules.Select(s => s.ScheduleId).Where(id => id > 0).ToList();
                        if (newScheduleIds.Any())
                        {
                            int synced = await _syncService.SyncPendingForSchedulesAsync(newScheduleIds);
                            if (synced > 0)
                            {
                                TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal yang baru dibuat.";
                            }
                        }
                        
                        // Notify Dashboard via SignalR
                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                        {
                            action = "bulk_create",
                            message = $"Berhasil membuat {schedules.Count} schedule delivery untuk tanggal {model.ScheduledDate:dd/MM/yyyy}",
                            timestamp = DateTime.Now
                        });

                        TempData["SuccessMessage"] = $"✅ Berhasil membuat {schedules.Count} schedule delivery untuk tanggal {model.ScheduledDate:dd/MM/yyyy}!";
                        return RedirectToAction(nameof(Index), new { startDate = model.ScheduledDate.ToString("yyyy-MM-dd"), endDate = model.ScheduledDate.ToString("yyyy-MM-dd") });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Error saat bulk create schedule: {ex.Message}";
            }
            
            return RedirectToAction(nameof(BulkCreate), new { selectedDate = model.ScheduledDate });
        }
        
        // Helper: Get nama hari dalam bahasa Indonesia
        private string GetIndonesianDayName(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Sunday => "Minggu",
                DayOfWeek.Monday => "Senin",
                DayOfWeek.Tuesday => "Selasa",
                DayOfWeek.Wednesday => "Rabu",
                DayOfWeek.Thursday => "Kamis",
                DayOfWeek.Friday => "Jumat",
                DayOfWeek.Saturday => "Sabtu",
                _ => ""
            };
        }
        
        // Helper: Get kode hari 3 huruf (Inggris) misal MON, TUE, WED
        private string GetEnglishShortDayCode(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Sunday => "SUN",
                DayOfWeek.Monday => "MON",
                DayOfWeek.Tuesday => "TUE",
                DayOfWeek.Wednesday => "WED",
                DayOfWeek.Thursday => "THU",
                DayOfWeek.Friday => "FRI",
                DayOfWeek.Saturday => "SAT",
                _ => ""
            };
        }

        // Helper: Check apakah cycle berisi informasi hari (Indonesia atau kode 3 huruf Inggris)
        private bool IsValidDayCycle(string? cycle)
        {
            if (string.IsNullOrWhiteSpace(cycle))
                return false;
                
            var daysId = new[] { "Senin", "Selasa", "Rabu", "Kamis", "Jumat", "Sabtu", "Minggu" };
            var daysEn = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
            return daysId.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase)) ||
                   daysEn.Any(day => cycle.Contains(day, StringComparison.OrdinalIgnoreCase));
        }

        // Helper: logika utama apakah customer dijadwalkan pada tanggal tertentu
        private bool ShouldScheduleCustomerOnDate(Customer customer, DateTime date)
        {
            // Customer tanpa cycle dianggap bisa setiap hari kerja
            if (string.IsNullOrWhiteSpace(customer.Cycle))
                return true;

            var cycle = customer.Cycle.Trim();
            var hariId = GetIndonesianDayName(date);
            var hariEn = GetEnglishShortDayCode(date);

            // Jika cycle eksplisit menyebut hari (Indonesia / Inggris 3 huruf)
            if (cycle.Contains(hariId, StringComparison.OrdinalIgnoreCase) ||
                cycle.Contains(hariEn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Jika cycle tidak valid sebagai info hari, anggap berlaku setiap hari kerja
            if (!IsValidDayCycle(cycle))
                return true;

            // Selain itu, tidak dijadwalkan di hari ini
            return false;
        }
        
        // Helper: Parse waktu HH:mm ke DateTime
        private DateTime? ParseTimeToDateTime(string? timeString, DateTime baseDate)
        {
            if (string.IsNullOrWhiteSpace(timeString))
                return null;
                
            if (TimeSpan.TryParse(timeString, out TimeSpan time))
            {
                return baseDate.Date.Add(time);
            }
            
            return null;
        }
        
        // Helper: Get SKID string
        private string? ParseSKID(string? skidString)
        {
            if (string.IsNullOrWhiteSpace(skidString))
                return null;
                
            return skidString.Trim();
        }

        // GET: DeliverySchedules/Create
        [AuthorizeAdmin]
        public IActionResult Create()
        {
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName");
            
            // Generate Schedule Number
            var scheduleNumber = GenerateScheduleNumber();
            
            var model = new DeliverySchedule
            {
                ScheduleNumber = scheduleNumber,
                ScheduledDate = DateTime.Today,
                Status = "Scheduled"
            };
            
            return View(model);
        }
        
        // Helper method untuk generate schedule number
        private string GenerateScheduleNumber()
        {
            var today = DateTime.Today;
            var prefix = $"SCH-{today:yyyyMMdd}";
            
            // Cari schedule number terakhir hari ini
            var lastSchedule = _context.DeliverySchedules
                .AsNoTracking() // Added AsNoTracking
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefault();
            
            int sequenceNumber = 1;
            if (lastSchedule != null)
            {
                // Extract sequence number dari schedule terakhir
                var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
                if (int.TryParse(lastSequence, out int lastNum))
                {
                    sequenceNumber = lastNum + 1;
                }
            }
            
            return $"{prefix}{sequenceNumber:D3}";
        }
        
        // API untuk get customer details
        [HttpGet]
        public IActionResult GetCustomerDetails(int customerId)
        {
            var customer = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.CustomerId == customerId)
                .Select(c => new {
                    route = c.Route ?? "",
                    cycle = c.Cycle ?? "",
                    skid = c.SKID ?? "",
                    area = c.Area ?? "",
                    startPrepareTime = c.StartPrepareTime,
                    stdPrepareTime = c.StdPrepareTime
                })
                .FirstOrDefault();
            
            if (customer == null)
            {
                return NotFound();
            }
            
            return Json(customer);
        }

        private async Task<int> GetNextSequenceInternal(DateTime date)
        {
            var prefix = $"SCH-{date:yyyyMMdd}";
            var lastSchedule = await _context.DeliverySchedules
                .AsNoTracking() // Added AsNoTracking
                .Where(s => s.ScheduleNumber.StartsWith(prefix))
                .OrderByDescending(s => s.ScheduleNumber)
                .FirstOrDefaultAsync();

            if (lastSchedule == null) return 1;

            var lastSequence = lastSchedule.ScheduleNumber.Substring(prefix.Length);
            if (int.TryParse(lastSequence, out int lastNum))
            {
                return lastNum + 1;
            }
            return 1;
        }

        // POST: DeliverySchedules/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAdmin]
        public async Task<IActionResult> Create([Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,ActualEnterDockTime,PickupTime,ETD,Range,SKID,Area,VehicleNumber,DriverName,DriverPhone,Notes,Status,TotalTargetQuantity,TotalActualQuantity")] DeliverySchedule schedule)
        {
            // Remove validation for optional fields
            ModelState.Remove("Route");
            ModelState.Remove("Cycle");
            ModelState.Remove("PickupTime");
            ModelState.Remove("ETD");
            ModelState.Remove("Range");
            ModelState.Remove("SKID");
            ModelState.Remove("Area");
            ModelState.Remove("VehicleNumber");
            ModelState.Remove("DriverName");
            ModelState.Remove("DriverPhone");
            ModelState.Remove("Notes");
            
            // Remove validation for navigation properties (will be loaded from database)
            ModelState.Remove("Customer");
            ModelState.Remove("Dock");
            ModelState.Remove("DeliveryItems");
            
            if (ModelState.IsValid)
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Generate Schedule Number if not provided
                    if (string.IsNullOrEmpty(schedule.ScheduleNumber) || schedule.ScheduleNumber == "SCH-001")
                    {
                        var sequence = await GetNextSequenceInternal(schedule.ScheduledDate);
                        schedule.ScheduleNumber = $"SCH-{schedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }

                    // Check for collision and retry once if needed
                    var exists = await _context.DeliverySchedules.AnyAsync(s => s.ScheduleNumber == schedule.ScheduleNumber);
                    if (exists)
                    {
                        var sequence = await GetNextSequenceInternal(schedule.ScheduledDate);
                        schedule.ScheduleNumber = $"SCH-{schedule.ScheduledDate:yyyyMMdd}{sequence:D3}";
                    }

                    schedule.CreatedDate = DateTime.Now;
                    schedule.CreatedBy = User.Identity?.Name ?? "System";
                    _context.Add(schedule);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    
                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "create",
                        message = $"Schedule baru {schedule.ScheduleNumber} telah dibuat",
                        timestamp = DateTime.Now
                    });

                    TempData["SuccessMessage"] = "Schedule berhasil ditambahkan!";
                    return RedirectToAction(nameof(Index), new { startDate = schedule.ScheduledDate.ToString("yyyy-MM-dd"), endDate = schedule.ScheduledDate.ToString("yyyy-MM-dd") });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = $"Gagal menyimpan schedule: {ex.Message}";
                    // Fallthrough to return view
                }
            }
            
            // Log errors for debugging
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            if (errors.Any())
            {
                TempData["ErrorMessage"] = $"Validasi gagal: {string.Join(", ", errors)}";
            }
            
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // GET: DeliverySchedules/Edit/5
        [AuthorizeAdmin]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ScheduleId == id);
            
            if (schedule == null)
            {
                return NotFound();
            }
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // POST: DeliverySchedules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAdmin]
        public async Task<IActionResult> Edit(int id, [Bind("ScheduleId,ScheduleNumber,CustomerId,ScheduledDate,Route,Cycle,EnterDockTime,ActualEnterDockTime,PickupTime,ETD,Range,SKID,Area,ActualStartTime,ActualEndTime,ActualPickupTime,VehicleNumber,DriverName,DriverPhone,Notes,Status,CreatedDate,CreatedBy,TotalTargetQuantity,TotalActualQuantity,StartPrepareTime,StdPrepareTime")] DeliverySchedule schedule)
        {
            if (id != schedule.ScheduleId)
            {
                return NotFound();
            }

            // Remove validation for navigation properties
            ModelState.Remove("Customer");
            ModelState.Remove("Dock");
            ModelState.Remove("DeliveryItems");

            if (ModelState.IsValid)
            {
                // Strict Guard: Tidak bisa Enter Dock jika target scan kanban belum 100%
                if (schedule.ActualEnterDockTime.HasValue)
                {
                    var items = await _context.DeliveryItems
                        .Include(di => di.Item)
                        .Where(di => di.ScheduleId == schedule.ScheduleId)
                        .ToListAsync();

                    if (items.Any())
                    {
                        bool kanbanNotComplete = items.Any(di => {
                            var qtyLot = (di.Item?.QtyLot > 0) ? di.Item.QtyLot.Value : 1;
                            var kanbanTarget = (int)Math.Ceiling((double)di.Quantity / qtyLot);
                            var kanbanActual = (int)Math.Ceiling((double)(di.ActualQuantity ?? 0) / qtyLot);
                            return kanbanTarget > 0 && kanbanActual < kanbanTarget;
                        });

                        if (kanbanNotComplete)
                        {
                            ModelState.AddModelError("ActualEnterDockTime", "❌ Tidak bisa Enter Dock: Scan kanban belum 100% terpenuhi! Selesaikan target kanban terlebih dahulu.");
                        }
                    }
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    schedule.UpdatedDate = DateTime.Now;
                    schedule.UpdatedBy = User.Identity?.Name ?? "System";
                    _context.Update(schedule);
                    await _context.SaveChangesAsync();
                    
                    // Jalankan sinkronisasi ulang setelah jadwal diupdate (antisipasi perubahan jam yang cocok dengan pending record)
                    try {
                        await _syncService.SyncAllPendingAsync();
                    } catch (Exception ex) {
                        // Log error tapi jangan gagalkan proses utama
                        Console.WriteLine("Sync error after Edit: " + ex.Message);
                    }
                    
                    // Load customer data untuk SignalR message (jika belum loaded)
                    if (schedule.Customer == null)
                    {
                        await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
                    }
                    
                    TempData["SuccessMessage"] = "Schedule berhasil diupdate!";
                    
                    // Kirim SignalR notification untuk update dashboard real-time
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "update",
                        message = $"Schedule {schedule.ScheduleNumber} ({schedule.Customer?.CustomerName ?? "N/A"}) telah diupdate",
                        timestamp = DateTime.Now
                    });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ScheduleExists(schedule.ScheduleId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            var customerList = _context.Customers
                .AsNoTracking() // Added AsNoTracking
                .Where(c => c.IsActive)
                .Select(c => new { 
                    c.CustomerId, 
                    DisplayName = $"{c.CustomerCode} - {c.CustomerName}" 
                })
                .ToList();

            ViewData["CustomerId"] = new SelectList(customerList, "CustomerId", "DisplayName", schedule.CustomerId);
            return View(schedule);
        }

        // GET: DeliverySchedules/StartDelivery/5
        [AuthorizeAdmin]
        public async Task<IActionResult> StartDelivery(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null)
            {
                return NotFound();
            }

            schedule.ActualStartTime = DateTime.Now;
            schedule.Status = "In Progress";
            schedule.UpdatedDate = DateTime.Now;
            schedule.UpdatedBy = User.Identity?.Name ?? "System";
            
            await _context.SaveChangesAsync();
            
            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
            
            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "arrival",
                message = $"Delivery ke {schedule.Customer?.CustomerName} dimulai",
                timestamp = DateTime.Now
            });
            
            TempData["SuccessMessage"] = "Delivery dimulai!";
            
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: DeliverySchedules/CompleteDelivery/5
        [AuthorizeAdmin]
        public async Task<IActionResult> CompleteDelivery(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules.FindAsync(id);
            if (schedule == null)
            {
                return NotFound();
            }

            schedule.ActualEndTime = DateTime.Now;
            schedule.Status = "Completed";
            schedule.UpdatedDate = DateTime.Now;
            schedule.UpdatedBy = User.Identity?.Name ?? "System";
            
            await _context.SaveChangesAsync();
            
            // Load customer data untuk SignalR message
            await _context.Entry(schedule).Reference(s => s.Customer).LoadAsync();
            
            // Hitung durasi jika ada ActualStartTime
            string durationText = "";
            if (schedule.ActualStartTime.HasValue)
            {
                var duration = schedule.ActualEndTime.Value - schedule.ActualStartTime.Value;
                durationText = duration.TotalHours >= 1 
                    ? $"{(int)duration.TotalHours} jam {duration.Minutes} menit"
                    : $"{(int)duration.TotalMinutes} menit";
            }
            
            // Broadcast update via SignalR
            await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
            {
                scheduleNumber = schedule.ScheduleNumber,
                action = "departure",
                message = $"Delivery ke {schedule.Customer?.CustomerName} selesai{(string.IsNullOrEmpty(durationText) ? "" : $". Durasi: {durationText}")}",
                timestamp = DateTime.Now
            });
            
            TempData["SuccessMessage"] = "Delivery selesai!";
            
            return RedirectToAction(nameof(Index));
        }

        // GET: DeliverySchedules/Delete/5
        [AuthorizeAdmin]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);
            
            if (schedule == null)
            {
                return NotFound();
            }

            return View(schedule);
        }

        // POST: DeliverySchedules/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [AuthorizeAdmin]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .Include(s => s.PreparationRecords)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule != null)
            {
                try
                {
                    // 1. Hapus delivery items (manual backup jika cascade terhambat)
                    if (schedule.DeliveryItems != null && schedule.DeliveryItems.Any())
                    {
                        _context.DeliveryItems.RemoveRange(schedule.DeliveryItems);
                    }

                    // 2. Lepaskan/Unlink preparation records agar tidak hilang (penting untuk laporan)
                    if (schedule.PreparationRecords != null && schedule.PreparationRecords.Any())
                    {
                        foreach (var prep in schedule.PreparationRecords)
                        {
                            prep.ScheduleId = null;
                        }
                    }

                    // 3. Hapus schedule utama
                    _context.DeliverySchedules.Remove(schedule);
                    await _context.SaveChangesAsync();

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        scheduleNumber = schedule.ScheduleNumber,
                        action = "delete",
                        message = $"Schedule {schedule.ScheduleNumber} telah dihapus",
                        timestamp = DateTime.Now
                    });

                    TempData["SuccessMessage"] = "Schedule berhasil dihapus!";
                }
                catch (DbUpdateException ex)
                {
                    TempData["ErrorMessage"] = $"Error saat menghapus schedule: {ex.InnerException?.Message ?? ex.Message}";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ScheduleExists(int id)
        {
            return _context.DeliverySchedules.Any(e => e.ScheduleId == id);
        }

        // POST: Bulk Delete Schedules
        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeAdmin]
        public async Task<IActionResult> BulkDelete(string selectedIds)
        {
            if (string.IsNullOrEmpty(selectedIds))
            {
                TempData["ErrorMessage"] = "Tidak ada schedule yang dipilih untuk dihapus!";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Parse IDs dengan error handling
                var ids = new List<int>();
                foreach (var idStr in selectedIds.Split(','))
                {
                    if (int.TryParse(idStr.Trim(), out int id))
                    {
                        ids.Add(id);
                    }
                }

                if (!ids.Any())
                {
                    TempData["ErrorMessage"] = "Tidak ada ID schedule yang valid!";
                    return RedirectToAction(nameof(Index));
                }

                // Load schedules dengan dependent items
        var schedulesToDelete = await _context.DeliverySchedules
            .Include(s => s.DeliveryItems)
            .Include(s => s.PreparationRecords)
            .Where(s => ids.Contains(s.ScheduleId))
            .ToListAsync();

        if (!schedulesToDelete.Any())
        {
            TempData["ErrorMessage"] = "Schedule tidak ditemukan!";
            return RedirectToAction(nameof(Index));
        }

        int totalItems = schedulesToDelete.Sum(s => s.DeliveryItems?.Count ?? 0);
        int totalPrepRecords = schedulesToDelete.Sum(s => s.PreparationRecords?.Count ?? 0);
        int deletedSchedules = schedulesToDelete.Count;

        // 1. Hapus delivery items terlebih dahulu
        var allDeliveryItems = schedulesToDelete
            .Where(s => s.DeliveryItems != null && s.DeliveryItems.Any())
            .SelectMany(s => s.DeliveryItems)
            .ToList();

        if (allDeliveryItems.Any())
        {
            _context.DeliveryItems.RemoveRange(allDeliveryItems);
        }

        // 2. Lepaskan link preparation records agar data tetap ada (penting untuk laporan)
        var allPrepRecords = schedulesToDelete
            .Where(s => s.PreparationRecords != null && s.PreparationRecords.Any())
            .SelectMany(s => s.PreparationRecords)
            .ToList();

        if (allPrepRecords.Any())
        {
            foreach (var prep in allPrepRecords)
            {
                prep.ScheduleId = null;
            }
        }

        // 3. Hapus schedules
        _context.DeliverySchedules.RemoveRange(schedulesToDelete);
        await _context.SaveChangesAsync();

                // Notify Dashboard via SignalR
                await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                {
                    action = "bulk_delete",
                    message = $"Berhasil menghapus {deletedSchedules} schedule dan {totalItems} item",
                    timestamp = DateTime.Now
                });

                TempData["SuccessMessage"] = $"✅ Berhasil menghapus {deletedSchedules} schedule{(deletedSchedules > 1 ? "" : "")} dan {totalItems} delivery item{(totalItems != 1 ? "s" : "")}!";
            }
            catch (DbUpdateException dbEx)
            {
                TempData["ErrorMessage"] = $"❌ Error database saat menghapus: {dbEx.InnerException?.Message ?? dbEx.Message}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error saat bulk delete: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // Download Excel Template
        public IActionResult DownloadTemplate()
        {
            var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Schedule Template");

            // Header
            worksheet.Cell(1, 1).Value = "CUST";
            worksheet.Cell(1, 2).Value = "ROUTE";
            worksheet.Cell(1, 3).Value = "CYCLE";
            worksheet.Cell(1, 4).Value = "ENTER DOCK";
            worksheet.Cell(1, 5).Value = "PICKUP";
            worksheet.Cell(1, 6).Value = "ETD";
            worksheet.Cell(1, 7).Value = "Range";
            worksheet.Cell(1, 8).Value = "SKID";
            worksheet.Cell(1, 9).Value = "AREA";
            worksheet.Cell(1, 10).Value = "START PREP";
            worksheet.Cell(1, 11).Value = "END PREP";
            worksheet.Cell(1, 12).Value = "QTY TARGET";

            // Style header
            var headerRange = worksheet.Range(1, 1, 1, 12);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            // Add sample data
            worksheet.Cell(2, 1).Value = "CUST001";
            worksheet.Cell(2, 2).Value = "Route A";
            worksheet.Cell(2, 3).Value = "Cycle 1";
            worksheet.Cell(2, 4).Value = "2025-01-20 07:30";
            worksheet.Cell(2, 5).Value = "2025-01-20 08:00";
            worksheet.Cell(2, 6).Value = "2025-01-20 09:00";
            worksheet.Cell(2, 7).Value = "10-15 KM";
            worksheet.Cell(2, 8).Value = 10;
            worksheet.Cell(2, 9).Value = "Area 1";
            worksheet.Cell(2, 10).Value = 15;
            worksheet.Cell(2, 11).Value = 30;
            worksheet.Cell(2, 12).Value = 100;

            // Auto fit columns
            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DeliverySchedule_Template.xlsx");
        }

        // Import Excel
        [HttpPost]
        [AuthorizeAdmin]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["ErrorMessage"] = "File tidak valid!";
                return RedirectToAction(nameof(Create));
            }

            if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            {
                TempData["ErrorMessage"] = "File harus berformat Excel (.xlsx atau .xls)!";
                return RedirectToAction(nameof(Create));
            }

            try
            {
                var schedules = new List<DeliverySchedule>();
                var errors = new List<string>();
                var batchSequences = new Dictionary<string, int>();
                var allCustomers = await _context.Customers.AsNoTracking().ToListAsync();
                var customersByCode = allCustomers
                    .Where(c => !string.IsNullOrWhiteSpace(c.CustomerCode))
                    .GroupBy(c => c.CustomerCode.Trim().ToUpper())
                    .ToDictionary(g => g.Key, g => g.First());

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RangeUsed().RowsUsed().Skip(1); // Skip header

                int rowNumber = 1;
                foreach (var row in rows)
                {
                    rowNumber++;
                    try
                    {
                        var custCode = row.Cell(1).GetString().Trim();
                        var route = row.Cell(2).GetString().Trim();
                        var cycle = row.Cell(3).GetString().Trim();
                        var enterDockStr = row.Cell(4).GetString().Trim();
                        var pickupStr = row.Cell(5).GetString().Trim();
                        var etdStr = row.Cell(6).GetString().Trim();
                        var range = row.Cell(7).GetString().Trim();
                        var skidStr = row.Cell(8).GetString().Trim();
                        var area = row.Cell(9).GetString().Trim();
                        var targetQtyStr = row.Cell(12).GetString().Trim();
                        var startPrepStr = row.Cell(10).GetString().Trim();
                        var endPrepStr = row.Cell(11).GetString().Trim();

                        // Validasi customer
                        if (!customersByCode.ContainsKey(custCode))
                        {
                            errors.Add($"Baris {rowNumber}: Customer '{custCode}' tidak ditemukan!");
                            continue;
                        }

                        var customer = customersByCode[custCode];
                        var customerId = customer.CustomerId;

                        // Parse datetime
                        DateTime? enterDockTime = null;
                        DateTime? pickupTime = null;
                        DateTime? etd = null;

                        if (!string.IsNullOrEmpty(enterDockStr) && DateTime.TryParse(enterDockStr, out var enterDock))
                        {
                            enterDockTime = enterDock;
                        }

                        if (!string.IsNullOrEmpty(pickupStr) && DateTime.TryParse(pickupStr, out var pickup))
                        {
                            pickupTime = pickup;
                        }

                        if (!string.IsNullOrEmpty(etdStr) && DateTime.TryParse(etdStr, out var etdParsed))
                        {
                            etd = etdParsed;
                        }

                        // Get SKID
                        string? skid = string.IsNullOrWhiteSpace(skidStr) ? null : skidStr;

                        // Parse Target Qty
                        decimal targetQty = 0;
                        if (!string.IsNullOrEmpty(targetQtyStr) && decimal.TryParse(targetQtyStr, out var targetParsed))
                        {
                            targetQty = targetParsed;
                        }

                        // Generate schedule number
                        var scheduledDate = etd?.Date ?? DateTime.Today;
                        var prefix = $"SCH-{scheduledDate:yyyyMMdd}";
                        
                        // We use a local sequence counter for the batch to avoid duplicate IDs 
                        // before they are committed to the DB
                        if (!batchSequences.ContainsKey(prefix))
                        {
                            batchSequences[prefix] = await GetNextSequenceInternal(scheduledDate);
                        }
                        
                        var sequence = batchSequences[prefix]++;
                        var scheduleNumber = $"{prefix}{sequence:D3}";

                        var schedule = new DeliverySchedule
                        {
                            ScheduleNumber = scheduleNumber,
                            CustomerId = customerId,
                            Route = string.IsNullOrWhiteSpace(route) ? customer.Route : route,
                            Cycle = string.IsNullOrWhiteSpace(cycle) ? customer.Cycle : cycle,
                            Range = string.IsNullOrWhiteSpace(range) ? customer.Range : range,
                            SKID = skid ?? customer.SKID,
                            Area = string.IsNullOrWhiteSpace(area) ? customer.Area : area,
                            StartPrepareTime = ParsePrepTime(startPrepStr) != 0 ? ParsePrepTime(startPrepStr) : customer.StartPrepareTime,
                            StdPrepareTime = ParsePrepTime(endPrepStr) != 0 ? ParsePrepTime(endPrepStr) : customer.StdPrepareTime,
                            TotalTargetQuantity = targetQty,
                            ScheduledDate = etd?.Date ?? DateTime.Today,
                            Status = "Scheduled",
                            CreatedDate = DateTime.Now,
                            CreatedBy = User.Identity?.Name ?? "System"
                        };

                        var eDockTime = ParseTimeToDateTime(customer.Docking, scheduledDate);
                        var pTime = pickupTime ?? ParseTimeToDateTime(customer.Pickup, scheduledDate);
                        var eTime = etd ?? ParseTimeToDateTime(customer.ETD, scheduledDate);

                        // Gunakan StartPrepare sebagai anchor untuk menentukan H+1
                        var sPrepMinutes = schedule.StartPrepareTime;
                        var sPrepTime = schedule.ScheduledDate.Date.AddMinutes(sPrepMinutes);

                        if (eDockTime.HasValue && eDockTime.Value < sPrepTime)
                            eDockTime = eDockTime.Value.AddDays(1);

                        if (pTime.HasValue && pTime.Value < sPrepTime)
                            pTime = pTime.Value.AddDays(1);

                        if (eTime.HasValue && eTime.Value < sPrepTime)
                            eTime = eTime.Value.AddDays(1);

                        // Double checks for logic consistency
                        if (pTime.HasValue && eDockTime.HasValue && pTime.Value < eDockTime.Value)
                            pTime = pTime.Value.AddDays(1);
                        if (eTime.HasValue && pTime.HasValue && eTime.Value < pTime.Value)
                            eTime = eTime.Value.AddDays(1);

                        schedule.EnterDockTime = eDockTime;
                        schedule.PickupTime = pTime;
                        schedule.ETD = eTime;

                        schedules.Add(schedule);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Baris {rowNumber}: {ex.Message}");
                    }
                }

                if (errors.Any())
                {
                    TempData["ErrorMessage"] = $"Import gagal! Ditemukan {errors.Count} error:\n" + string.Join("\n", errors.Take(10));
                    return RedirectToAction(nameof(Create));
                }

                if (schedules.Any())
                {
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        _context.DeliverySchedules.AddRange(schedules);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();

                        // Auto-sync pending preparations ke jadwal yang baru di-import
                        var importedScheduleIds = schedules.Select(s => s.ScheduleId).Where(id => id > 0).ToList();
                        if (importedScheduleIds.Any())
                        {
                            int synced = await _syncService.SyncPendingForSchedulesAsync(importedScheduleIds);
                            if (synced > 0)
                            {
                                TempData["InfoMessage"] = $"{synced} record preparation PENDING telah tersinkronisasi ke jadwal yang baru di-import.";
                            }
                        }

                    // Notify Dashboard via SignalR
                    await _hubContext.Clients.All.SendAsync("deliveryUpdated", new
                    {
                        action = "import",
                        message = $"Berhasil import {schedules.Count} schedule dari Excel",
                        timestamp = DateTime.Now
                    });

                        TempData["SuccessMessage"] = $"Berhasil import {schedules.Count} schedule dari Excel!";
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = "Tidak ada data yang valid untuk diimport!";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error saat import Excel: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
        // GET: DeliverySchedules/ExportExcel
        public async Task<IActionResult> ExportExcel(DateTime? startDate, DateTime? endDate, int? customerId, string status)
        {
            var schedules = _context.DeliverySchedules
                .Include(d => d.Customer)
                .AsNoTracking()
                .AsQueryable();

            if (startDate.HasValue) schedules = schedules.Where(s => s.ScheduledDate >= startDate.Value);
            if (endDate.HasValue) schedules = schedules.Where(s => s.ScheduledDate <= endDate.Value);
            if (customerId.HasValue) schedules = schedules.Where(s => s.CustomerId == customerId.Value);
            if (!string.IsNullOrEmpty(status)) schedules = schedules.Where(s => s.Status == status);

            var data = await schedules.OrderBy(s => s.ScheduledDate).ThenBy(s => s.ETD).ToListAsync();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Delivery Schedules");

            // Headers
            string[] headers = { "SCH NO", "DATE", "CUSTOMER", "ROUTE", "CYCLE", "DOCK IN", "PICKUP", "ETD", "START PREP", "END PREP", "QTY TARGET", "QTY ACTUAL", "STATUS" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightBlue;
            }

            // Data
            int row = 2;
            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.ScheduleNumber;
                worksheet.Cell(row, 2).Value = item.ScheduledDate.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 3).Value = item.Customer?.CustomerName ?? "-";
                worksheet.Cell(row, 4).Value = item.Route;
                worksheet.Cell(row, 5).Value = item.Cycle;
                worksheet.Cell(row, 6).Value = item.EnterDockTime?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 7).Value = item.PickupTime?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 8).Value = item.ETD?.ToString("HH:mm") ?? "-";
                worksheet.Cell(row, 9).Value = FormatPrepTime(item.StartPrepareTime);
                worksheet.Cell(row, 10).Value = FormatPrepTime(item.StdPrepareTime);
                worksheet.Cell(row, 11).Value = (double)item.TotalTargetQuantity;
                worksheet.Cell(row, 12).Value = (double)item.TotalActualQuantity;
                worksheet.Cell(row, 13).Value = item.Status;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DeliverySchedules_{DateTime.Now:yyyyMMdd}.xlsx");
        }
        /// <summary>
        /// API endpoint untuk mendapatkan data tabel history schedule terbaru via AJAX.
        /// Mendukung filter yang sama dengan Index.
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> GetHistoryScheduleTableData(DateTime? startDate, DateTime? endDate, int? customerId, string? status)
        {
            // Default filter - tampilkan schedule hari ini jika tidak ada filter (konsisten dengan Index)
            if (!startDate.HasValue && !endDate.HasValue)
            {
                startDate = DateTime.Today;
                endDate = DateTime.Today.AddDays(7);
            }

            var query = _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(d => d.ScheduledDate.Date >= startDate.Value.Date);
            }
            if (endDate.HasValue)
            {
                query = query.Where(d => d.ScheduledDate.Date <= endDate.Value.Date);
            }
            if (customerId.HasValue)
            {
                query = query.Where(d => d.CustomerId == customerId.Value);
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(d => d.Status == status);
            }

            var results = await query.ToListAsync();
            // Urutkan: 
            // 1. In Progress di paling atas
            // 2. Yang belum complete (ActualEndTime null) di tengah  
            // 3. Yang sudah complete (ActualEndTime ada nilai) di paling bawah
            var sortedSchedules = results
                .OrderBy(s =>
                {
                    // Delivery complete (ActualEndTime ada) → paling bawah
                    if (s.ActualEndTime.HasValue)
                        return 2;
                    // In Progress → atas
                    if (s.Status == "In Progress" || s.PreparationStatus == "In Progress" || 
                        (s.ActualStartTime.HasValue && !s.ActualEndTime.HasValue) || 
                        (s.ActualEnterDockTime.HasValue && !s.ActualEndTime.HasValue))
                        return 0;
                    // Cancelled → paling bawah
                    if (s.Status == "Cancelled")
                        return 3;
                    return 1;
                })
                .ThenBy(s => s.ScheduledDate)
                .ThenBy(s => s.ETD ?? s.PickupTime ?? s.EnterDockTime ?? DateTime.MaxValue)
                .ThenBy(s => s.ScheduleId)
                .ToList();

            return PartialView("_HistoryScheduleTablePartial", sortedSchedules);
        }
        private int ParsePrepTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim();
            if (value.Contains(":"))
            {
                // Try format HH:mm or H:mm
                var parts = value.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int mins))
                {
                    return (hours * 60) + mins;
                }
                
                if (TimeSpan.TryParse(value, out var ts))
                {
                    return (int)ts.TotalMinutes;
                }
            }
            
            // Try numeric
            if (double.TryParse(value, out double numericVal))
            {
                // If it's a fractional number like 0.8263888 (Excel's way of representing time)
                if (numericVal < 1 && numericVal > 0)
                {
                    return (int)Math.Round(numericVal * 24 * 60);
                }
                return (int)Math.Round(numericVal);
            }
            
            return 0;
        }

        private string FormatPrepTime(int totalMinutes)
        {
            if (totalMinutes <= 0) return "00:00";
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }

        [HttpPost]
        [DeliveryControl.Filters.AuthorizeRoles("Admin")]
        public async Task<IActionResult> ClearDeliveryData()
        {
            var count = await _context.DeliverySchedules.CountAsync();
            _context.DeliverySchedules.RemoveRange(_context.DeliverySchedules);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = $"Berhasil menghapus {count} data Jadwal Delivery." });
        }
        public class ScanDetailDto
        {
            public string waktuScan { get; set; } = "";
            public string deliveryDate { get; set; } = "";
            public string dock { get; set; } = "";
            public string cycle { get; set; } = "";
            public string vin { get; set; } = "";
            public string plant { get; set; } = "";
            public string tag { get; set; } = "";
            public string label { get; set; } = "";
            public string kanban { get; set; } = "";
            public string createdBy { get; set; } = "";
        }

        private async Task<List<ScanDetailDto>> GetScanDetailsListInternal(string manifest, string itemTag, int? scheduleId = null)
        {
            if (string.IsNullOrEmpty(manifest) || string.IsNullOrEmpty(itemTag))
                return new List<ScanDetailDto>();

            manifest = manifest.Trim();
            itemTag  = itemTag.Trim();

            var targetSchedule = scheduleId.HasValue
                ? await _context.DeliverySchedules.Include(s => s.Customer).AsNoTracking().FirstOrDefaultAsync(s => s.ScheduleId == scheduleId.Value)
                : await _context.DeliverySchedules.Include(s => s.Customer).AsNoTracking().FirstOrDefaultAsync(s => s.ScheduleNumber == manifest);

            int? targetScheduleId = targetSchedule?.ScheduleId;

            // Cari DeliveryItem spesifik di schedule ini yang cocok dengan itemTag
            DeliveryItem? matchedDeliveryItem = null;
            if (targetScheduleId.HasValue)
            {
                matchedDeliveryItem = await _context.DeliveryItems
                    .Include(di => di.Item)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(di => di.ScheduleId == targetScheduleId.Value &&
                        (di.ExternalPartNo == itemTag ||
                         (di.Item != null && (di.Item.VIN == itemTag || di.Item.ItemCode == itemTag || di.Item.CustomerPartNumber == itemTag || di.Item.ItemName == itemTag))));
            }

            if (matchedDeliveryItem == null)
            {
                matchedDeliveryItem = await _context.DeliveryItems
                    .Include(di => di.Item)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(di => (di.DeliverySchedule.ScheduleNumber == manifest || (targetScheduleId.HasValue && di.ScheduleId == targetScheduleId.Value)) &&
                        (di.ExternalPartNo == itemTag ||
                         (di.Item != null && (di.Item.VIN == itemTag || di.Item.ItemCode == itemTag || di.Item.CustomerPartNumber == itemTag || di.Item.ItemName == itemTag))));
            }

            Item? itemMaster = matchedDeliveryItem?.Item;
            if (itemMaster == null && matchedDeliveryItem?.ItemId != null)
            {
                itemMaster = await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.ItemId == matchedDeliveryItem.ItemId);
            }
            if (itemMaster == null)
            {
                itemMaster = await _context.Items.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.VIN == itemTag || i.ItemCode == itemTag || i.CustomerPartNumber == itemTag || i.ItemName == itemTag);
            }
            
            var vinDisplay = itemMaster?.VIN ?? (matchedDeliveryItem?.Item?.VIN ?? "undefined");
            var plantDisplay = itemMaster?.Plant ?? (matchedDeliveryItem?.Item?.Plant ?? "undefined");

            // Hitung Target Kanban untuk item ini pada jadwal ini
            int qpc = (itemMaster?.QtyLot != null && itemMaster.QtyLot > 0)
                ? itemMaster.QtyLot.Value
                : (matchedDeliveryItem?.Item?.QtyLot != null && matchedDeliveryItem.Item.QtyLot > 0 ? matchedDeliveryItem.Item.QtyLot.Value : 1);
            if (qpc <= 0) qpc = 1;
            int targetKbn = matchedDeliveryItem != null ? (int)Math.Ceiling((double)matchedDeliveryItem.Quantity / qpc) : 0;
            int actualKbn = matchedDeliveryItem != null ? (int)Math.Ceiling((double)(matchedDeliveryItem.ActualQuantity ?? 0) / qpc) : 0;

            var tagCandidates = new List<string> { itemTag };
            var normalizedTag = DeliveryControl.Helpers.VinHelper.Normalize(itemTag);
            if (!string.IsNullOrEmpty(normalizedTag))
            {
                tagCandidates.Add(normalizedTag);
            }

            if (matchedDeliveryItem != null && !string.IsNullOrWhiteSpace(matchedDeliveryItem.ExternalPartNo))
            {
                tagCandidates.Add(matchedDeliveryItem.ExternalPartNo.Trim());
                var normExt = DeliveryControl.Helpers.VinHelper.Normalize(matchedDeliveryItem.ExternalPartNo);
                if (!string.IsNullOrEmpty(normExt)) tagCandidates.Add(normExt);
            }

            if (itemMaster != null)
            {
                if (!string.IsNullOrEmpty(itemMaster.VIN))
                {
                    tagCandidates.Add(itemMaster.VIN);
                    var norm = DeliveryControl.Helpers.VinHelper.Normalize(itemMaster.VIN);
                    if (!string.IsNullOrEmpty(norm)) tagCandidates.Add(norm);
                }
                if (!string.IsNullOrEmpty(itemMaster.ItemCode))
                {
                    tagCandidates.Add(itemMaster.ItemCode);
                    var norm = DeliveryControl.Helpers.VinHelper.Normalize(itemMaster.ItemCode);
                    if (!string.IsNullOrEmpty(norm)) tagCandidates.Add(norm);
                }
                if (!string.IsNullOrEmpty(itemMaster.CustomerPartNumber))
                {
                    tagCandidates.Add(itemMaster.CustomerPartNumber);
                    var norm = DeliveryControl.Helpers.VinHelper.Normalize(itemMaster.CustomerPartNumber);
                    if (!string.IsNullOrEmpty(norm)) tagCandidates.Add(norm);
                }
            }

            tagCandidates = tagCandidates
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t.Trim().ToUpper())
                .Distinct()
                .ToList();

            // Match SPESIFIK untuk manifest jadwal ini (HANYA scheduleId / manifest ini)
            var recordsQuery = _context.PreparationRecords
                .Include(p => p.DeliverySchedule)
                .AsQueryable();

            if (targetScheduleId.HasValue)
            {
                recordsQuery = recordsQuery.Where(p => 
                    p.ScheduleId == targetScheduleId.Value ||
                    (!string.IsNullOrEmpty(p.ManifestNumber) && p.ManifestNumber.Trim() == manifest) ||
                    (p.DeliverySchedule != null && p.DeliverySchedule.ScheduleNumber.Trim() == manifest));
            }
            else
            {
                recordsQuery = recordsQuery.Where(p => 
                    (!string.IsNullOrEmpty(p.ManifestNumber) && p.ManifestNumber.Trim() == manifest) ||
                    (p.DeliverySchedule != null && p.DeliverySchedule.ScheduleNumber.Trim() == manifest));
            }

            var records = await recordsQuery
                .OrderBy(p => p.CreatedDate)
                .ToListAsync();

            // 1. Matching VIN / Tag
            var filteredRecords = records.Where(r => {
                var rTagUpper = (r.Tag ?? "").Trim().ToUpper();
                if (string.IsNullOrEmpty(rTagUpper)) return false;
                if (tagCandidates.Contains(rTagUpper)) return true;
                return tagCandidates.Any(tc => 
                    DeliveryControl.Helpers.VinHelper.IsMatch(rTagUpper, tc) ||
                    rTagUpper.Contains(tc) ||
                    tc.Contains(rTagUpper));
            }).ToList();

            // 2. Matching KANBAN / Schedule
            if (targetScheduleId.HasValue)
            {
                filteredRecords = filteredRecords
                    .Where(r => r.ScheduleId == targetScheduleId.Value ||
                                (!string.IsNullOrEmpty(r.ManifestNumber) && r.ManifestNumber.Trim().ToUpper() == manifest.Trim().ToUpper()) ||
                                DeliveryControl.Helpers.VinHelper.IsKanbanBelongsToManifest(r.Kanban, manifest))
                    .ToList();
            }
            else
            {
                filteredRecords = filteredRecords
                    .Where(r => (!string.IsNullOrEmpty(r.ManifestNumber) && r.ManifestNumber.Trim().ToUpper() == manifest.Trim().ToUpper()) ||
                                DeliveryControl.Helpers.VinHelper.IsKanbanBelongsToManifest(r.Kanban, manifest))
                    .ToList();
            }

            // 3. Matching DOCK dan CYCLE (TMMIN & ADM)
            if (targetSchedule != null)
            {
                var targetDock = (string.IsNullOrWhiteSpace(targetSchedule.Area) ? (targetSchedule.Customer?.Docking ?? "") : targetSchedule.Area).Trim().ToUpper();
                var targetCycle = (targetSchedule.Cycle ?? "").Trim().ToUpper();

                if (!string.IsNullOrEmpty(targetCycle) && targetCycle != "-")
                {
                    filteredRecords = filteredRecords.Where(r => 
                        r.ScheduleId == (targetScheduleId ?? -1) ||
                        string.IsNullOrEmpty(r.DockCycle) || r.DockCycle == "-" || r.DockCycle.Trim().ToUpper() == targetCycle
                    ).ToList();
                }

                if (!string.IsNullOrEmpty(targetDock) && targetDock != "-")
                {
                    filteredRecords = filteredRecords.Where(r => 
                        r.ScheduleId == (targetScheduleId ?? -1) ||
                        string.IsNullOrEmpty(r.DockName) || r.DockName == "-" || 
                        r.DockName.Trim().ToUpper().Contains(targetDock) || targetDock.Contains(r.DockName.Trim().ToUpper())
                    ).ToList();
                }
            }

            // Filter out system adjustment records from UI
            filteredRecords = filteredRecords
                .Where(r => r.Kanban != "OPNAME-REDUCE")
                .OrderBy(r => r.CreatedDate)
                .ToList();

            // Deduplikasi barcode kanban
            var uniqueRecords = new List<PreparationRecord>();
            var seenBarcodes = new HashSet<string>();

            foreach (var r in filteredRecords)
            {
                if (!string.IsNullOrEmpty(r.Kanban))
                {
                    var kbnUpper = r.Kanban.Trim().ToUpper();
                    if ((kbnUpper.StartsWith("KBN") || kbnUpper.StartsWith("DN")) && kbnUpper.Length >= 12)
                    {
                        if (seenBarcodes.Contains(kbnUpper))
                        {
                            continue;
                        }
                        seenBarcodes.Add(kbnUpper);
                    }
                }
                uniqueRecords.Add(r);
            }

            // Batasi jumlah detail scan agar sesuai target kanban / actual kanban orderan ini
            int maxAllowed = targetKbn > 0 ? Math.Max(targetKbn, actualKbn) : actualKbn;
            if (maxAllowed > 0 && uniqueRecords.Count > maxAllowed)
            {
                uniqueRecords = uniqueRecords.Take(maxAllowed).ToList();
            }

            var finalRecords = uniqueRecords.ToList();

            return finalRecords.Select(r => new ScanDetailDto {
                waktuScan = r.CreatedDate.ToString("dd/MM/yyyy HH:mm:ss"),
                deliveryDate = r.TargetDate.HasValue ? r.TargetDate.Value.ToString("dd/MM/yyyy") : (r.DeliverySchedule != null ? r.DeliverySchedule.ScheduledDate.ToString("dd/MM/yyyy") : "undefined"),
                dock = r.DockName ?? "-",
                cycle = r.DockCycle ?? "-",
                vin = vinDisplay,
                plant = string.IsNullOrEmpty(r.Plant) ? plantDisplay : r.Plant,
                tag = r.Tag,
                label = r.Label,
                kanban = r.Kanban,
                createdBy = (!string.IsNullOrWhiteSpace(r.CreatedBy) && r.CreatedBy != "-") ? r.CreatedBy : "Operator"
            }).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> GetScanDetails(string manifest, string itemTag, int? scheduleId = null)
        {
            if (string.IsNullOrEmpty(manifest) || string.IsNullOrEmpty(itemTag))
                return Json(new { success = false, message = "Manifest atau Item tidak valid." });

            var data = await GetScanDetailsListInternal(manifest, itemTag, scheduleId);
            return Json(new { success = true, data = data });
        }

        // GET: DeliverySchedules/PrintManifest/5
        [HttpGet]
        public async Task<IActionResult> PrintManifest(int id, int? manifestId)
        {
            var userRole = HttpContext.Session.GetString("Role") ?? "";
            if (userRole != "Super Admin" && userRole != "Admin" && userRole != "PPIC")
            {
                return Unauthorized();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            var custName = (schedule.Customer?.CustomerName ?? "").ToUpper();
            var custCode = (schedule.Customer?.CustomerCode ?? "").ToUpper();
            if (!custName.Contains("ADM") && !custCode.Contains("ADM") && 
                !custName.Contains("TMMIN") && !custCode.Contains("TMMIN"))
            {
                return Unauthorized();
            }

            var candidates = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.CustomerId == schedule.CustomerId && 
                           s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                           s.ScheduleId != schedule.ScheduleId)
                .ToListAsync();

            var scheduleArea = (string.IsNullOrWhiteSpace(schedule.Area) ? (schedule.Customer?.Docking ?? "") : schedule.Area).Trim().ToUpper();
            var scheduleCycle = (schedule.Cycle ?? "").Trim().ToUpper();
            var scheduleRoute = (string.IsNullOrWhiteSpace(schedule.Route) ? "" : schedule.Route).Trim().ToUpper();
            var today = DateTime.Today;

            var groupSchedules = candidates
                .Where(s => {
                    var sArea = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper();
                    var sCycle = (s.Cycle ?? "").Trim().ToUpper();
                    var sRoute = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper();
                    if (sArea != scheduleArea || sCycle != scheduleCycle || sRoute != scheduleRoute)
                        return false;
                    if (s.Status == "Cancelled")
                        return false;
                    return true;
                })
                .ToList();

            var allSchedules = new List<DeliverySchedule> { schedule };
            allSchedules.AddRange(groupSchedules);

            if (manifestId.HasValue)
            {
                allSchedules = allSchedules.Where(s => s.ScheduleId == manifestId.Value).ToList();
            }

            var combinedItems = allSchedules
                .SelectMany(s => s.DeliveryItems.Select(di => new { 
                    MainScheduleNumber = schedule.ScheduleNumber,
                    ManifestNumber = s.ScheduleNumber, 
                    ScheduleId = s.ScheduleId,
                    ScheduledDate = s.ScheduledDate,
                    Customer = s.Customer,
                    Area = string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area,
                    Cycle = s.Cycle ?? "",
                    Item = di.Item, 
                    di.Quantity, 
                    di.ActualQuantity, 
                    di.PrepScanTime,
                    di.IsCompleted, 
                    di.DeliveryItemId,
                    di.ExternalPartNo
                }))
                .ToList();

            var itemsWithStatus = combinedItems.Select(gi => {
                var qpc0 = gi.Item?.QtyLot ?? 1; if (qpc0 <= 0) qpc0 = 1;
                var plan0 = Math.Ceiling((double)gi.Quantity / qpc0);
                var act0  = Math.Ceiling((double)(gi.ActualQuantity ?? 0) / qpc0);
                var var0  = act0 - plan0;
                int sortOrder = (act0 >= plan0 && plan0 > 0) ? 2 : (act0 > 0 ? 1 : 0);
                return new { gi.MainScheduleNumber, gi.ManifestNumber, gi.ScheduleId, gi.ScheduledDate, gi.Customer, gi.Area, gi.Cycle, gi.Item, gi.Quantity, gi.ActualQuantity, gi.PrepScanTime, gi.IsCompleted, gi.DeliveryItemId, gi.ExternalPartNo,
                    KbnPlan = plan0, KbnActual = act0, KbnVariance = var0, SortOrder = sortOrder };
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ManifestNumber)
            .ThenBy(x => x.Item?.VIN ?? x.Item?.ItemCode)
            .ToList();

            ViewBag.TotalTarget = itemsWithStatus.Sum(i => (int)i.KbnPlan);
            ViewBag.TotalActual = itemsWithStatus.Sum(i => (int)i.KbnActual);

            var printModelList = new List<dynamic>();

            foreach (var item in itemsWithStatus)
            {
                var itemTag = item.Item?.VIN ?? item.Item?.ItemCode ?? item.Item?.CustomerPartNumber ?? item.Item?.ItemName ?? "";
                var scanLogsList = await GetScanDetailsListInternal(item.ManifestNumber, itemTag, item.ScheduleId);

                var getJobsNo = new Func<string, string>((k) => {
                    if (string.IsNullOrEmpty(k)) return "-";
                    var dashIndex = k.IndexOf('-');
                    if (dashIndex >= 2 && dashIndex + 4 < k.Length)
                        return k.Substring(dashIndex - 2, 7);
                    return "-";
                });

                string operatorName = "-";
                string labelSupplier = "-";
                string jobsNo = "-";
                if (scanLogsList != null && scanLogsList.Count > 0)
                {
                    operatorName = scanLogsList.First().createdBy ?? "-";
                    labelSupplier = string.IsNullOrEmpty(scanLogsList.First().label) ? "-": scanLogsList.First().label;
                    jobsNo = getJobsNo(scanLogsList.First().kanban);
                }
                
                if (labelSupplier == "-")
                {
                    labelSupplier = !string.IsNullOrEmpty(item.Item?.ItemCode) ? item.Item.ItemCode : "-";
                }

                string partNo = !string.IsNullOrWhiteSpace(item.Item?.CustomerPartNumber) ? item.Item.CustomerPartNumber : (!string.IsNullOrWhiteSpace(item.ExternalPartNo) ? item.ExternalPartNo : "-");

                string judgement = (item.KbnActual >= item.KbnPlan && item.KbnPlan > 0) ? "OK" : "-";

                printModelList.Add(new {
                    NoOrder = item.MainScheduleNumber,
                    NoManifest = item.ManifestNumber,
                    TanggalDelivery = item.ScheduledDate.ToString("dd/MM/yyyy"),
                    Customer = !string.IsNullOrEmpty(item.Customer?.CustomerCode) ? item.Customer.CustomerCode : (item.Customer?.CustomerName ?? "-"),
                    Dock = !string.IsNullOrEmpty(item.Customer?.CustomerName) ? item.Customer.CustomerName : (!string.IsNullOrEmpty(item.Area) ? item.Area : "-"),
                    Cycle = !string.IsNullOrEmpty(item.Cycle) ? item.Cycle : "-",
                    Vin = !string.IsNullOrEmpty(item.Item?.VIN) ? item.Item.VIN : "-",
                    LabelSupplier = labelSupplier,
                    PartNo = partNo,
                    JobsNo = jobsNo,
                    Judgement = judgement,
                    TargetKanban = item.KbnPlan,
                    ActKanban = item.KbnActual,
                    Operator = operatorName
                });
            }

            ViewBag.PrintModelList = printModelList;
            ViewBag.MainSchedule = schedule;
            ViewBag.TotalManifests = allSchedules.Count;

            return View(schedule);
        }

        // GET: DeliverySchedules/ExportKanbanDetailExcel/5
        [HttpGet]
        public async Task<IActionResult> ExportKanbanDetailExcel(int id)
        {
            var userRole = HttpContext.Session.GetString("Role") ?? "";
            if (userRole != "Super Admin" && userRole != "Admin" && userRole != "PPIC")
            {
                return Unauthorized();
            }

            var schedule = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(d => d.Customer)
                .Include(d => d.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(m => m.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            var candidates = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.CustomerId == schedule.CustomerId && 
                           s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                           s.ScheduleId != schedule.ScheduleId)
                .ToListAsync();

            var scheduleArea = (string.IsNullOrWhiteSpace(schedule.Area) ? (schedule.Customer?.Docking ?? "") : schedule.Area).Trim().ToUpper();
            var scheduleCycle = (schedule.Cycle ?? "").Trim().ToUpper();
            var scheduleRoute = (string.IsNullOrWhiteSpace(schedule.Route) ? "" : schedule.Route).Trim().ToUpper();
            var today = DateTime.Today;

            var groupSchedules = candidates
                .Where(s => {
                    var sArea = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper();
                    var sCycle = (s.Cycle ?? "").Trim().ToUpper();
                    var sRoute = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper();
                    if (sArea != scheduleArea || sCycle != scheduleCycle || sRoute != scheduleRoute)
                        return false;
                    if (s.Status == "Cancelled")
                        return false;
                    return true;
                })
                .ToList();

            var allSchedules = new List<DeliverySchedule> { schedule };
            allSchedules.AddRange(groupSchedules);

            var combinedItems = allSchedules
                .SelectMany(s => s.DeliveryItems.Select(di => new { 
                    MainScheduleNumber = schedule.ScheduleNumber,
                    ManifestNumber = s.ScheduleNumber, 
                    ScheduleId = s.ScheduleId,
                    ScheduledDate = s.ScheduledDate,
                    Customer = s.Customer,
                    Area = string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area,
                    Cycle = s.Cycle ?? "",
                    Item = di.Item, 
                    di.Quantity, 
                    di.ActualQuantity, 
                    di.PrepScanTime,
                    di.IsCompleted, 
                    di.DeliveryItemId,
                    di.ExternalPartNo
                }))
                .ToList();

            var itemsWithStatus = combinedItems.Select(gi => {
                var qpc0 = gi.Item?.QtyLot ?? 1; if (qpc0 <= 0) qpc0 = 1;
                var plan0 = Math.Ceiling((double)gi.Quantity / qpc0);
                var act0  = Math.Ceiling((double)(gi.ActualQuantity ?? 0) / qpc0);
                var var0  = act0 - plan0;
                int sortOrder = (act0 >= plan0 && plan0 > 0) ? 2 : (act0 > 0 ? 1 : 0);
                return new { gi.MainScheduleNumber, gi.ManifestNumber, gi.ScheduleId, gi.ScheduledDate, gi.Customer, gi.Area, gi.Cycle, gi.Item, gi.Quantity, gi.ActualQuantity, gi.PrepScanTime, gi.IsCompleted, gi.DeliveryItemId, gi.ExternalPartNo,
                    KbnPlan = plan0, KbnActual = act0, KbnVariance = var0, SortOrder = sortOrder };
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ManifestNumber)
            .ThenBy(x => x.Item?.VIN ?? x.Item?.ItemCode)
            .ToList();

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Detail Kanban");

            // Header Info Title
            worksheet.Cell(1, 1).Value = "DETAIL SCAN KANBAN DASHBOARD SHIPPING";
            worksheet.Range(1, 1, 1, 8).Merge();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;
            worksheet.Cell(1, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#1e293b");

            var custDispName = !string.IsNullOrEmpty(schedule.Customer?.CustomerCode) ? schedule.Customer.CustomerCode : (schedule.Customer?.CustomerName ?? "-");
            worksheet.Cell(2, 1).Value = $"No. Order: {schedule.ScheduleNumber} | Customer: {custDispName} | Tanggal: {schedule.ScheduledDate:dd MMMM yyyy} | Grouped: {allSchedules.Count} Manifests";
            worksheet.Range(2, 1, 2, 8).Merge();
            worksheet.Cell(2, 1).Style.Font.Italic = true;
            worksheet.Cell(2, 1).Style.Font.FontSize = 10;
            worksheet.Cell(2, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#64748b");
            worksheet.Range(1, 1, 1, 14).Merge(); // Correct merge range for title
            worksheet.Range(2, 1, 2, 14).Merge(); // Correct merge range for subtitle

            string[] singleHeaders = new string[] {
                "NO ORDER", "NO MANIFEST", "TANGGAL DELIVERY", "CUSTOMER", "DOCK", "CYCLE"
            };
            string[] rightHeaders = new string[] {
                "SCAN KANBAN CUST.", "Judgement", "TARGET KANBAN", "ACT KANBAN", "OPERATOR"
            };

            for (int col = 0; col < singleHeaders.Length; col++)
            {
                var cell = worksheet.Cell(3, col + 1);
                cell.Value = singleHeaders[col];
                worksheet.Range(3, col + 1, 4, col + 1).Merge();
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e293b");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                worksheet.Cell(4, col + 1).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }

            var groupCell = worksheet.Cell(3, 7);
            groupCell.Value = "SCAN LABEL SUPPLIER";
            worksheet.Range(3, 7, 3, 9).Merge();
            groupCell.Style.Font.Bold = true;
            groupCell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.Yellow;
            groupCell.Style.Font.FontColor = ClosedXML.Excel.XLColor.Black;
            groupCell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            groupCell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;

            string[] subHeaders = new string[] { "VIN", "LABEL", "JOBS NO" };
            for(int i = 0; i < subHeaders.Length; i++)
            {
                var cell = worksheet.Cell(4, 7 + i);
                cell.Value = subHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e293b");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }

            for (int i = 0; i < rightHeaders.Length; i++)
            {
                var col = 10 + i;
                var cell = worksheet.Cell(3, col);
                cell.Value = rightHeaders[i];
                worksheet.Range(3, col, 4, col).Merge();
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e293b");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                worksheet.Cell(4, col).Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }

            int startRow = 4;
            int currentRow = startRow + 1;

            foreach (var item in itemsWithStatus)
            {
                var itemTag = item.Item?.VIN ?? item.Item?.ItemCode ?? item.Item?.CustomerPartNumber ?? item.Item?.ItemName ?? "";
                var scanLogsList = await GetScanDetailsListInternal(item.ManifestNumber, itemTag, item.ScheduleId);

                var getJobsNo = new Func<string, string>((k) => {
                    if (string.IsNullOrEmpty(k)) return "-";
                    var dashIndex = k.IndexOf('-');
                    if (dashIndex >= 2 && dashIndex + 4 < k.Length)
                        return k.Substring(dashIndex - 2, 7);
                    return "-";
                });

                int plan = (int)item.KbnPlan;
                int act = (int)item.KbnActual;
                int scanCount = scanLogsList != null ? scanLogsList.Count : 0;
                int maxRows = Math.Max(plan, scanCount);
                if (maxRows == 0) maxRows = 1;

                for (int i = 0; i < maxRows; i++)
                {
                    int rowTarget = (i < plan) ? 1 : 0;
                    int rowAct = (i < act) ? 1 : 0;

                    string waktuScanText = "-";
                    string operatorText = "-";
                    string jobsNoText = "-";
                    string labelText = "-";
                    string judgement = "-";

                    if (i < scanCount && scanLogsList != null)
                    {
                        var s = scanLogsList[i];
                        var kbnTagStr = string.IsNullOrEmpty(s.kanban) || s.kanban == "-" ? "" : $" ({s.kanban})";
                        waktuScanText = $"{s.waktuScan}{kbnTagStr}";
                        operatorText = string.IsNullOrEmpty(s.createdBy) ? "-" : s.createdBy;
                        jobsNoText = getJobsNo(s.kanban);
                        labelText = string.IsNullOrEmpty(s.label) ? (!string.IsNullOrEmpty(item.Item?.ItemCode) ? item.Item.ItemCode : "-") : s.label;
                    }
                    else
                    {
                        labelText = !string.IsNullOrEmpty(item.Item?.ItemCode) ? item.Item.ItemCode : "-";
                    }

                    if (rowAct >= rowTarget && rowTarget > 0)
                        judgement = "OK";
                    else if (rowTarget > 0 && rowAct == 0)
                        judgement = "-";
                    else if (rowAct > 0)
                        judgement = "OK"; // Extra scan

                    worksheet.Cell(currentRow, 1).Value = item.MainScheduleNumber;
                    worksheet.Cell(currentRow, 2).Value = item.ManifestNumber;
                    worksheet.Cell(currentRow, 3).Value = item.ScheduledDate.ToString("dd/MM/yyyy");
                    worksheet.Cell(currentRow, 4).Value = !string.IsNullOrEmpty(item.Customer?.CustomerCode) ? item.Customer.CustomerCode : (item.Customer?.CustomerName ?? "-");
                    worksheet.Cell(currentRow, 5).Value = !string.IsNullOrEmpty(item.Customer?.CustomerName) ? item.Customer.CustomerName : (!string.IsNullOrEmpty(item.Area) ? item.Area : "-");
                    worksheet.Cell(currentRow, 6).Value = !string.IsNullOrEmpty(item.Cycle) ? item.Cycle : "-";
                    worksheet.Cell(currentRow, 7).Value = !string.IsNullOrEmpty(item.Item?.VIN) ? item.Item.VIN : "-";
                    worksheet.Cell(currentRow, 8).Value = labelText;
                    worksheet.Cell(currentRow, 9).Value = jobsNoText;
                    worksheet.Cell(currentRow, 10).Value = waktuScanText;
                    worksheet.Cell(currentRow, 11).Value = judgement;
                    worksheet.Cell(currentRow, 12).Value = rowTarget;
                    worksheet.Cell(currentRow, 13).Value = rowAct;
                    worksheet.Cell(currentRow, 14).Value = operatorText;

                    for (int col = 1; col <= 14; col++)
                    {
                        var cell = worksheet.Cell(currentRow, col);
                        cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                        cell.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#cbd5e1");
                        cell.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;

                        if (col == 1 || col == 2 || col == 3 || col == 4 || col == 5 || col == 6 || col == 7 || col == 9 || col == 11 || col == 12 || col == 13)
                        {
                            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                        }
                        if (col == 10 || col == 14 || col == 8)
                        {
                            cell.Style.Alignment.WrapText = true;
                            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;
                        }
                    }

                    currentRow++;
                }
            }

            // ADD TOTAL ROW
            worksheet.Cell(currentRow, 1).Value = "TOTAL";
            worksheet.Range(currentRow, 1, currentRow, 11).Merge();
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Right;
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;

            worksheet.Cell(currentRow, 12).FormulaA1 = $"SUM(L5:L{currentRow - 1})";
            worksheet.Cell(currentRow, 12).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 12).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            
            worksheet.Cell(currentRow, 13).FormulaA1 = $"SUM(M5:M{currentRow - 1})";
            worksheet.Cell(currentRow, 13).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 13).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

            for (int col = 1; col <= 14; col++)
            {
                var cell = worksheet.Cell(currentRow, col);
                cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#cbd5e1");
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f1f5f9");
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Column(10).Width = 35;
            worksheet.Column(12).Width = 25;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var content = stream.ToArray();

            var fileName = $"Detail_Kanban_{schedule.ScheduleNumber}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        private static int GetShift(DeliverySchedule s)
        {
            if (!s.PickupTime.HasValue) return 1;
            var hour = s.PickupTime.Value.Hour;
            return hour < 14 ? 1 : 2;
        }

        public IActionResult TrendDelivery()
        {
            return View();
        }

        [HttpGet]
        [Microsoft.AspNetCore.Authorization.AllowAnonymous]
        public async Task<IActionResult> GetTrendDeliveryData(DateTime? date)
        {
            var targetDate = (date ?? DateTime.Today).Date;

            // Load mode manipulasi
            var forceOnTimeSetting = await _context.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "ForceOnTimeMode");
            bool forceOnTimeMode = forceOnTimeSetting?.Value == "true";

            // Range 1 bulan penuh berdasarkan targetDate
            var rangeStart = new DateTime(targetDate.Year, targetDate.Month, 1);
            var rangeEnd = rangeStart.AddMonths(1).AddDays(-1);

            // Kita load data dari queryStart (60 hari sebelum awal bulan) untuk cover carry-overs
            var queryStart = rangeStart.AddDays(-60);
            var queryEnd = rangeEnd.AddDays(1);

            var schedules = await _context.DeliverySchedules
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.Status != "Cancelled" && s.ScheduledDate >= queryStart && s.ScheduledDate < queryEnd.AddDays(1))
                .ToListAsync();

            // Grouping di memori untuk menghindari O(N^2) di dalam perulangan kalender
            var schedulesByDate = schedules
                .GroupBy(s => s.ScheduledDate.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 1. Data Harian (Daily Trend) untuk bulan berjalan
            var dailyLabels = new List<string>();
            var dailyPrepare = new List<object>();
            var dailyTruckIn = new List<object>();
            var dailyDelivery = new List<object>();

            for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
            {
                dailyLabels.Add(d.Day.ToString());

                // Tentukan effectiveNow untuk tanggal d
                DateTime effectiveNow;
                if (d < DateTime.Today)
                {
                    effectiveNow = d.AddDays(1).AddSeconds(-1); // Akhir hari d
                }
                else if (d == DateTime.Today)
                {
                    effectiveNow = DateTime.Now;
                }
                else
                {
                    // Tanggal di masa depan belum ada data delay
                    dailyPrepare.Add(new { count = 0, docks = new string[0] });
                    dailyTruckIn.Add(new { count = 0, docks = new string[0] });
                    dailyDelivery.Add(new { count = 0, docks = new string[0] });
                    continue;
                }

                // Hitung active schedules untuk tanggal d
                var activeSchedulesForDay = schedulesByDate.ContainsKey(d) ? schedulesByDate[d] : new List<DeliverySchedule>();

                var tableGroups = activeSchedulesForDay
                    .GroupBy(s => new {
                        CustId = s.CustomerId,
                        Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                        Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                        Route = (s.Route ?? "").Trim().ToUpper(),
                        Date  = s.ScheduledDate.Date
                    });

                int delayPrepareCount = 0;
                int delayPickupCount = 0;
                int delayDeliveryCount = 0;
                
                var groupDocksPrepare = new HashSet<string>();
                var groupDocksPickup = new HashSet<string>();
                var groupDocksDelivery = new HashSet<string>();

                foreach (var g in tableGroups)
                {
                    var groupSchedules = g.ToList();
                    var item = groupSchedules.First();
                    
                    var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                    var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                    var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                    var today = d.Date;
                    var daysAhead = (item.ScheduledDate.Date - today).Days;
                    var isFutureSchedule = daysAhead > 0;
                    var isH1Shift = isFutureSchedule &&
                                    (daysAhead == 1 ||
                                     (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                     (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                    var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today && item.PickupTime.HasValue && pickupHour < 12;
                    
                    var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                    var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                    var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                    var isPastCompletedGroup = item.ScheduledDate.Date < today && groupSchedules.All(s => 
                        (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow) || 
                        string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                        (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                    );

                    if (isPastCompletedGroup) continue;
                    if (isFutureSchedule && !isH1Shift) continue;

                    var dockInTimeTarget = item.EnterDockTime;
                    var pickupTimeTarget = item.PickupTime;
                    var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                    if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                    if (isH1Shift || isTodayH1 || isOvernightPrep) {
                        var h1ShiftDays = isH1Shift ? daysAhead : 1;
                        planEndTime = planEndTime.AddDays(-h1ShiftDays);
                    }

                    var isCarryOverSched = item.ScheduledDate.Date < d.Date;
                    var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                    while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                    while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                    
                    if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                        var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                        if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                    }
                    if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                        pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                    }

                    if (!isCarryOverSched) {
                        var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                        if (isH1Shift || isTodayH1 || isOvernightPrep) {
                            var h1ShiftDays = isH1Shift ? daysAhead : 1;
                            startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                        }
                        if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                        if (pickupTimeTarget.HasValue && !isTodayH1) {
                            var anchor = dockInTimeTarget ?? planEndTime;
                            if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                        }
                    }

                    var isCompleted = groupSchedules.All(s => 
                        (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow) || 
                        string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                        string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase)
                    );

                    double totalKanbanTarget = 0;
                    double totalKanbanActual = 0;
                    foreach(var s in groupSchedules)
                    {
                        if (s.DeliveryItems != null)
                        {
                            foreach(var di in s.DeliveryItems)
                            {
                                var qpc = di.Item?.QtyLot ?? 1;
                                if (qpc > 0)
                                {
                                    var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                    totalKanbanTarget += diKbnTarget;
                                    totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                        ? diKbnTarget
                                        : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                                }
                            }
                        }
                    }
                    bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                    string displayStatus = "Scheduled";
                    if (groupSchedules.All(s => s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow))
                        displayStatus = "Completed";
                    else if (groupSchedules.Any(s => s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= effectiveNow))
                        displayStatus = "In Progress";
                    else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                        displayStatus = "Prepared";
                    else if (groupSchedules.Any(s => s.ActualStartTime.HasValue && s.ActualStartTime.Value <= effectiveNow))
                        displayStatus = "In Progress";
                    else
                    {
                        bool startPrepTimeReached = true;
                        if (startPMinutes > 0)
                        {
                            DateTime startPrepDT = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                            if (isH1Shift || isTodayH1 || isOvernightPrep) {
                                var h1ShiftDays = isH1Shift ? daysAhead : 1;
                                startPrepDT = startPrepDT.AddDays(-h1ShiftDays);
                            }
                            startPrepTimeReached = effectiveNow >= startPrepDT;
                        }
                        if (startPrepTimeReached && groupSchedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                            displayStatus = "Preparing";
                        else 
                            displayStatus = (item.Status ?? item.DriverStatus) ?? "Scheduled";
                    }

                    if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                        && !isCompleted
                        && !groupSchedules.Any(s => (s.ActualStartTime.HasValue && s.ActualStartTime.Value <= effectiveNow) || (s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= effectiveNow))
                        && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    {
                        displayStatus = "Delayed";
                    }

                    var isDelayed = displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase);
                    var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);
                    
                    if (displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase) && 
                       (isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15))))
                    {
                        isPrepared = false;
                    }

                    var isDelayPickup = false;
                    var hasActualPickupOnDay = item.ActualPickupTime.HasValue && item.ActualPickupTime.Value <= effectiveNow;
                    if (!hasActualPickupOnDay && pickupTimeTarget.HasValue && !isCompleted) {
                        if (effectiveNow >= pickupTimeTarget.Value) isDelayPickup = true;
                    }
                    if (isCarryOverSched && !isCompleted && !hasActualPickupOnDay) {
                        isDelayPickup = true;
                    }

                    var isDelayDelivery = false;
                    var hasActualEndTimeOnDay = item.ActualEndTime.HasValue && item.ActualEndTime.Value <= effectiveNow;
                    if (!hasActualEndTimeOnDay && item.ETD.HasValue && !isCompleted) {
                        var etdTarget = item.ETD.Value;
                        if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                        if (effectiveNow >= etdTarget) isDelayDelivery = true;
                    }

                    var isDelayPrepare = false;
                    if (!isCompleted)
                    {
                        if (!isPrepared)
                        {
                            bool overdueEndPrep = stdPMinutes > 0 && effectiveNow >= planEndTime;
                            bool overdueDockIn  = dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value;
                            if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                        }
                        else
                        {
                            var groupMaxReadyTime = isKanbanFullyPrepared && groupSchedules.Any(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= effectiveNow) 
                                ? groupSchedules.Where(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= effectiveNow).Max(s => (DateTime?)s.ReadyToDockTime) 
                                : (DateTime?)null;
                            if (groupMaxReadyTime.HasValue)
                            {
                                var readyTime = groupMaxReadyTime.Value;
                                DateTime effectiveTarget;
                                if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                                    effectiveTarget = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                                else if (dockInTimeTarget.HasValue)
                                    effectiveTarget = dockInTimeTarget.Value;
                                else
                                    effectiveTarget = planEndTime;
                                    
                                if (readyTime > effectiveTarget) isDelayPrepare = true;
                            }
                            else
                            {
                                bool overdueEndPrep = stdPMinutes > 0 && effectiveNow >= planEndTime;
                                bool overdueDockIn  = dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value;
                                if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                            }
                        }
                    }
                    if (isCarryOverSched && !isCompleted && !isKanbanFullyPrepared)
                    {
                        isDelayPrepare = true;
                    }

                    var isPickupLate = false;
                    var planBaseDate = item.CreatedDate;
                    if (hasActualPickupOnDay && pickupTimeTarget.HasValue) {
                        var actualP = item.ActualPickupTime.Value;
                        if (planBaseDate > actualP && planBaseDate > pickupTimeTarget.Value) actualP = planBaseDate;
                        if ((actualP - pickupTimeTarget.Value).TotalMinutes > 0) isPickupLate = true;
                    }

                    var isDeliveryLate = false;
                    if (hasActualEndTimeOnDay && item.ETD.HasValue) {
                        var actualE = item.ActualEndTime.Value;
                        if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                        var etdForPunctuality = item.ETD.Value;
                        if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                        if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                    }

                    if (forceOnTimeMode)
                    {
                        isDelayPrepare = false;
                        isDelayPickup = false;
                        isDelayDelivery = false;
                        isPickupLate = false;
                        isDeliveryLate = false;
                    }

                    // --- H-1 schedules MUST also be counted in today's delay charts! ---
                    if (isDelayPrepare && !isCompleted) {
                        delayPrepareCount++;
                        var dockStr = string.IsNullOrWhiteSpace(item.Area) 
                            ? (item.Customer?.Docking ?? "UNKNOWN") 
                            : item.Area;
                        groupDocksPrepare.Add(dockStr);
                    }

                    bool hasPickupBadge = false;
                    if (hasActualPickupOnDay)
                    {
                        hasPickupBadge = isPickupLate;
                    }
                    else if (!isCompleted)
                    {
                        hasPickupBadge = isDelayPickup;
                    }
                    if (hasPickupBadge) {
                        delayPickupCount++;
                        var dockStr = string.IsNullOrWhiteSpace(item.Area) 
                            ? (item.Customer?.Docking ?? "UNKNOWN") 
                            : item.Area;
                        groupDocksPickup.Add(dockStr);
                    }

                    bool hasDeliveryBadge = false;
                    if (hasActualEndTimeOnDay)
                    {
                        hasDeliveryBadge = isDeliveryLate;
                    }
                    else if (!isCompleted)
                    {
                        hasDeliveryBadge = isDelayDelivery ||
                            (isCarryOverSched && !isCompleted) || 
                            (isTodayH1 && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value) || 
                            (isOvernightPrep && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                    }
                    if (hasDeliveryBadge) {
                        delayDeliveryCount++;
                        var dockStr = string.IsNullOrWhiteSpace(item.Area) 
                            ? (item.Customer?.Docking ?? "UNKNOWN") 
                            : item.Area;
                        groupDocksDelivery.Add(dockStr);
                    }
                }

                dailyPrepare.Add(new { count = delayPrepareCount, docks = groupDocksPrepare.ToList() });
                dailyTruckIn.Add(new { count = delayPickupCount, docks = groupDocksPickup.ToList() });
                dailyDelivery.Add(new { count = delayDeliveryCount, docks = groupDocksDelivery.ToList() });
            }

            // 2. Data Bulanan (Monthly Trend) untuk tahun berjalan
            var monthlyPrepare = new int[12];
            var monthlyTruckIn = new int[12];
            var monthlyDelivery = new int[12];

            for (int m = 1; m <= 12; m++)
            {
                var monthStart = new DateTime(targetDate.Year, m, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                int monthPrepDelays = 0;
                int monthTruckInDelays = 0;
                int monthDeliveryDelays = 0;

                for (var d = monthStart; d <= monthEnd; d = d.AddDays(1))
                {
                    DateTime effectiveNow;
                    if (d < DateTime.Today)
                    {
                        effectiveNow = d.AddDays(1).AddSeconds(-1);
                    }
                    else if (d == DateTime.Today)
                    {
                        effectiveNow = DateTime.Now;
                    }
                    else
                    {
                        continue;
                    }

                    var activeSchedulesForDay = schedulesByDate.ContainsKey(d) ? schedulesByDate[d] : new List<DeliverySchedule>();

                    var tableGroups = activeSchedulesForDay
                        .GroupBy(s => new {
                            CustId = s.CustomerId,
                            Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                            Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                            Route = (s.Route ?? "").Trim().ToUpper(),
                            Date  = s.ScheduledDate.Date
                        });

                    foreach (var g in tableGroups)
                    {
                        var groupSchedules = g.ToList();
                        var item = groupSchedules.First();
                        
                        var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                        var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                        var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                        var today = d.Date;
                        var daysAhead = (item.ScheduledDate.Date - today).Days;
                        var isFutureSchedule = daysAhead > 0;
                        var isH1Shift = isFutureSchedule &&
                                        (daysAhead == 1 ||
                                         (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                         (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                        var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today && item.PickupTime.HasValue && pickupHour < 12;
                        
                        var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                        var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                        var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                        var isPastCompletedGroup = item.ScheduledDate.Date < today && groupSchedules.All(s => 
                            (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow) || 
                            string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                            (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                        );

                        if (isPastCompletedGroup) continue;
                        if (isFutureSchedule && !isH1Shift) continue;

                        var dockInTimeTarget = item.EnterDockTime;
                        var pickupTimeTarget = item.PickupTime;
                        var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                        if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                        if (isH1Shift || isTodayH1 || isOvernightPrep) {
                            var h1ShiftDays = isH1Shift ? daysAhead : 1;
                            planEndTime = planEndTime.AddDays(-h1ShiftDays);
                        }

                        var isCarryOverSched = item.ScheduledDate.Date < d.Date;
                        var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                        while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                        while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                        
                        if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                            var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                            if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                        }
                        if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                            pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                        }

                        if (!isCarryOverSched) {
                            var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                            if (isH1Shift || isTodayH1 || isOvernightPrep) {
                                var h1ShiftDays = isH1Shift ? daysAhead : 1;
                                startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                            }
                            if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                            if (pickupTimeTarget.HasValue && !isTodayH1) {
                                var anchor = dockInTimeTarget ?? planEndTime;
                                if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                            }
                        }

                        var isCompleted = groupSchedules.All(s => 
                            (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow) || 
                            string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                            string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase)
                        );

                        double totalKanbanTarget = 0;
                        double totalKanbanActual = 0;
                        foreach(var s in groupSchedules)
                        {
                            if (s.DeliveryItems != null)
                            {
                                foreach(var di in s.DeliveryItems)
                                {
                                    var qpc = di.Item?.QtyLot ?? 1;
                                    if (qpc > 0)
                                    {
                                        var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                        totalKanbanTarget += diKbnTarget;
                                        totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                            ? diKbnTarget
                                            : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                                    }
                                }
                            }
                        }
                        bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                        string displayStatus = "Scheduled";
                        if (groupSchedules.All(s => s.ActualEndTime.HasValue && s.ActualEndTime.Value <= effectiveNow))
                            displayStatus = "Completed";
                        else if (groupSchedules.Any(s => s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= effectiveNow))
                            displayStatus = "In Progress";
                        else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                            displayStatus = "Prepared";
                        else if (groupSchedules.Any(s => s.ActualStartTime.HasValue && s.ActualStartTime.Value <= effectiveNow))
                            displayStatus = "In Progress";
                        else
                        {
                            bool startPrepTimeReached = true;
                            if (startPMinutes > 0)
                            {
                                DateTime startPrepDT = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                                 var h1ShiftDays = isH1Shift ? daysAhead : 1;
                                 startPrepDT = startPrepDT.AddDays(-h1ShiftDays);
                             }
                                startPrepTimeReached = effectiveNow >= startPrepDT;
                            }
                            if (startPrepTimeReached && groupSchedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                                displayStatus = "Preparing";
                            else 
                                displayStatus = (item.Status ?? item.DriverStatus) ?? "Scheduled";
                        }

                        if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                            && !isCompleted
                            && !groupSchedules.Any(s => (s.ActualStartTime.HasValue && s.ActualStartTime.Value <= effectiveNow) || (s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= effectiveNow))
                            && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                        {
                            displayStatus = "Delayed";
                        }

                        var isDelayed = displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase);
                        var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);
                        
                        if (displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase) && 
                           (isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15))))
                        {
                            isPrepared = false;
                        }

                        var isDelayPickup = false;
                        var hasActualPickupOnDay = item.ActualPickupTime.HasValue && item.ActualPickupTime.Value <= effectiveNow;
                        if (!hasActualPickupOnDay && pickupTimeTarget.HasValue && !isCompleted) {
                            if (effectiveNow >= pickupTimeTarget.Value) isDelayPickup = true;
                        }
                        if (isCarryOverSched && !isCompleted && !hasActualPickupOnDay) {
                            isDelayPickup = true;
                        }

                        var isDelayDelivery = false;
                        var hasActualEndTimeOnDay = item.ActualEndTime.HasValue && item.ActualEndTime.Value <= effectiveNow;
                        if (!hasActualEndTimeOnDay && item.ETD.HasValue && !isCompleted) {
                            var etdTarget = item.ETD.Value;
                            if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                            if (effectiveNow >= etdTarget) isDelayDelivery = true;
                        }

                        var isDelayPrepare = false;
                        if (!isCompleted)
                        {
                            if (!isPrepared)
                            {
                                bool overdueEndPrep = stdPMinutes > 0 && effectiveNow >= planEndTime;
                                bool overdueDockIn  = dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value;
                                if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                            }
                            else
                            {
                                var groupMaxReadyTime = isKanbanFullyPrepared && groupSchedules.Any(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= effectiveNow) ? groupSchedules.Where(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= effectiveNow).Max(s => (DateTime?)s.ReadyToDockTime) : (DateTime?)null;
                                if (groupMaxReadyTime.HasValue)
                                {
                                    var readyTime = groupMaxReadyTime.Value;
                                    DateTime effectiveTarget;
                                    if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                                        effectiveTarget = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                                    else if (dockInTimeTarget.HasValue)
                                        effectiveTarget = dockInTimeTarget.Value;
                                    else
                                        effectiveTarget = planEndTime;
                                        
                                    if (readyTime > effectiveTarget) isDelayPrepare = true;
                                }
                                else
                                {
                                    bool overdueEndPrep = stdPMinutes > 0 && effectiveNow >= planEndTime;
                                    bool overdueDockIn  = dockInTimeTarget.HasValue && effectiveNow >= dockInTimeTarget.Value;
                                    if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                                }
                            }
                        }
                        if (isCarryOverSched && !isCompleted && !isKanbanFullyPrepared)
                        {
                            isDelayPrepare = true;
                        }

                        var isPickupLate = false;
                        var planBaseDate = item.CreatedDate;
                        if (hasActualPickupOnDay && pickupTimeTarget.HasValue) {
                            var actualP = item.ActualPickupTime.Value;
                            if (planBaseDate > actualP && planBaseDate > pickupTimeTarget.Value) actualP = planBaseDate;
                            if ((actualP - pickupTimeTarget.Value).TotalMinutes > 0) isPickupLate = true;
                        }

                        var isDeliveryLate = false;
                        if (hasActualEndTimeOnDay && item.ETD.HasValue) {
                            var actualE = item.ActualEndTime.Value;
                            if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                            var etdForPunctuality = item.ETD.Value;
                            if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                            if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                        }

                        if (forceOnTimeMode)
                        {
                            isDelayPrepare = false;
                            isDelayPickup = false;
                            isDelayDelivery = false;
                            isPickupLate = false;
                            isDeliveryLate = false;
                        }

                        if (item.ScheduledDate.Date <= d.Date)
                        {
                            if (isDelayPrepare && !isCompleted) monthPrepDelays++;
                            
                            bool hasPickupBadge = false;
                            if (hasActualPickupOnDay)
                            {
                                hasPickupBadge = isPickupLate;
                            }
                            else if (!isCompleted)
                            {
                                hasPickupBadge = isDelayPickup;
                            }
                            if (hasPickupBadge) monthTruckInDelays++;

                            bool hasDeliveryBadge = false;
                            if (hasActualEndTimeOnDay)
                            {
                                hasDeliveryBadge = isDeliveryLate;
                            }
                            else if (!isCompleted)
                            {
                                hasDeliveryBadge = isDelayDelivery ||
                                    (isCarryOverSched && !isCompleted) ||
                                    (isTodayH1 && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value) || 
                                    (isOvernightPrep && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && effectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                            }
                            if (hasDeliveryBadge) monthDeliveryDelays++;
                        }
                    }
                }

                monthlyPrepare[m - 1] = monthPrepDelays;
                monthlyTruckIn[m - 1] = monthTruckInDelays;
                monthlyDelivery[m - 1] = monthDeliveryDelays;
            }

            // 3. Data Vendor / Customer Performance untuk Shift 1 dan Shift 2 pada targetDate
            var activeSchedulesForTarget = schedules
                .Where(s => s.ScheduledDate.Date == targetDate)
                .ToList();

            var targetGroups = activeSchedulesForTarget
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                });

            var targetDateEffectiveNow = targetDate < DateTime.Today ? targetDate.AddDays(1).AddSeconds(-1) : DateTime.Now;

            var vendorTripsShift1 = new Dictionary<string, (int Total, int Delayed)>();
            var vendorTripsShift2 = new Dictionary<string, (int Total, int Delayed)>();

            foreach (var g in targetGroups)
            {
                var groupSchedules = g.ToList();
                var item = groupSchedules.First();
                var customerName = item.Customer?.CustomerName ?? "(Unknown)";

                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                var today = targetDate.Date;
                var daysAhead = (item.ScheduledDate.Date - today).Days;
                var isFutureSchedule = daysAhead > 0;
                var isH1Shift = isFutureSchedule &&
                                (daysAhead == 1 ||
                                 (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                 (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today && item.PickupTime.HasValue && pickupHour < 12;
                
                var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                var isPastCompletedGroup = item.ScheduledDate.Date < today && groupSchedules.All(s => 
                    (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= targetDateEffectiveNow) || 
                    string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                    (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity))
                );

                if (isPastCompletedGroup) continue;
                if (isFutureSchedule && !isH1Shift) continue;

                var dockInTimeTarget = item.EnterDockTime;
                var pickupTimeTarget = item.PickupTime;
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    planEndTime = planEndTime.AddDays(-h1ShiftDays);
                }

                var isCarryOverSched = item.ScheduledDate.Date < targetDate;
                var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                
                if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                    var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                    if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                }
                if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                    pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                }

                if (!isCarryOverSched) {
                    var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                    if (isH1Shift || isTodayH1 || isOvernightPrep) {
                        var h1ShiftDays = isH1Shift ? daysAhead : 1;
                        startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                    }
                    if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                    if (pickupTimeTarget.HasValue && !isTodayH1) {
                        var anchor = dockInTimeTarget ?? planEndTime;
                        if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                    }
                }

                var isCompleted = groupSchedules.All(s => 
                    (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= targetDateEffectiveNow) || 
                    string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                    string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase)
                );

                double totalKanbanTarget = 0;
                double totalKanbanActual = 0;
                foreach(var s in groupSchedules)
                {
                    if (s.DeliveryItems != null)
                    {
                        foreach(var di in s.DeliveryItems)
                        {
                            var qpc = di.Item?.QtyLot ?? 1;
                            if (qpc > 0)
                            {
                                var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                totalKanbanTarget += diKbnTarget;
                                totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                    ? diKbnTarget
                                    : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                            }
                        }
                    }
                }
                bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                string displayStatus = "Scheduled";
                if (groupSchedules.All(s => s.ActualEndTime.HasValue && s.ActualEndTime.Value <= targetDateEffectiveNow))
                    displayStatus = "Completed";
                else if (groupSchedules.Any(s => s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= targetDateEffectiveNow))
                    displayStatus = "In Progress";
                else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                    displayStatus = "Prepared";
                else if (groupSchedules.Any(s => s.ActualStartTime.HasValue && s.ActualStartTime.Value <= targetDateEffectiveNow))
                    displayStatus = "In Progress";
                else
                {
                    bool startPrepTimeReached = true;
                    if (startPMinutes > 0)
                    {
                        DateTime startPrepDT = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                        if (isH1Shift || isTodayH1 || isOvernightPrep) {
                            var h1ShiftDays = isH1Shift ? daysAhead : 1;
                            startPrepDT = startPrepDT.AddDays(-h1ShiftDays);
                        }
                        startPrepTimeReached = targetDateEffectiveNow >= startPrepDT;
                    }
                    if (startPrepTimeReached && groupSchedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                        displayStatus = "Preparing";
                    else 
                        displayStatus = (item.Status ?? item.DriverStatus) ?? "Scheduled";
                }

                if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                    && !isCompleted
                    && !groupSchedules.Any(s => (s.ActualStartTime.HasValue && s.ActualStartTime.Value <= targetDateEffectiveNow) || (s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= targetDateEffectiveNow))
                    && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    displayStatus = "Delayed";

                var isDelayed = displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase);
                var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);
                
                if (displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase) && 
                   (isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15))))
                {
                    isPrepared = false;
                }

                var isDelayPickup = false;
                var hasActualPickupOnDay = item.ActualPickupTime.HasValue && item.ActualPickupTime.Value <= targetDateEffectiveNow;
                if (!hasActualPickupOnDay && pickupTimeTarget.HasValue && !isCompleted) {
                    if (targetDateEffectiveNow >= pickupTimeTarget.Value) isDelayPickup = true;
                }
                if (isCarryOverSched && !isCompleted && !hasActualPickupOnDay) {
                    isDelayPickup = true;
                }

                var isDelayDelivery = false;
                var hasActualEndTimeOnDay = item.ActualEndTime.HasValue && item.ActualEndTime.Value <= targetDateEffectiveNow;
                if (!hasActualEndTimeOnDay && item.ETD.HasValue && !isCompleted) {
                    var etdTarget = item.ETD.Value;
                    if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                    if (targetDateEffectiveNow >= etdTarget) isDelayDelivery = true;
                }

                var isDelayPrepare = false;
                if (!isCompleted)
                {
                    if (!isPrepared)
                    {
                        bool overdueEndPrep = stdPMinutes > 0 && targetDateEffectiveNow >= planEndTime;
                        bool overdueDockIn  = dockInTimeTarget.HasValue && targetDateEffectiveNow >= dockInTimeTarget.Value;
                        if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                    }
                    else
                    {
                        var groupMaxReadyTime = isKanbanFullyPrepared && groupSchedules.Any(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= targetDateEffectiveNow) 
                            ? groupSchedules.Where(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= targetDateEffectiveNow).Max(s => (DateTime?)s.ReadyToDockTime) 
                            : (DateTime?)null;
                        if (groupMaxReadyTime.HasValue)
                        {
                            var readyTime = groupMaxReadyTime.Value;
                            DateTime effectiveTarget;
                            if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                                    effectiveTarget = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                            else if (dockInTimeTarget.HasValue)
                                    effectiveTarget = dockInTimeTarget.Value;
                            else
                                    effectiveTarget = planEndTime;
                                    
                            if (readyTime > effectiveTarget) isDelayPrepare = true;
                        }
                        else
                        {
                            bool overdueEndPrep = stdPMinutes > 0 && targetDateEffectiveNow >= planEndTime;
                            bool overdueDockIn  = dockInTimeTarget.HasValue && targetDateEffectiveNow >= dockInTimeTarget.Value;
                            if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                        }
                    }
                }
                if (isCarryOverSched && !isCompleted && !isKanbanFullyPrepared)
                {
                    isDelayPrepare = true;
                }

                var isPickupLate = false;
                var planBaseDate = item.CreatedDate;
                if (hasActualPickupOnDay && pickupTimeTarget.HasValue) {
                    var actualP = item.ActualPickupTime.Value;
                    if (planBaseDate > actualP && planBaseDate > pickupTimeTarget.Value) actualP = planBaseDate;
                    if ((actualP - pickupTimeTarget.Value).TotalMinutes > 0) isPickupLate = true;
                }

                var isDeliveryLate = false;
                if (hasActualEndTimeOnDay && item.ETD.HasValue) {
                    var actualE = item.ActualEndTime.Value;
                    if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                    var etdForPunctuality = item.ETD.Value;
                    if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                    if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                }

                bool hasAnyDelay = false;
                if (item.ScheduledDate.Date <= targetDate.Date)
                {
                    bool hasPickupBadge = false;
                    if (hasActualPickupOnDay)
                    {
                        hasPickupBadge = isPickupLate;
                    }
                    else if (!isCompleted)
                    {
                        hasPickupBadge = isDelayPickup;
                    }

                    bool hasDeliveryBadge = false;
                    if (hasActualEndTimeOnDay)
                    {
                        hasDeliveryBadge = isDeliveryLate;
                    }
                    else if (!isCompleted)
                    {
                        hasDeliveryBadge = isDelayDelivery ||
                            (isCarryOverSched && !isCompleted) ||
                            (isTodayH1 && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value) ||
                            (isOvernightPrep && !isCompleted && !hasActualEndTimeOnDay && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                    }

                    hasAnyDelay = (isDelayPrepare && !isCompleted) || hasPickupBadge || hasDeliveryBadge;
                }

                var shift = GetShift(item);
                var targetDict = shift == 1 ? vendorTripsShift1 : vendorTripsShift2;

                if (!targetDict.ContainsKey(customerName))
                {
                    targetDict[customerName] = (0, 0);
                }

                var currentVal = targetDict[customerName];
                targetDict[customerName] = (currentVal.Total + 1, currentVal.Delayed + (hasAnyDelay ? 1 : 0));
            }

            var shift1VendorsList = vendorTripsShift1.Select(kv => new
            {
                vendor = kv.Key,
                pctDelay = kv.Value.Total > 0 ? (int)Math.Round((double)kv.Value.Delayed * 100 / kv.Value.Total) : 0
            })
            .OrderByDescending(v => v.pctDelay)
            .ThenBy(v => v.vendor)
            .ToList();

            var shift2VendorsList = vendorTripsShift2.Select(kv => new
            {
                vendor = kv.Key,
                pctDelay = kv.Value.Total > 0 ? (int)Math.Round((double)kv.Value.Delayed * 100 / kv.Value.Total) : 0
            })
            .OrderByDescending(v => v.pctDelay)
            .ThenBy(v => v.vendor)
            .ToList();

            // Calculate active (uncompleted) indicators for targetDate
            var tomorrowDate = targetDate.AddDays(1);
            var activeSchedulesForIndicators = schedules
                .Where(s => s.ScheduledDate.Date == targetDate ||
                            (s.ScheduledDate.Date < targetDate && s.Status != "Completed" && (!s.ActualEndTime.HasValue || s.ActualEndTime.Value.Date > targetDate)) ||
                            (((s.ScheduledDate.Date == tomorrowDate) ||
                              (targetDate.DayOfWeek == DayOfWeek.Friday && (s.ScheduledDate.Date == targetDate.AddDays(2) || s.ScheduledDate.Date == targetDate.AddDays(3))) ||
                              (targetDate.DayOfWeek == DayOfWeek.Saturday && s.ScheduledDate.Date == targetDate.AddDays(2))) &&
                             s.Status != "Completed" && (!s.ActualEndTime.HasValue || s.ActualEndTime.Value.Date > targetDate)))
                .ToList();

            var indicatorGroups = activeSchedulesForIndicators
                .GroupBy(s => new {
                    CustId = s.CustomerId,
                    Area  = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper(),
                    Cycle = (s.Cycle ?? "").Trim().ToUpper(),
                    Route = (s.Route ?? "").Trim().ToUpper(),
                    Date  = s.ScheduledDate.Date
                });

            int activePrepareCount = 0;
            int activePickupCount = 0;
            int activeDeliveryCount = 0;

            foreach (var g in indicatorGroups)
            {
                var groupSchedules = g.ToList();
                var item = groupSchedules.First();

                var startPMinutes = item.StartPrepareTime > 0 ? item.StartPrepareTime : (item.Customer?.StartPrepareTime ?? 0);
                var stdPMinutes = item.StdPrepareTime > 0 ? item.StdPrepareTime : (item.Customer?.StdPrepareTime ?? 0);

                var pickupHour = item.PickupTime.HasValue ? item.PickupTime.Value.Hour : 12;
                var today = targetDate.Date;
                var daysAhead = (item.ScheduledDate.Date - today).Days;
                var isFutureSchedule = daysAhead > 0;
                var isH1Shift = isFutureSchedule &&
                                (daysAhead == 1 ||
                                 (today.DayOfWeek == DayOfWeek.Friday && (daysAhead == 2 || daysAhead == 3)) ||
                                 (today.DayOfWeek == DayOfWeek.Saturday && daysAhead == 2));
                var isTodayH1 = !isFutureSchedule && item.ScheduledDate.Date == today && item.PickupTime.HasValue && pickupHour < 12;
                
                var dockInForOvernightCheck = item.EnterDockTime ?? item.ActualEnterDockTime;
                var dockInTimeRaw = dockInForOvernightCheck.HasValue ? dockInForOvernightCheck.Value.TimeOfDay.TotalMinutes : -1.0;
                var isOvernightPrep = !isH1Shift && !isTodayH1 && !isFutureSchedule && item.ScheduledDate.Date == today && dockInTimeRaw >= 0 && startPMinutes > 0 && dockInTimeRaw < startPMinutes;

                var isPastCompletedGroup = item.ScheduledDate.Date < today && groupSchedules.All(s => s.ActualEndTime.HasValue || string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || (s.DeliveryItems != null && s.DeliveryItems.Any() && s.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity)));

                if (isPastCompletedGroup) continue;
                if (isFutureSchedule && !isH1Shift) continue;

                var isCompleted = groupSchedules.All(s => 
                    (s.ActualEndTime.HasValue && s.ActualEndTime.Value <= targetDateEffectiveNow) || 
                    string.Equals((s.Status ?? "").Trim(), "Completed", StringComparison.OrdinalIgnoreCase) || 
                    string.Equals((s.Status ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase)
                );

                if (isCompleted) continue; // INDICATORS DO NOT COUNT COMPLETED DELAYS!

                var dockInTimeTarget = item.EnterDockTime;
                var pickupTimeTarget = item.PickupTime;
                var planEndTime = item.ScheduledDate.Date.AddMinutes(stdPMinutes);

                if (startPMinutes > 0 && stdPMinutes < startPMinutes) { planEndTime = planEndTime.AddDays(1); }
                if (isH1Shift || isTodayH1 || isOvernightPrep) {
                    var h1ShiftDays = isH1Shift ? daysAhead : 1;
                    planEndTime = planEndTime.AddDays(-h1ShiftDays);
                }

                var isCarryOverSched = item.ScheduledDate.Date < targetDate;
                var overBumpCap = item.ScheduledDate.Date.AddDays(1);
                while (dockInTimeTarget.HasValue && dockInTimeTarget.Value.Date > overBumpCap) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                while (pickupTimeTarget.HasValue && pickupTimeTarget.Value.Date > overBumpCap) pickupTimeTarget = pickupTimeTarget.Value.AddDays(-1);
                
                if (isTodayH1 && dockInTimeTarget.HasValue && item.PickupTime.HasValue) {
                    var pickupToday = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                    if (dockInTimeTarget.Value > pickupToday) dockInTimeTarget = dockInTimeTarget.Value.AddDays(-1);
                }
                if (isTodayH1 && pickupTimeTarget.HasValue && item.PickupTime.HasValue) {
                    pickupTimeTarget = item.ScheduledDate.Date.Add(item.PickupTime.Value.TimeOfDay);
                }

                if (!isCarryOverSched) {
                    var startPrepDT2 = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                    if (isH1Shift || isTodayH1 || isOvernightPrep) {
                        var h1ShiftDays = isH1Shift ? daysAhead : 1;
                        startPrepDT2 = startPrepDT2.AddDays(-h1ShiftDays);
                    }
                    if (startPMinutes > 0 && dockInTimeTarget.HasValue && dockInTimeTarget.Value < startPrepDT2) dockInTimeTarget = dockInTimeTarget.Value.AddDays(1);
                    if (pickupTimeTarget.HasValue && !isTodayH1) {
                        var anchor = dockInTimeTarget ?? planEndTime;
                        if (pickupTimeTarget.Value < anchor) pickupTimeTarget = pickupTimeTarget.Value.AddDays(1);
                    }
                }

                double totalKanbanTarget = 0;
                double totalKanbanActual = 0;
                foreach(var s in groupSchedules)
                {
                    if (s.DeliveryItems != null)
                    {
                        foreach(var di in s.DeliveryItems)
                        {
                            var qpc = di.Item?.QtyLot ?? 1;
                            if (qpc > 0)
                            {
                                var diKbnTarget = Math.Ceiling((double)di.Quantity / qpc);
                                totalKanbanTarget += diKbnTarget;
                                totalKanbanActual += (di.ActualQuantity ?? 0) >= di.Quantity
                                    ? diKbnTarget
                                    : Math.Ceiling((double)(di.ActualQuantity ?? 0) / qpc);
                            }
                        }
                    }
                }
                bool isKanbanFullyPrepared = totalKanbanTarget > 0 && totalKanbanActual >= totalKanbanTarget;

                string displayStatus = "Scheduled";
                if (groupSchedules.All(s => s.ActualEndTime.HasValue && s.ActualEndTime.Value <= targetDateEffectiveNow))
                    displayStatus = "Completed";
                else if (groupSchedules.Any(s => s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= targetDateEffectiveNow))
                    displayStatus = "In Progress";
                else if (isKanbanFullyPrepared && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                    displayStatus = "Prepared";
                else if (groupSchedules.Any(s => s.ActualStartTime.HasValue && s.ActualStartTime.Value <= targetDateEffectiveNow))
                    displayStatus = "In Progress";
                else
                {
                    bool startPrepTimeReached = true;
                    if (startPMinutes > 0)
                    {
                        DateTime startPrepDT = item.ScheduledDate.Date.AddMinutes(startPMinutes);
                        if (isH1Shift || isTodayH1 || isOvernightPrep) {
                            var h1ShiftDays = isH1Shift ? daysAhead : 1;
                            startPrepDT = startPrepDT.AddDays(-h1ShiftDays);
                        }
                        startPrepTimeReached = targetDateEffectiveNow >= startPrepDT;
                    }
                    if (startPrepTimeReached && groupSchedules.Any(s => s.PreparationStatus == "In Progress" || s.PreparationStatus == "Prepared") && !string.Equals(item.Status, "Delayed", StringComparison.OrdinalIgnoreCase))
                        displayStatus = "Preparing";
                    else 
                        displayStatus = (item.Status ?? item.DriverStatus) ?? "Scheduled";
                }

                if ((isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15)))
                    && !isCompleted
                    && !groupSchedules.Any(s => (s.ActualStartTime.HasValue && s.ActualStartTime.Value <= targetDateEffectiveNow) || (s.ActualEnterDockTime.HasValue && s.ActualEnterDockTime.Value <= targetDateEffectiveNow))
                    && !displayStatus.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                    displayStatus = "Delayed";

                var isPrepared = isKanbanFullyPrepared || displayStatus.Equals("Prepared", StringComparison.OrdinalIgnoreCase);
                
                if (displayStatus.Equals("Delayed", StringComparison.OrdinalIgnoreCase) && 
                   (isCarryOverSched || (isOvernightPrep && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15))))
                {
                    isPrepared = false;
                }

                var isDelayPickup = false;
                var hasActualPickupOnDay = item.ActualPickupTime.HasValue && item.ActualPickupTime.Value <= targetDateEffectiveNow;
                if (!hasActualPickupOnDay && pickupTimeTarget.HasValue) {
                    if (targetDateEffectiveNow >= pickupTimeTarget.Value) isDelayPickup = true;
                }
                if (isCarryOverSched && !hasActualPickupOnDay) {
                    isDelayPickup = true;
                }

                var isDelayDelivery = false;
                var hasActualEndTimeOnDay = item.ActualEndTime.HasValue && item.ActualEndTime.Value <= targetDateEffectiveNow;
                if (!hasActualEndTimeOnDay && item.ETD.HasValue) {
                    var etdTarget = item.ETD.Value;
                    if (isTodayH1) while (etdTarget.Date > item.ScheduledDate.Date) etdTarget = etdTarget.AddDays(-1);
                    if (targetDateEffectiveNow >= etdTarget) isDelayDelivery = true;
                }

                var isDelayPrepare = false;
                if (!isPrepared)
                {
                    bool overdueEndPrep = stdPMinutes > 0 && targetDateEffectiveNow >= planEndTime;
                    bool overdueDockIn  = dockInTimeTarget.HasValue && targetDateEffectiveNow >= dockInTimeTarget.Value;
                    if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                }
                else
                {
                    var groupMaxReadyTime = isKanbanFullyPrepared && groupSchedules.Any(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= targetDateEffectiveNow) 
                        ? groupSchedules.Where(s => s.ReadyToDockTime.HasValue && s.ReadyToDockTime.Value <= targetDateEffectiveNow).Max(s => (DateTime?)s.ReadyToDockTime) 
                        : (DateTime?)null;
                    if (groupMaxReadyTime.HasValue)
                    {
                        var readyTime = groupMaxReadyTime.Value;
                        DateTime effectiveTarget;
                        if (stdPMinutes > 0 && dockInTimeTarget.HasValue)
                                effectiveTarget = dockInTimeTarget.Value < planEndTime ? dockInTimeTarget.Value : planEndTime;
                        else if (dockInTimeTarget.HasValue)
                                effectiveTarget = dockInTimeTarget.Value;
                        else
                                effectiveTarget = planEndTime;
                                
                        if (readyTime > effectiveTarget) isDelayPrepare = true;
                    }
                    else
                    {
                        bool overdueEndPrep = stdPMinutes > 0 && targetDateEffectiveNow >= planEndTime;
                        bool overdueDockIn  = dockInTimeTarget.HasValue && targetDateEffectiveNow >= dockInTimeTarget.Value;
                        if (overdueEndPrep || overdueDockIn) isDelayPrepare = true;
                    }
                }
                if (isCarryOverSched && !isKanbanFullyPrepared)
                {
                    isDelayPrepare = true;
                }

                var isPickupLate = false;
                var planBaseDate = item.CreatedDate;
                if (hasActualPickupOnDay && pickupTimeTarget.HasValue) {
                    var actualP = item.ActualPickupTime.Value;
                    if (planBaseDate > actualP && planBaseDate > pickupTimeTarget.Value) actualP = planBaseDate;
                    if ((actualP - pickupTimeTarget.Value).TotalMinutes > 0) isPickupLate = true;
                }

                var isDeliveryLate = false;
                if (hasActualEndTimeOnDay && item.ETD.HasValue) {
                    var actualE = item.ActualEndTime.Value;
                    if (planBaseDate > actualE && planBaseDate > item.ETD.Value) actualE = planBaseDate;
                    var etdForPunctuality = item.ETD.Value;
                    if (isTodayH1) while (etdForPunctuality.Date > item.ScheduledDate.Date) etdForPunctuality = etdForPunctuality.AddDays(-1);
                    if ((actualE - etdForPunctuality).TotalMinutes > 0) isDeliveryLate = true;
                }

                if (isDelayPrepare) activePrepareCount++;

                bool hasPickupBadge = false;
                if (hasActualPickupOnDay)
                {
                    hasPickupBadge = isPickupLate;
                }
                else
                {
                    hasPickupBadge = isDelayPickup;
                }
                if (hasPickupBadge) activePickupCount++;

                bool hasDeliveryBadge = false;
                if (hasActualEndTimeOnDay)
                {
                    hasDeliveryBadge = isDeliveryLate;
                }
                else
                {
                    hasDeliveryBadge = isDelayDelivery ||
                        isCarryOverSched ||
                        (isTodayH1 && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value) ||
                        (isOvernightPrep && pickupTimeTarget.HasValue && targetDateEffectiveNow >= pickupTimeTarget.Value.AddMinutes(15));
                }
                if (hasDeliveryBadge) activeDeliveryCount++;
            }

            return Json(new
            {
                daily = new
                {
                    labels = dailyLabels,
                    prepare = dailyPrepare,
                    truckIn = dailyTruckIn,
                    delivery = dailyDelivery
                },
                monthly = new
                {
                    prepare = monthlyPrepare,
                    truckIn = monthlyTruckIn,
                    delivery = monthlyDelivery
                },
                vendors = new
                {
                    shift1 = shift1VendorsList,
                    shift2 = shift2VendorsList
                },
                indicators = new
                {
                    prepare = activePrepareCount,
                    truckIn = activePickupCount,
                    delivery = activeDeliveryCount,
                    total = indicatorGroups.Count()
                },
                totalSchedules = targetGroups.Count()
            });
        }

        private async Task<bool> IsAutoEnterDockActiveAsync()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "AutoEnterDockMode");
            return setting != null && setting.Value == "1";
        }

        private async Task<bool> IsSkipLeaderVerificationActiveAsync()
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == "SkipLeaderVerificationMode");
            return setting != null && setting.Value == "1";
        }

        private async Task RecalculateScheduleTotals(int scheduleId)
        {
            bool autoEnterDockActive = await IsAutoEnterDockActiveAsync();
            bool skipLeaderVerifActive = await IsSkipLeaderVerificationActiveAsync();
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule != null)
            {
                schedule.TotalTargetQuantity = schedule.DeliveryItems.Sum(di => di.Quantity);
                schedule.TotalActualQuantity = schedule.DeliveryItems.Sum(di => di.ActualQuantity ?? 0);

                bool allItemsDone = schedule.DeliveryItems.Any() &&
                                    schedule.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity);

                if (allItemsDone)
                {
                    var now = DateTime.Now;
                    bool anyDockInSet = false;
                    var groupSchedules = await _context.DeliverySchedules
                        .Include(s => s.DeliveryItems)
                        .Where(s => s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                                    s.Cycle == schedule.Cycle &&
                                    s.Route == schedule.Route &&
                                    s.Area == schedule.Area &&
                                    s.Status != "Cancelled")
                        .ToListAsync();

                    bool isGroupReady = groupSchedules.All(gs =>
                        !gs.DeliveryItems.Any() ||
                        gs.DeliveryItems.All(di => (di.ActualQuantity ?? 0) >= di.Quantity));

                    if (isGroupReady)
                    {
                        foreach (var gs in groupSchedules)
                        {
                            if (gs.PreparationStatus != "Prepared")
                            {
                                gs.PreparationStatus = "Prepared";
                                gs.ReadyToDockTime ??= now;
                            }

                            if (gs.ActualEndTime.HasValue)
                            {
                                gs.Status = "Completed";
                                gs.DriverStatus = "Completed";
                            }
                            else if (gs.Status != "In Progress")
                            {
                                gs.Status = "In Progress";
                            }

                            if (autoEnterDockActive && skipLeaderVerifActive && !gs.ActualEnterDockTime.HasValue)
                            {
                                gs.ActualEnterDockTime = now;
                                gs.DriverStatus = "In Progress";
                                anyDockInSet = true;
                            }
                        }

                        await _context.SaveChangesAsync();

                        await _hubContext.Clients.All.SendAsync("deliveryUpdated", new {
                            action = anyDockInSet ? "enterDock" : "readyToDock",
                            scheduleNumber = schedule.ScheduleNumber
                        });
                        await _stockHubContext.Clients.All.SendAsync("updateStock");
                        return;
                    }
                    else if (schedule.PreparationStatus != "Prepared")
                    {
                        schedule.PreparationStatus = "Prepared";
                        schedule.ReadyToDockTime ??= now;
                    }
                }
                else
                {
                    if (schedule.PreparationStatus == "Prepared" && !schedule.ActualEnterDockTime.HasValue)
                    {
                        schedule.PreparationStatus = "In Progress";
                        schedule.ReadyToDockTime = null;
                    }
                    if (schedule.ActualEndTime.HasValue)
                    {
                        schedule.ActualEndTime = null;
                        schedule.DriverStatus = schedule.ActualEnterDockTime.HasValue ? "In Progress" : null;
                    }
                }

                await _context.SaveChangesAsync();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AuthorizeRoles("Super Admin", "Admin")]
        public async Task<IActionResult> ConfirmAllKanban(int id)
        {
            var schedule = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == id);

            if (schedule == null)
            {
                return NotFound();
            }

            var candidates = await _context.DeliverySchedules
                .Include(s => s.Customer)
                .Include(s => s.DeliveryItems)
                    .ThenInclude(di => di.Item)
                .Where(s => s.CustomerId == schedule.CustomerId && 
                           s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                           s.ScheduleId != schedule.ScheduleId)
                .ToListAsync();

            var scheduleArea = (string.IsNullOrWhiteSpace(schedule.Area) ? (schedule.Customer?.Docking ?? "") : schedule.Area).Trim().ToUpper();
            var scheduleCycle = (schedule.Cycle ?? "").Trim().ToUpper();
            var scheduleRoute = (string.IsNullOrWhiteSpace(schedule.Route) ? "" : schedule.Route).Trim().ToUpper();
            var today = DateTime.Today;

            var groupSchedules = candidates
                .Where(s => {
                    var sArea = (string.IsNullOrWhiteSpace(s.Area) ? (s.Customer?.Docking ?? "") : s.Area).Trim().ToUpper();
                    var sCycle = (s.Cycle ?? "").Trim().ToUpper();
                    var sRoute = (string.IsNullOrWhiteSpace(s.Route) ? "" : s.Route).Trim().ToUpper();
                    if (sArea != scheduleArea || sCycle != scheduleCycle || sRoute != scheduleRoute)
                        return false;

                    if (s.Status == "Cancelled")
                        return false;

                    return true;
                })
                .ToList();

            var allSchedulesToProcess = new List<DeliverySchedule> { schedule };
            allSchedulesToProcess.AddRange(groupSchedules);

            foreach (var s in allSchedulesToProcess)
            {
                foreach (var deliveryItem in s.DeliveryItems)
                {
                    var qtyLot = (deliveryItem.Item?.QtyLot > 0) ? deliveryItem.Item.QtyLot.Value : 1;
                    var targetKanban = (int)Math.Ceiling((double)deliveryItem.Quantity / qtyLot);

                    var tagCandidates = Helpers.VinHelper.GetItemTags(deliveryItem.Item?.VIN, deliveryItem.Item?.ItemCode, deliveryItem.ExternalPartNo ?? deliveryItem.Item?.CustomerPartNumber);

                    List<PreparationRecord>? existingRecords = null;
                    if (tagCandidates.Any())
                    {
                        var parentCustomerId = s.CustomerId;
                        var parentCycle = s.Cycle?.Trim();

                        existingRecords = await _context.PreparationRecords
                            .Where(p => (p.ScheduleId == deliveryItem.ScheduleId ||
                                         (p.ScheduleId == null && 
                                          !string.IsNullOrEmpty(p.ManifestNumber) && 
                                          p.ManifestNumber == s.ScheduleNumber &&
                                          (!p.DockCustomerId.HasValue || p.DockCustomerId.Value == parentCustomerId) &&
                                          (string.IsNullOrEmpty(p.DockCycle) || p.DockCycle == "-" || 
                                           (!string.IsNullOrEmpty(parentCycle) && parentCycle != "-" && p.DockCycle.Trim() == parentCycle)))) &&
                                        tagCandidates.Contains(p.Tag) &&
                                        p.Remark == "Match")
                            .OrderByDescending(p => p.PreparationId)
                            .ToListAsync();

                        var pendingRecords = existingRecords.Where(p => p.ScheduleId == null).ToList();
                        if (pendingRecords.Any())
                        {
                            foreach (var pr in pendingRecords)
                            {
                                pr.ScheduleId = deliveryItem.ScheduleId;
                            }
                        }

                        var currentUser = HttpContext.Session.GetString("FullName") ?? HttpContext.Session.GetString("Username") ?? "ManualEdit";

                        // Update existing manual records so their operator is the one clicking "Confirm All Kanban"
                        var existingManualRecords = existingRecords.Where(r => r.Kanban == "MANUAL").ToList();
                        foreach (var pr in existingManualRecords)
                        {
                            pr.CreatedBy = currentUser;
                            _context.PreparationRecords.Update(pr);
                        }

                        if (existingRecords.Count > targetKanban)
                        {
                            var toDelete = existingRecords
                                .Take(existingRecords.Count - targetKanban)
                                .ToList();
                            _context.PreparationRecords.RemoveRange(toDelete);
                        }
                        else if (existingRecords.Count < targetKanban)
                        {
                            var normalizedTag = Helpers.VinHelper.Normalize(deliveryItem.Item?.VIN ?? tagCandidates.First());
                            int toAdd = targetKanban - existingRecords.Count;
                            for (int i = 0; i < toAdd; i++)
                            {
                                await _context.PreparationRecords.AddAsync(new PreparationRecord
                                {
                                    Tag         = normalizedTag,
                                    Label       = "MANUAL",
                                    Kanban      = "MANUAL",
                                    Remark      = "Match",
                                    ScheduleId  = deliveryItem.ScheduleId,
                                    CreatedDate = DateTime.Now,
                                    CreatedBy   = currentUser,
                                    TargetDate  = s.ScheduledDate,
                                    DockCustomerId = s.CustomerId,
                                    DockName    = s.Customer?.CustomerName,
                                    DockCycle   = s.Cycle,
                                    ManifestNumber = s.ScheduleNumber,
                                    Plant       = deliveryItem.Item?.Plant ?? string.Empty
                                });
                            }
                        }

                    }

                    deliveryItem.ActualQuantity = targetKanban * qtyLot;
                    deliveryItem.IsCompleted = true;
                    var firstRealScanTime = existingRecords?.Where(r => r.Kanban != "MANUAL").Select(r => (DateTime?)r.CreatedDate).Min();
                    deliveryItem.PrepScanTime = deliveryItem.PrepScanTime ?? firstRealScanTime ?? DateTime.Now;
                    deliveryItem.UpdatedDate = DateTime.Now;
                    _context.Update(deliveryItem);
                }

                await _context.SaveChangesAsync();
                await RecalculateScheduleTotals(s.ScheduleId);
            }

            TempData["SuccessMessage"] = "Semua kanban pada grup manifes ini berhasil dikonfirmasi secara manual!";
            return RedirectToAction(nameof(Details), new { id = id });
        }

        [HttpGet]
        public async Task<IActionResult> GetDeliveryItemsJson(int scheduleId)
        {
            var schedule = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .ThenInclude(di => di.Item)
                .FirstOrDefaultAsync(s => s.ScheduleId == scheduleId);

            if (schedule == null) return NotFound();

            var groupSchedules = await _context.DeliverySchedules
                .Include(s => s.DeliveryItems)
                .ThenInclude(di => di.Item)
                .Where(s => s.ScheduledDate.Date == schedule.ScheduledDate.Date &&
                            s.Cycle == schedule.Cycle &&
                            s.Route == schedule.Route &&
                            s.Area == schedule.Area &&
                            s.ScheduleId != scheduleId &&
                            s.Status != "Cancelled")
                .ToListAsync();

            var allSchedules = new List<DeliverySchedule> { schedule };
            allSchedules.AddRange(groupSchedules);

            var items = allSchedules.SelectMany(s => s.DeliveryItems).Select(di => new
            {
                deliveryItemId = di.DeliveryItemId,
                qtyLot = di.Item?.QtyLot ?? 1,
                quantity = di.Quantity,
                actualQuantity = di.ActualQuantity ?? 0
            }).ToList();

            return Json(items);
        }
    }
}

