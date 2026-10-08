using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dashboardKlinik.Models
{
    public class Pasien
    {
        [Key]
        public int Id { get; set; }

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

        [Display(Name = "Tanggal Terdaftar")]
        public DateTime TanggalTerdaftar { get; set; } = DateTime.Now;

        // Navigation property
        public virtual ICollection<KunjunganKlinik> Kunjungan { get; set; } = new List<KunjunganKlinik>();
    }
}
