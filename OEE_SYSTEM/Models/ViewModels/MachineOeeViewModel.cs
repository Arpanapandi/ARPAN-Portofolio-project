using OeeSystem.Models;

namespace OeeSystem.Models.ViewModels;

public class MachineOeeViewModel
{
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty; // Nama shift dari database
    public int? ShiftId { get; set; } // ID shift dari database
    public DateTime ShiftDate { get; set; }
    public string ShiftKey { get; set; } = string.Empty;
    public DateTime ShiftStart { get; set; }
    public DateTime ShiftEnd { get; set; }
    public MachineStatus Status { get; set; }
    public double StandarCycleTime { get; set; }
    public string? ImageUrl { get; set; }

    // OEE Metrics
    public double Oee { get; set; }
    public double Availability { get; set; }
    public double Performance { get; set; }
    public double Quality { get; set; }

    // Time metrics
    public TimeSpan PlannedProductionTime { get; set; }
    public TimeSpan OperatingTime { get; set; }
    public TimeSpan DowntimeTotal { get; set; }
    public TimeSpan RestBreakTime { get; set; } // ✅ Rest Break Time (terpisah untuk display, tetap termasuk Planned Downtime)
    public TimeSpan NoLoadingTime { get; set; } // ✅ NO LOADING time (tidak masuk ke OEE)
    public TimeSpan NettOperatingTime { get; set; } // ✅ Nett Operating Time = Cycle Time × Total Produced

    // Production metrics
    public int TotalCount { get; set; }
    public int GoodCount { get; set; }
    public int RejectCount { get; set; }

    // Current Job Info
    public JobRunViewModel? ActiveJob { get; set; }

    // History
    public List<DowntimeEventViewModel> RecentDowntimes { get; set; } = new();
    public List<ProductionCountViewModel> RecentProductionCounts { get; set; } = new();

    // Chart Data
    public ChartDataViewModel ChartData { get; set; } = new();
    
    // Action Buttons Data
    public List<DowntimeReason> LineStopReasons { get; set; } = new();
    public DowntimeReason? RestReason { get; set; }
    public List<NgType> NgTypes { get; set; } = new();
    public bool HasActiveJob { get; set; }
    public bool HasActiveDowntime { get; set; }
    public bool HasActiveRestBreak { get; set; } // ✅ Status Rest Break aktif
    public string? ActiveDowntimeDescription { get; set; }
    public MachineStatus MachineStatus { get; set; } // Status dari Admin (Aktif/TidakAktif)
}

public class ChartDataViewModel
{
    // Donut Chart: Machine Run Time
    public double RunTimeMinutes { get; set; }
    public double IdleTimeMinutes { get; set; }
    public double OffTimeMinutes { get; set; }

    // Bar Chart: OEE Analysis
    public double OeeValue { get; set; }
    public double AvailabilityValue { get; set; }
    public double PerformanceValue { get; set; }
    public double QualityValue { get; set; }

    // Stacked Bar Chart: Weekly Trend
    public List<WeeklyTrendData> WeeklyTrend { get; set; } = new();
}

public class WeeklyTrendData
{
    public string DateLabel { get; set; } = string.Empty;
    public double RunTimeMinutes { get; set; }
    public double IdleTimeMinutes { get; set; }
    public double OffTimeMinutes { get; set; }
}

public class JobRunViewModel
{
    public int Id { get; set; }
    public string WorkOrderNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImageUrl { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public int? ManPowerId { get; set; }
    public string? ManPowerName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int TargetQuantity { get; set; }
    public int CurrentQuantity { get; set; }
    public DateTime LastStatusChangeTime { get; set; } // Durasi sejak status terakhir
    public int SinceLastChangeSeconds { get; set; } // Durasi dalam detik untuk sinkronisasi dengan Operator View
}

public class DowntimeEventViewModel
{
    public int Id { get; set; }
    public string ReasonCategory { get; set; } = string.Empty;
    public string ReasonDescription { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double DurationSeconds { get; set; }
}

public class ProductionCountViewModel
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int GoodCount { get; set; }
    public int RejectCount { get; set; }
    public string? RejectReason { get; set; }
}

