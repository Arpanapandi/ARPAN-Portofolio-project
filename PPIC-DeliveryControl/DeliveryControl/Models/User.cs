using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk data User dengan role authentication
    /// </summary>
    public class User
    {
        [Key]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Username wajib diisi")]
        [StringLength(50)]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [StringLength(255)]
        [Display(Name = "Password")]
        [DataType(DataType.Password)]
        public string? Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama lengkap wajib diisi")]
        [StringLength(200)]
        [Display(Name = "Nama Lengkap")]
        public string FullName { get; set; } = string.Empty;

        [StringLength(100)]
        [EmailAddress(ErrorMessage = "Format email tidak valid")]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Role wajib dipilih")]
        [StringLength(20)]
        [Display(Name = "Role")]
        public string Role { get; set; } = "User"; // Admin atau User

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Jika true, user memiliki akses ke SEMUA dock (termasuk dock baru yang ditambahkan)
        /// </summary>
        [Display(Name = "Akses Semua Dock")]
        public bool HasAllDockAccess { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        [NotMapped]
        [Display(Name = "Konfirmasi Password")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Password dan konfirmasi password tidak sama")]
        public string? ConfirmPassword { get; set; }

        // Navigation properties
        public virtual ICollection<UserDockAccess> UserDockAccesses { get; set; } = new List<UserDockAccess>();
    }
}

