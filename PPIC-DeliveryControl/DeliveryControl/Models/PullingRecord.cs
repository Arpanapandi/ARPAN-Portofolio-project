using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    [Table("PullingRecords")]
    public class PullingRecord
    {
        [Key]
        public int PullingId { get; set; }

        [StringLength(20)]
        public string Plant { get; set; } = string.Empty; // Molded, Hose, RVI

        [StringLength(5)]
        public string Rack { get; set; } = string.Empty; // A - K

        public int Column { get; set; } // 1 - 20

        [Required(ErrorMessage = "Tag wajib diisi")]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required(ErrorMessage = "Label wajib diisi")]
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }
        
        public int? ItemId { get; set; }
        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; } = 1;

        /// <summary>True jika record ini dibuat via Manual Adjust (bukan scan)</summary>
        public bool IsManualAdjust { get; set; } = false;

        /// <summary>Alasan/catatan untuk manual adjust</summary>
        [StringLength(500)]
        public string? AdjustNote { get; set; }

        /// <summary>
        /// Remark: "Match" = normal (affect stock), "Mismatch" = log only (no stock impact)
        /// </summary>
        [StringLength(20)]
        public string Remark { get; set; } = "Match";
    }
}
