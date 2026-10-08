using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dashboardKlinik.Models
{
    public class KunjunganKlinik
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "Timestamp")]
        public DateTime Timestamp { get; set; }

        [Required]
        [Display(Name = "Tanggal Kunjungan")]
        [DataType(DataType.Date)]
        public DateTime TanggalKunjungan { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Nama Pasien")]
        public string NamaPasien { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Display(Name = "NPK")]
        public string NPK { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Plant")]
        public string Plant { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Departemen")]
        public string Departemen { get; set; } = string.Empty;

        [Required]
        [StringLength(1)]
        [Display(Name = "Jenis Kelamin")]
        public string JenisKelamin { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        [Display(Name = "Keluhan")]
        public string Keluhan { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        [Display(Name = "Diagnosa")]
        public string Diagnosa { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        [Display(Name = "Dokter")]
        public string Dokter { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Status")]
        public string Status { get; set; } = string.Empty;

        // Foreign Key
        public int? PasienId { get; set; }

        [ForeignKey("PasienId")]
        public virtual Pasien? Pasien { get; set; }
    }
}
