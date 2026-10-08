using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class PreparationRecord
    {
        [Key]
        public int PreparationId { get; set; }

        [StringLength(20)]
        public string Plant { get; set; } = string.Empty;

        [StringLength(5)]
        public string? Rack { get; set; }

        public int? Column { get; set; }

        [Required(ErrorMessage = "Tag wajib diisi")]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required(ErrorMessage = "Label wajib diisi")]
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kanban wajib diisi")]
        [StringLength(100)]
        public string Kanban { get; set; } = string.Empty;

        public int? ScheduleId { get; set; }

        [ForeignKey("ScheduleId")]
        public virtual DeliverySchedule? DeliverySchedule { get; set; }

        /// <summary>
        /// Nomor manifest (ScheduleNumber) yang menjadi tujuan item ini.
        /// Diisi saat scan: jika jadwal ditemukan langsung → ScheduleNumber jadwal tersebut.
        /// Jika pending → ScheduleNumber yang dicari dari kanban barcode.
        /// SyncService hanya akan menyinkronkan record ini ke jadwal dengan ScheduleNumber yang sama.
        /// </summary>
        [StringLength(100)]
        public string? ManifestNumber { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        /// <summary>
        /// Remark: "Match" = normal (affect stock), "Mismatch" = log only (no stock impact)
        /// </summary>
        [StringLength(20)]
        public string Remark { get; set; } = "Match";

        /// <summary>
        /// Dock yang dipilih operator sebelum scan (CustomerName dari Customer master).
        /// Digunakan untuk memastikan scan preparation masuk ke jadwal dock yang tepat.
        /// </summary>
        [StringLength(200)]
        public string? DockName { get; set; }

        /// <summary>
        /// Cycle dari dock yang dipilih (mis. A1, B2).
        /// Bersama DockName, digunakan sebagai kunci matching ke jadwal saat sync pending.
        /// </summary>
        [StringLength(50)]
        public string? DockCycle { get; set; }

        /// <summary>
        /// CustomerId dari Customer yang dipilih operator sebelum scan.
        /// FK lunak (tidak ada constraint) — digunakan untuk filter jadwal saat sync.
        /// </summary>
        public int? DockCustomerId { get; set; }

        /// <summary>
        /// Tanggal jadwal target scan (null = hari ini / gunakan CreatedDate).
        /// Diisi saat operator memilih tanggal masa depan di form scan preparation.
        /// Digunakan oleh SyncService untuk mencocokkan ke jadwal pada tanggal yang tepat.
        /// </summary>
        public DateTime? TargetDate { get; set; }

        /// <summary>
        /// Flag sementara (tidak disimpan ke DB): bypass validasi stock habis di rak.
        /// Diisi dari request body saat toggle "Skip Stock Validation" aktif.
        /// </summary>
        [NotMapped]
        public bool SkipStockValidation { get; set; } = false;

        [NotMapped]
        public int SumQty { get; set; } = 1;
    }
}
