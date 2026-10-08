using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk Dock/Area Loading Customer
    /// </summary>
    public class Dock
    {
        [Key]
        public int DockId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required(ErrorMessage = "Kode dock wajib diisi")]
        [StringLength(50)]
        public string DockCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nama dock wajib diisi")]
        [StringLength(200)]
        public string DockName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Location { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        // Navigation properties
        [ForeignKey("CustomerId")]
        public virtual Customer Customer { get; set; } = null!;
        public virtual ICollection<DeliverySchedule> DeliverySchedules { get; set; } = new List<DeliverySchedule>();
    }
}

