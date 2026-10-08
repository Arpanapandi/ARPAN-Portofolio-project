using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    public class ShoppingRecord
    {
        [Key]
        public int ShoppingId { get; set; }

        public int? ItemId { get; set; }

        [Required]
        [StringLength(100)]
        public string Tag { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Label { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Kanban { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(20)]
        public string? Plant { get; set; }
        
        [StringLength(100)]
        public string? TargetCustomer { get; set; }

        [NotMapped]
        public int SumQty { get; set; } = 1;
    }
}
