using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class PremiumFreight
    {
        [Key]
        public int Id { get; set; }

        public int? ScheduleId { get; set; }

        [StringLength(100)]
        public string? FreightNumber { get; set; }

        [Required(ErrorMessage = "Kategori Pengiriman wajib diisi")]
        [StringLength(100)]
        public string KategoriPengiriman { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jenis Armada wajib diisi")]
        [StringLength(100)]
        public string JenisArmada { get; set; } = string.Empty;

        [StringLength(100)]
        public string? VehicleNumber { get; set; }

        [StringLength(100)]
        public string? DriverName { get; set; }

        [StringLength(100)]
        public string? CustomerName { get; set; }

        [StringLength(100)]
        public string? Route { get; set; }

        [StringLength(50)]
        public string? Cycle { get; set; }

        public DateTime? ScheduledDate { get; set; }

        [StringLength(500)]
        public string? Keterangan { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Submitted";

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [ForeignKey("ScheduleId")]
        public virtual DeliverySchedule? DeliverySchedule { get; set; }
    }
}
