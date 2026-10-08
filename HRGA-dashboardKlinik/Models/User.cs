using System.ComponentModel.DataAnnotations;

namespace dashboardKlinik.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Nama wajib diisi")]
        [StringLength(100, ErrorMessage = "Nama maksimal 100 karakter")]
        [Display(Name = "Nama Lengkap")]
        public string Nama { get; set; } = string.Empty;

        [Required(ErrorMessage = "Username wajib diisi")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Username minimal 3 dan maksimal 50 karakter")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password wajib diisi")]
        [StringLength(255)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "NPK wajib diisi")]
        [RegularExpression(@"^\d{4}$", ErrorMessage = "NPK harus berupa 4 digit angka")]
        [StringLength(4, MinimumLength = 4, ErrorMessage = "NPK harus tepat 4 digit")]
        [Display(Name = "NPK")]
        public string NPK { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role wajib dipilih")]
        [StringLength(50)]
        [Display(Name = "Role")]
        public string Role { get; set; } = "Admin"; // Admin, Dokter, Perawat, Staff

        [Required]
        [StringLength(20)]
        [Display(Name = "Status")]
        public string Status { get; set; } = "Aktif"; // Aktif, Non-Aktif

        [Display(Name = "Tanggal Dibuat")]
        public DateTime TanggalDibuat { get; set; } = DateTime.Now;
    }
}
