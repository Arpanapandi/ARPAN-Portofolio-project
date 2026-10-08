using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk Activity Log - mencatat semua aktivitas di sistem
    /// </summary>
    public class ActivityLog
    {
        [Key]
        public int LogId { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Modul")]
        public string Module { get; set; } = string.Empty; // Customer, Schedule, Driver, Preparation, etc.

        [Required]
        [StringLength(50)]
        [Display(Name = "Aksi")]
        public string Action { get; set; } = string.Empty; // Create, Update, Delete, Confirm, etc.

        [StringLength(200)]
        [Display(Name = "Entity")]
        public string? EntityName { get; set; } // Nama entity yang dimodifikasi (e.g., "CUST001", "SCH-20251128001")

        public int? EntityId { get; set; } // ID entity yang dimodifikasi

        [Display(Name = "Deskripsi")]
        public string? Description { get; set; } // Deskripsi detail aktivitas

        [StringLength(500)]
        [Display(Name = "Data Lama")]
        public string? OldData { get; set; } // JSON data sebelum perubahan (optional)

        [StringLength(500)]
        [Display(Name = "Data Baru")]
        public string? NewData { get; set; } // JSON data setelah perubahan (optional)

        [Required]
        [Display(Name = "Tanggal & Waktu")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [Required]
        [StringLength(100)]
        [Display(Name = "User")]
        public string PerformedBy { get; set; } = string.Empty; // Username/email user yang melakukan aksi

        [StringLength(50)]
        [Display(Name = "IP Address")]
        public string? IpAddress { get; set; }

        [StringLength(200)]
        [Display(Name = "User Agent")]
        public string? UserAgent { get; set; }
    }
}

