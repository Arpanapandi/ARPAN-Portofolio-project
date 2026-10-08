using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Tabel khusus untuk mencatat setiap scan invalid (NG) di Pulling maupun Preparation.
    /// Tidak mempengaruhi stock — hanya untuk monitoring dan audit.
    /// </summary>
    [Table("ScanNGLogs")]
    public class ScanNGLog
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Module asal: "Pulling" atau "Preparation"</summary>
        [Required]
        [StringLength(20)]
        public string Module { get; set; } = string.Empty;

        /// <summary>Tag/VIN yang di-scan (barcode rak)</summary>
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        /// <summary>Label yang di-scan (barcode label produk)</summary>
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        /// <summary>Kanban yang di-scan (khusus Preparation)</summary>
        [StringLength(100)]
        public string Kanban { get; set; } = string.Empty;

        /// <summary>Alasan error / pesan invalid</summary>
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Nama user yang melakukan scan</summary>
        [StringLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>Waktu kejadian</summary>
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
