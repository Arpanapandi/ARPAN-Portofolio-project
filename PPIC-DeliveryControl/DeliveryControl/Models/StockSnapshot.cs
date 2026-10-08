using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Snapshot harian stock level per item, diambil otomatis jam 08:00
    /// </summary>
    public class StockSnapshot
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Tanggal snapshot (biasanya = hari ini saat jam 08:00)</summary>
        [Required]
        public DateTime SnapshotDate { get; set; }

        /// <summary>Jam snapshot (08:00 untuk auto, bisa berbeda untuk manual)</summary>
        public TimeSpan SnapshotTime { get; set; }

        /// <summary>Item code / VIN</summary>
        [Required]
        [StringLength(100)]
        public string ItemCode { get; set; } = string.Empty;

        [StringLength(200)]
        public string ItemName { get; set; } = string.Empty;

        /// <summary>Plant: Hose, Molded, RVI</summary>
        [StringLength(50)]
        public string Plant { get; set; } = string.Empty;

        /// <summary>Jumlah stock saat snapshot (satuan lot/kanban)</summary>
        public int StockQty { get; set; }

        /// <summary>Coverage dalam hari (StockQty / RackMin)</summary>
        [Column(TypeName = "decimal(10,4)")]
        public decimal DaysCoverage { get; set; }

        /// <summary>&lt;1D | &lt;1.5D | 1.5-2D | 2-3D | &gt;3D</summary>
        [StringLength(20)]
        public string StockLevel { get; set; } = string.Empty;

        /// <summary>true = dipicu manual (bukan jam 08:00 otomatis)</summary>
        public bool IsManual { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
