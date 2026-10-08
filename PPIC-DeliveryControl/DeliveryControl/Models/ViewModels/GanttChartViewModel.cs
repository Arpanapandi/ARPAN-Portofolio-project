using DeliveryControl.Models;

namespace DeliveryControl.Models.ViewModels
{
    /// <summary>
    /// ViewModel untuk Gantt Chart — menggabungkan Customer master dengan schedule opsional.
    /// Setiap row mewakili satu Customer (dari master), dengan daftar schedule-nya (bisa kosong).
    /// </summary>
    public class GanttChartViewModel
    {
        /// <summary>Semua active Customers yang akan ditampilkan sebagai baris di Gantt Chart.</summary>
        public List<GanttRow> Rows { get; set; } = new();

        /// <summary>Tanggal yang dipilih (HARI INI).</summary>
        public DateTime SelectedDate { get; set; } = DateTime.Today;

        /// <summary>Total jadwal yang ada (dari DeliverySchedules).</summary>
        public int TotalScheduled { get; set; }
        public int TotalCompleted { get; set; }
        public int TotalInProgress { get; set; }
        public int TotalLate { get; set; }
    }

    public class GanttRow
    {
        /// <summary>Data Customer dari master.</summary>
        public Customer Customer { get; set; } = null!;

        /// <summary>
        /// Daftar schedule untuk Customer ini pada tanggal yang dipilih.
        /// Kosong jika tidak ada jadwal yang di-upload.
        /// </summary>
        public List<DeliverySchedule> Schedules { get; set; } = new();

        /// <summary>True jika customer ini memiliki setidaknya satu schedule.</summary>
        public bool HasSchedule => Schedules.Any();

        /// <summary>
        /// Waktu pickup paling awal dari semua schedule (untuk sorting).
        /// Jika tidak ada schedule, diambil dari Customer.Pickup string.
        /// Null jika tidak ada data sama sekali atau waktu adalah 00:00.
        /// </summary>
        public DateTime? EarliestPickupTime { get; set; }

        /// <summary>Sort key: non-zero time → sort ascending; zero/null → sort last.</summary>
        public double SortKey { get; set; }

        /// <summary>Apakah data ini memiliki waktu lengkap (non-zero).</summary>
        public bool HasCompleteData { get; set; }
    }
}
