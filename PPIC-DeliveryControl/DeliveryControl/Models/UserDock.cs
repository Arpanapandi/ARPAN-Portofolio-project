using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Junction table: relasi User ke Dock/Customer yang diizinkan.
    /// Digunakan untuk role "Prepare" agar user hanya bisa handle dock tertentu.
    /// </summary>
    public class UserDock
    {
        [Key]
        public int UserDockId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int CustomerId { get; set; }

        public DateTime AssignedDate { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [ForeignKey("CustomerId")]
        public virtual Customer? Customer { get; set; }
    }
}
