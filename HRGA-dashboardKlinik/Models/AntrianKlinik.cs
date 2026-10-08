using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dashboardKlinik.Models
{
    public class AntrianKlinik
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        [Display(Name = "Nomor Antrian")]
        public string NomorAntrian { get; set; } = string.Empty; // Misal: A-001

        [Required]
        [Display(Name = "Nomor Urut")]
        public int NomorUrut { get; set; } // 1, 2, 3...

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Tanggal")]
        public DateTime Tanggal { get; set; } = DateTime.Today;

        [Display(Name = "Waktu Daftar")]
        public DateTime WaktuDaftar { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "NPK wajib diisi")]
        [StringLength(4, ErrorMessage = "NPK harus 4 digit angka")]
        [Display(Name = "NPK")]
        public string NPK { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama Pasien wajib diisi")]
        [StringLength(200)]
        [Display(Name = "Nama Pasien")]
        public string NamaPasien { get; set; } = string.Empty;

        [Required(ErrorMessage = "Plant wajib dipilih")]
        [StringLength(50)]
        [Display(Name = "Plant")]
        public string Plant { get; set; } = string.Empty; // MOLDED / HOSE

        [Required(ErrorMessage = "Departemen wajib dipilih")]
        [StringLength(100)]
        [Display(Name = "Departemen")]
        public string Departemen { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jenis Kelamin wajib dipilih")]
        [StringLength(1)]
        [Display(Name = "Jenis Kelamin")]
        public string JenisKelamin { get; set; } = string.Empty; // L / P

        [Required(ErrorMessage = "Keluhan wajib diisi")]
        [StringLength(500)]
        [Display(Name = "Keluhan")]
        public string Keluhan { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        [Display(Name = "Status Antrian")]
        public string StatusAntrian { get; set; } = "Menunggu"; // Menunggu, Dipanggil, Sedang Diperiksa, Selesai, Batal

        [Display(Name = "Waktu Dipanggil")]
        public DateTime? WaktuDipanggil { get; set; }

        [Display(Name = "Waktu Selesai")]
        public DateTime? WaktuSelesai { get; set; }

        // Relasi Opsional
        public int? PasienId { get; set; }

        [ForeignKey("PasienId")]
        public virtual Pasien? Pasien { get; set; }

        public int? KunjunganId { get; set; }

        [ForeignKey("KunjunganId")]
        public virtual KunjunganKlinik? Kunjungan { get; set; }
    }
}
