using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Konfigurasi sistem sederhana berbasis key-value.
    /// Contoh: AutoSchedulerCutoffTime = "18:00"
    /// </summary>
    public class SystemSetting
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }
    }
}


