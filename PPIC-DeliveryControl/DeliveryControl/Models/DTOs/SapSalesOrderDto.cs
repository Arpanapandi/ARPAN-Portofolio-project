namespace DeliveryControl.Models.DTOs
{
    public class SapSalesOrderDto
    {
        public string SalesOrderNumber { get; set; } = string.Empty;
        public string PurchaseOrderNumber { get; set; } = string.Empty;
        public string ShippingPoint { get; set; } = string.Empty;
        
        public List<SapSalesOrderItemDto> Items { get; set; } = new List<SapSalesOrderItemDto>();
    }

    public class SapSalesOrderItemDto
    {
        public string ItemCode { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CustMaterialInfo { get; set; } = string.Empty;
        public string ShipToMaterial { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal OdQty { get; set; }
        public string Sloc { get; set; } = string.Empty;
    }
}
