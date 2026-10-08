using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk Item yang akan di-deliver pada setiap schedule
    /// </summary>
    public class DeliveryItem
    {
        [Key]
        public int DeliveryItemId { get; set; }

        [Required]
        public int ScheduleId { get; set; }

        [Required]                    
        public int ItemId { get; set; }

        [Required(ErrorMessage = "Quantity wajib diisi")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ActualQuantity { get; set; }

        [StringLength(20)]
        public string? Unit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalWeight { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalVolume { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        /// <summary>
        /// Part No eksternal asli dari manifest (contoh: "23827-BZ060-00").
        /// Disimpan saat import dan tetap ada meski item di-remap ke master VIN.
        /// </summary>
        [StringLength(100)]
        public string? ExternalPartNo { get; set; }

        public int? QtyLotOverride { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? PrepScanTime { get; set; }
        public DateTime? UpdatedDate { get; set; }

        // Calculated Kanban
        [NotMapped]
        public int CalculatedKanbanTarget
        {
            get
            {
                var qpc = QtyLotOverride ?? Item?.QtyLot ?? 1;
                return qpc > 0 ? (int)Math.Ceiling((double)Quantity / qpc) : 0;
            }
        }

        [NotMapped]
        public int CalculatedKanbanActual
        {
            get
            {
                var qpc = QtyLotOverride ?? Item?.QtyLot ?? 1;
                if (qpc <= 0) return 0;
                // Gunakan Ceiling (sama persis dengan formula di Portal Preparation, PreparationController, dan validasi Enter Dock)
                // agar angka kanban konsisten di Dashboard Shipping, Portal Preparation, dan Portal Driver.
                // Jika pieces sudah 100%, kembalikan target agar tidak ada float mismatch.
                if ((ActualQuantity ?? 0) >= Quantity)
                    return CalculatedKanbanTarget;
                return (int)Math.Ceiling((double)(ActualQuantity ?? 0) / qpc);
            }
        }

        // Calculated property
        [NotMapped]
        public decimal? VarianceQuantity
        {
            get
            {
                if (ActualQuantity.HasValue)
                    return ActualQuantity.Value - Quantity;
                return null;
            }
        }

        // Navigation properties
        [ForeignKey("ScheduleId")]
        public virtual DeliverySchedule? DeliverySchedule { get; set; }

        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }
    }
}

