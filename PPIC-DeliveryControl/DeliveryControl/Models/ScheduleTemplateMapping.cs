using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk mapping field target sistem dengan keyword di file sumber (Contoh: "DN NO" -> "Manifest")
    /// </summary>
    public class ScheduleTemplateMapping
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TemplateId { get; set; }

        [ForeignKey("TemplateId")]
        public virtual ScheduleTemplate? Template { get; set; }

        /// <summary>
        /// Nama Field Target di Sistem (Manifest, PartNo, PartName, Qty, Date, Route, Cycle)
        /// </summary>
        [Required]
        [Display(Name = "Field Target")]
        [StringLength(50)]
        public string TargetField { get; set; } = string.Empty;

        /// <summary>
        /// Keyword judul kolom di file (koma dipisahkan)
        /// </summary>
        [Required]
        [Display(Name = "Keyword Sumber")]
        public string SourceKeywords { get; set; } = string.Empty;

        /// <summary>
        /// Apakah ini data Header (sekali per file) atau Detail (berbaris)
        /// </summary>
        [Display(Name = "Header")]
        public bool IsHeader { get; set; } = false;

        /// <summary>
        /// Opsional: Indeks kolom (0-indexed) sebagai cadangan jika keyword tidak ditemukan
        /// </summary>
        [Display(Name = "Indeks Kolom (Opsional)")]
        public int? ColumnIndex { get; set; }
        
        /// <summary>
        /// Regex pattern khusus (opsional) untuk ekstraksi teks kompleks
        /// </summary>
        [Display(Name = "Pattern Regex (Opsional)")]
        public string? RegexPattern { get; set; }
    }
}
