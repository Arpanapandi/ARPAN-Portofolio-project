using SistemPacking.Web.Models;

namespace SistemPacking.Web.Models;

// Auth DTOs
public class LoginDto
{
    public string NIK { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class UserSessionDto
{
    public int Id { get; set; }
    public string NIK { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool CanManageMaster { get; set; }
    public bool CanManageOrder { get; set; }
    public bool CanShopping { get; set; }
    public bool CanPacking { get; set; }
    public bool CanVerify { get; set; }
    public bool CanViewReport { get; set; }
    public bool CanManageUser { get; set; }
}

// Order DTOs
public class OrderDto
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public decimal TotalTarget { get; set; }
    public decimal TotalShopped { get; set; }
    public decimal TotalActual { get; set; }
    public decimal PackingProgress { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderDetailDto> Details { get; set; } = new();
}

public class OrderDetailDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public string? FGLocation { get; set; }
    public decimal TargetQty { get; set; }
    public decimal ShoppingQty { get; set; }
    public decimal ActualQty { get; set; }
    public decimal ProgressPercent => TargetQty > 0 ? Math.Min(100, ActualQty / TargetQty * 100) : 0;
    public bool IsComplete => ActualQty >= TargetQty;
}

public class OrderFlattenedDto
{
    public int OrderId { get; set; }
    public int OrderDetailId { get; set; }
    public DateTime OrderDate { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Dock { get; set; }
    public string PartNo { get; set; } = string.Empty;
    public string? VIN { get; set; }
    public decimal TargetQty { get; set; }
    public decimal ActualQty { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
}

// Scan DTOs
public class ScanResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? Barcode { get; set; }
    public string? UOM { get; set; }
    public decimal TargetQty { get; set; }
    public decimal ScannedQty { get; set; }
    public decimal ActualQty { get; set; }
    public decimal Remaining { get; set; }
    public string? SpisImagePath { get; set; }
    public string? SppsImagePath { get; set; }
    public int? OrderDetailId { get; set; }
    public int OrderId { get; set; }
    public bool IsOverQty { get; set; }
    public bool IsComplete { get; set; }
    
    // Check Points
    public string? Point1 { get; set; }
    public string? Point2 { get; set; }
    public string? Point3 { get; set; }
    public string? Point4 { get; set; }
    public string? Point5 { get; set; }
    
    // Additional Master Data
    public string? VIN { get; set; }
    public string? Dock { get; set; }
    public string? CustomerName { get; set; }
}

// Dashboard DTOs
public class DashboardKpiDto
{
    public int TotalOrders { get; set; }
    public int WaitingShopping { get; set; }
    public int InShopping { get; set; }
    public int ShoppingComplete { get; set; }
    public int InPacking { get; set; }
    public int PackingComplete { get; set; }
    public int WaitingVerification { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Finished { get; set; }
    public decimal OverallProgress { get; set; }
    public List<OrderSummaryDto> RecentOrders { get; set; } = new();
    public List<ChartDataDto> DailyOrderChart { get; set; } = new();
    public List<ChartDataDto> ProductivityChart { get; set; } = new();
}

public class OrderSummaryDto
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal TargetQty { get; set; }
    public decimal ShoppingQty { get; set; }
    public decimal ActualQty { get; set; }
    public decimal Progress { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
}

public class ChartDataDto
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string? Color { get; set; }
}

// Report DTOs
public class ReportFilterDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? CustomerId { get; set; }
    public int? ShiftId { get; set; }
    public int? OperatorId { get; set; }
    public string? Status { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
}

// Upload Result DTO
public class UploadResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public int? HistoryId { get; set; }
}

// Shipping Dashboard DTOs
public class ShippingDashboardKpiDto
{
    public int ProgresShoppingCount { get; set; }
    public decimal ProgresShoppingTarget { get; set; }
    public decimal ProgresShoppingActual { get; set; }
    public int ProgresPackingCount { get; set; }
    public decimal ProgresPackingTarget { get; set; }
    public decimal ProgresPackingActual { get; set; }
    public int ProgresDeliveryCount { get; set; }
    public decimal ProgresDeliveryTarget { get; set; }
    public decimal ProgresDeliveryActual { get; set; }
    public int DelayShoppingCount { get; set; }
    public int DelayPackingCount { get; set; }
    public int DelayDeliveryCount { get; set; }
    public int FinishedCount { get; set; }
}

public class ShoppingIndicatorRowDto
{
    public int OrderDetailId { get; set; }
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string CustPartNo { get; set; } = string.Empty;
    public decimal QtyOrder { get; set; }
    public decimal QtyActShop { get; set; }
    public decimal QtyMinus => QtyOrder - QtyActShop;
    public string StockFG { get; set; } = "N/A";
}

public class PackingIndicatorRowDto
{
    public int OrderDetailId { get; set; }
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string CustPartNo { get; set; } = string.Empty;
    public decimal QtyOrder { get; set; }
    public decimal QtyPacked { get; set; }
    public decimal QtyMinus => QtyOrder - QtyPacked;
    public decimal QtyShopping { get; set; }
}

public class DeliveryIndicatorRowDto
{
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? PlanDeliveryDate { get; set; }
    public decimal TotalQty { get; set; }
    public string? PlanETD { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? DeliveryRemark { get; set; }
    public string? ActualETD { get; set; }
    public bool IsDelay { get; set; }
}

public class FinishedRowDto
{
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? PlanDeliveryDate { get; set; }
    public decimal TotalQty { get; set; }
    public string? PlanETD { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? DeliveryRemark { get; set; }
    public string? ActualETD { get; set; }
}

// Verification DTO
public class VerificationDto
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int LeaderId { get; set; }
    public string LeaderName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? RejectReason { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public decimal OverallProgress { get; set; }
    public List<OrderDetailDto> Details { get; set; } = new();
}
