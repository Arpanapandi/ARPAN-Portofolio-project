using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Lokasi rak fisik untuk satu Item. Satu Item bisa punya banyak lokasi rak.
    /// </summary>
    [Table("ItemRackLocations")]
    public class ItemRackLocation
    {
        [Key]
        public int Id { get; set; }

        public int ItemId { get; set; }

        [Required]
        [StringLength(10)]
        public string Rack { get; set; } = string.Empty;   // A – I

        public int NoRack { get; set; }                    // 1 – 33

        [StringLength(20)]
        public string? Plant { get; set; }                 // Molded / Hose / RVI

        public int SortOrder { get; set; } = 0;

        [ForeignKey("ItemId")]
        public virtual Item? Item { get; set; }
    }
}
