using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk mapping akses Dock per User
    /// </summary>
    public class UserDockAccess
    {
        [Key]
        public int UserDockAccessId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int DockId { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;

        [ForeignKey("DockId")]
        public virtual Dock Dock { get; set; } = null!;
    }
}
