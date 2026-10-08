using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models.ViewModels
{
    public class CreateOrderDeliveryViewModel
    {
        // General Section (Auto-filled)
        [Required]
        [Display(Name = "Sales Order")]
        public string SalesOrder { get; set; } = string.Empty;

        [Display(Name = "Purchase Order")]
        public string PurchaseOrder { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Shipping Point")]
        public string ShippingPoint { get; set; } = string.Empty;

        // Header Section (User Input)
        [Required]
        [Display(Name = "Sold-to-party")]
        public string SoldToParty { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Ship-to-party")]
        public string ShipToParty { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Delivery Date")]
        public DateTime DeliveryDate { get; set; } = DateTime.Now;

        [Display(Name = "Dock Code")]
        public bool DockCode { get; set; }

        [Display(Name = "Vehicle Number")]
        public string? VehicleNumber { get; set; }

        [Display(Name = "Route Number")]
        public string? RouteNumber { get; set; }

        [Display(Name = "Delivery Text")]
        public string? DeliveryText { get; set; }

        // Items (Editable in table)
        public List<CreateOrderDeliveryItemViewModel> Items { get; set; } = new List<CreateOrderDeliveryItemViewModel>();
    }

    public class CreateOrderDeliveryItemViewModel
    {
        public string ItemCode { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CustMaterialInfo { get; set; } = string.Empty;
        public string ShipToMaterial { get; set; } = string.Empty;
        
        // Editable fields
        public string Barcode { get; set; } = string.Empty;
        public decimal OdQty { get; set; }
        public string Sloc { get; set; } = string.Empty;
    }
}
