using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models.ViewModels
{
    public class AutoPrintDNViewModel
    {
        [Required(ErrorMessage = "Sales Order (SO) Number is required.")]
        [Display(Name = "Sales Order (SO) Number")]
        public string SONumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "SAP API URL is required.")]
        [Display(Name = "SAP API URL")]
        public string SapUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "Header Key is required.")]
        [Display(Name = "Header Key")]
        public string HeaderKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "Header Value is required.")]
        [Display(Name = "Header Value")]
        public string HeaderValue { get; set; } = string.Empty;

        [Required(ErrorMessage = "Shipping Point is required.")]
        [Display(Name = "Shipping Point")]
        public string ShippingPoint { get; set; } = string.Empty;
    }
}
