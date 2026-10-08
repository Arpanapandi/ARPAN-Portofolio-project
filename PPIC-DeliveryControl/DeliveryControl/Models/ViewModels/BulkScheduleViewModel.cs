using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models.ViewModels
{
    /// <summary>
    /// ViewModel untuk bulk scheduling delivery
    /// </summary>
    public class BulkScheduleViewModel
    {
        [Required(ErrorMessage = "Tanggal jadwal wajib dipilih")]
        [Display(Name = "Tanggal Jadwal")]
        public DateTime ScheduledDate { get; set; } = DateTime.Today;

        [Display(Name = "Customer yang Dipilih")]
        public List<int> SelectedCustomerIds { get; set; } = new List<int>();

        // Mode pembuatan jadwal: otomatis (berdasarkan Cycle & hari) atau manual
        public bool IsAutoGenerate { get; set; } = true;

        // Data customer yang tersedia untuk dijadwalkan
        public List<CustomerScheduleItem> AvailableCustomers { get; set; } = new List<CustomerScheduleItem>();
    }

    /// <summary>
    /// Item customer untuk ditampilkan di tabel
    /// </summary>
    public class CustomerScheduleItem
    {
        public int CustomerId { get; set; }
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string? Route { get; set; }
        public string? Cycle { get; set; }
        public string? Docking { get; set; }
        public string? Pickup { get; set; }
        public string? ETD { get; set; }
        public string? Range { get; set; }
        public string? SKID { get; set; }
        public string? Area { get; set; }
        public bool IsSelected { get; set; }
        
        // Untuk menandai apakah customer ini sesuai dengan hari yang dipilih
        public bool IsMatchingDay { get; set; }
    }
}

