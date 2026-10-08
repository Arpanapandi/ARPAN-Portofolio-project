using System;
using System.Collections.Generic;

namespace DeliveryControl.Models
{
    public class StockTrendViewModel
    {
        public List<TrendDataPoint> OverallTrend { get; set; } = new List<TrendDataPoint>();
        public List<PlantTrendData> PlantTrends { get; set; } = new List<PlantTrendData>();
        
        public List<StockItemDetail> RecentMolded { get; set; } = new List<StockItemDetail>();
        public List<StockItemDetail> RecentHose { get; set; } = new List<StockItemDetail>();
        public List<StockItemDetail> RecentRVI { get; set; } = new List<StockItemDetail>();
        
        public int TotalFGStock { get; set; }
        public int HighestActivityTarget { get; set; }
        
        public string FilterPeriod { get; set; } = "Day"; // Day, Month, Year
        public string ViewMode { get; set; } = "Activity"; // Activity, Level

        // For Level Stock Trend Mode
        public List<CategoryTrendPoint> CategoryTrends { get; set; } = new List<CategoryTrendPoint>();
    }

    public class PlantTrendData
    {
        public string PlantName { get; set; } = string.Empty;
        public List<TrendDataPoint> DataPoints { get; set; } = new List<TrendDataPoint>();
        public int PeakActivity { get; set; }
        public int TotalStock { get; set; }
        public List<CategoryTrendPoint> CategoryTrends { get; set; } = new List<CategoryTrendPoint>();
    }

    public class TrendDataPoint
    {
        public string Label { get; set; } = string.Empty; // Hour, Date, or Month
        public int PullingCount { get; set; }
        public int PreparationCount { get; set; }
    }

    public class CategoryTrendPoint
    {
        public string Label { get; set; } = string.Empty; // Date
        public int CatLess1 { get; set; }     // < 1 D
        public int CatLess1_5 { get; set; }   // < 1.5 D
        public int CatRange1_5_2 { get; set; } // 1.5 - 2 D
        public int CatRange2_3 { get; set; }   // 2 - 3 D
        public int CatMore3 { get; set; }      // > 3 D

        // New Simplified Categories
        public int ShortageCount { get; set; }
        public int NormalCount { get; set; }
        public int OverCount { get; set; }

        // Plant-specific shortage (< 1.5 D) - Kept for compatibility if needed
        public int HoseShortage { get; set; }
        public int MoldedShortage { get; set; }
        public int RviShortage { get; set; }
    }
}
