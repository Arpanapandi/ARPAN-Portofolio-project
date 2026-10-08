using System.ComponentModel.DataAnnotations;

namespace dashboardKlinik.Models
{
    public class ActivityLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [StringLength(100)]
        public string EntityType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
