using System.ComponentModel.DataAnnotations;
using DeliveryControl.Models;

namespace DeliveryControl.Models.ViewModels
{
    public class DelayChartCustomerDetail
    {
        public string CustomerName { get; set; } = string.Empty;

        [Display(Name = "Delay (menit)")]
        public int DelayMinutes { get; set; }
    }

    public class DailyPickupDelayData
    {
        public DateTime Date { get; set; }

        [Display(Name = "Total Delay Truck-In (menit)")]
        public int TotalDelayMinutes { get; set; }

        // Aggregasi delay per customer untuk hari tersebut
        public List<DelayChartCustomerDetail> CustomerDetails { get; set; } = new List<DelayChartCustomerDetail>();
    }

    public class DelayChartHourData
    {
        public int Hour { get; set; }

        [Display(Name = "Total Delay (menit)")]
        public int TotalDelayMinutes { get; set; }

        // List nama customer (untuk tampilan sederhana)
        public List<string> Customers { get; set; } = new List<string>();

        // Detail delay per customer (untuk stacked bar)
        public List<DelayChartCustomerDetail> CustomerDetails { get; set; } = new List<DelayChartCustomerDetail>();
    }

    public class DelayChartViewModel
    {
        [Display(Name = "Tanggal")]
        public DateTime SelectedDate { get; set; } = DateTime.Today;

        // Range tanggal untuk grafik harian (trend)
        [Display(Name = "Tanggal Mulai")]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Tanggal Selesai")]
        public DateTime? EndDate { get; set; }

        public int? CustomerId { get; set; }

        // Delay terhadap STD Pickup (Actual Pickup vs PickupTime)
        public List<DelayChartHourData> PickupHourlyData { get; set; } = new List<DelayChartHourData>();

        // Delay Dock In (Actual Enter Dock vs EnterDockTime)
        public List<DelayChartHourData> DockInHourlyData { get; set; } = new List<DelayChartHourData>();

        // Total delay dalam menit untuk hari ini
        public int TotalPickupDelayMinutes { get; set; }
        public int TotalDockInDelayMinutes { get; set; }

        // ================= GRAFIK HARIAN DELAY PICKUP =================

        // Total delay pickup per hari untuk rentang tanggal
        public List<DailyPickupDelayData> DailyPickupDelays { get; set; } = new List<DailyPickupDelayData>();

        // ================= ON TIME (jumlah order) =================

        public class OnTimeChartHourData
        {
            public int Hour { get; set; }

            // Total order on time pada jam tersebut
            public int TotalCount { get; set; }

            public List<string> Customers { get; set; } = new List<string>();

            // Gunakan DelayChartCustomerDetail untuk menyimpan jumlah order per customer (DelayMinutes sebagai Count)
            public List<DelayChartCustomerDetail> CustomerDetails { get; set; } = new List<DelayChartCustomerDetail>();
        }

        // On Time terhadap STD Pickup
        public List<OnTimeChartHourData> OnTimePickupHourlyData { get; set; } = new List<OnTimeChartHourData>();

        // On Time Dock In terhadap STD Enter Dock
        public List<OnTimeChartHourData> OnTimeDockInHourlyData { get; set; } = new List<OnTimeChartHourData>();

        public int TotalOnTimePickupCount { get; set; }
        public int TotalOnTimeDockInCount { get; set; }
    }

    // ViewModel khusus untuk halaman tren harian + tabel schedule (delay pickup)
    public class DailyPickupTrendViewModel
    {
        [Display(Name = "Tanggal")]
        public DateTime SelectedDate { get; set; } = DateTime.Today;

        [Display(Name = "Tanggal Mulai")]
        public DateTime StartDate { get; set; }

        [Display(Name = "Tanggal Selesai")]
        public DateTime EndDate { get; set; }

        public int? CustomerId { get; set; }

        public List<DailyPickupDelayData> DailyPickupDelays { get; set; } = new List<DailyPickupDelayData>();

        // Daftar schedule yang mengalami delay pickup dalam rentang tanggal
        public List<DeliverySchedule> DelayedSchedules { get; set; } = new List<DeliverySchedule>();
    }
}

