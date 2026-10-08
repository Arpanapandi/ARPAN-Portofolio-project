using System.Collections.Generic;

namespace DeliveryControl.Models
{
    public class StockDashboardViewModel
    {
        public string PlantName { get; set; } = string.Empty;
        public List<PullingRecord> RecentPulling { get; set; } = new List<PullingRecord>();
        public List<PreparationRecord> RecentPreparation { get; set; } = new List<PreparationRecord>();
        public List<string> LogUsers { get; set; } = new List<string>();
        
        public List<StockItemDetail> StockDetails { get; set; } = new List<StockItemDetail>();
        
        public int ShortageCount { get; set; }
        public int NormalCount { get; set; }
        public int OverCount { get; set; }

        public int TotalPullingToday { get; set; }
        public int TotalPreparationToday { get; set; }
        public DateTime? SearchDate { get; set; }
        public string Period { get; set; } = "Day";
        
        public List<ScanNGLog> NGPulling { get; set; } = new List<ScanNGLog>();
        public List<ScanNGLog> NGPreparation { get; set; } = new List<ScanNGLog>();
        public int TotalNGPulling { get; set; }
        public int TotalNGPreparation { get; set; }
        
        public int NetStock => StockDetails.Count; // Updated to show current actual stock

        // --- TREND CHART DATA ---
        public string TrendHistoryJson { get; set; } = "[]";
        public string FullDatesJson { get; set; } = "[]";
        public List<DailyCategoryHistory> DailyHistory { get; set; } = new List<DailyCategoryHistory>();
        public CategorySummary TodaySummary { get; set; } = new CategorySummary();
        public CategorySummary YesterdaySummary { get; set; } = new CategorySummary();

        // -- Pagination metadata ------------------------------------------------------
        // Stored here so the GetStockViewModel cache wrapper can re-assign ViewBag
        // even when the result is served from IMemoryCache (Internal is not called).
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalItemCount { get; set; }
        public int NoOrderCount { get; set; }
        public bool ShowNoOrder { get; set; } // Global Saklar flag
        public Dictionary<string, string> PaginationRouteData { get; set; } = new();
        public string CurrentStatus { get; set; } = "All";
        
        public List<string> AvailableCustomers { get; set; } = new List<string>();
        public string SelectedCustomer { get; set; } = "All";
        public List<string> AvailableCategories { get; set; } = new List<string>();
        public string SelectedCategory { get; set; } = "All";
        public List<string> AvailablePlants { get; set; } = new List<string>();
        public List<string> AvailableStatuses { get; set; } = new List<string>();
        public string SelectedItemStatus { get; set; } = "All";
    }

    public class StockItemDetail
    {
        public int No { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Rak { get; set; } = string.Empty;
        public int? NoRak { get; set; }
        public string Time { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string RackInfo { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string VIN { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string? StatusItem { get; set; }
        public int? QtyLot { get; set; }
        public int? Min { get; set; }
        public int? Rop { get; set; }
        public int? Max { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal LevelStock { get; set; }
        public string Operator { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Shortage, Normal, Over
        public int? ItemId { get; set; }
        public DateTime LastActivityDate { get; set; }
        public bool IsManualAdjust { get; set; }
        public string? AdjustNote { get; set; }

        /// <summary>Rincian stok per lokasi rak (untuk popup detail)</summary>
        public List<RackLocationDetail> RackBreakdown { get; set; } = new();
    }

    public class RackLocationDetail
    {
        public string Rack { get; set; } = string.Empty;
        public int Column { get; set; }
        public decimal Stock { get; set; }
        public string LastActivity { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
    }

    public class DailyCategoryHistory
    {
        public string DateLabel { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public int Shortage { get; set; }
        public int Normal { get; set; }
        public int Over { get; set; }
    }

    public class CategorySummary
    {
        public int Shortage { get; set; }
        public int Normal { get; set; }
        public int Over { get; set; }
    }
}
