using SistemPacking.Web.Models;

namespace SistemPacking.Web.Models;

public class Order : BaseEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public string? DeliveryNote { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public int? ReleasedById { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public int? CancelledById { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int? UploadHistoryId { get; set; }
    public string? Notes { get; set; }
    public DateTime? PlanDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? PlanETD { get; set; }
    public string? ActualETD { get; set; }
    public string? DeliveryRemark { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public User? ReleasedBy { get; set; }
    public User? CancelledBy { get; set; }
    public OrderUploadHistory? UploadHistory { get; set; }
    public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
    public Verification? Verification { get; set; }
}

public class OrderDetail : BaseEntity
{
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public decimal TargetQty { get; set; }
    public decimal ActualQty { get; set; } = 0;
    public decimal ShoppingQty { get; set; } = 0;
    public string? Notes { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public ICollection<ShoppingLog> ShoppingLogs { get; set; } = new List<ShoppingLog>();
    public ICollection<PackingLog> PackingLogs { get; set; } = new List<PackingLog>();
}

public class OrderUploadHistory : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public int UploadedById { get; set; }
    public DateTime UploadedAt { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public string? ErrorLog { get; set; }

    // Navigation
    public User UploadedBy { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}

public class ShoppingLog : BaseEntity
{
    public int? OrderDetailId { get; set; }
    public int? ItemId { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public decimal ScannedQty { get; set; }
    public int OperatorId { get; set; }
    public int? ShiftId { get; set; }
    public bool IsValid { get; set; } = true;
    public string? InvalidReason { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;

    // Navigation
    public OrderDetail? OrderDetail { get; set; }
    public Item? Item { get; set; }
    public User Operator { get; set; } = null!;
    public Shift? Shift { get; set; }
}

public class PackingLog : BaseEntity
{
    public int OrderDetailId { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public decimal PackedQty { get; set; }
    public int OperatorId { get; set; }
    public int? ShiftId { get; set; }
    public DateTime PackedAt { get; set; } = DateTime.Now;

    // Navigation
    public OrderDetail OrderDetail { get; set; } = null!;
    public User Operator { get; set; } = null!;
    public Shift? Shift { get; set; }
}

public class ScanNgLog : BaseEntity
{
    public string ScannedBarcode { get; set; } = string.Empty;
    public string? ItemCode { get; set; }
    public string? Category { get; set; } // "Export" or "SparePart" or "Unknown"
    public string Module { get; set; } = string.Empty; // "Shopping", "Export", "SparePart"
    public string ErrorMessage { get; set; } = string.Empty;
    public int OperatorId { get; set; }
    public int? ShiftId { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;

    // Navigation
    public User Operator { get; set; } = null!;
    public Shift? Shift { get; set; }
}

public class Verification : BaseEntity
{
    public int OrderId { get; set; }
    public int LeaderId { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;
    public string? Notes { get; set; }
    public int? RejectReasonId { get; set; }
    public DateTime? VerifiedAt { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public User Leader { get; set; } = null!;
    public ReasonReject? RejectReason { get; set; }
}

public class ShoppingLogView
{
    public int Id { get; set; }
    public DateTime ScannedAt { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kanban { get; set; } = string.Empty;
    public decimal ScannedQty { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? InvalidReason { get; set; }
}

public class PackingLogView
{
    public int Id { get; set; }
    public DateTime PackedAt { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ScannedBarcode { get; set; } = string.Empty;
    public decimal PackedQty { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
