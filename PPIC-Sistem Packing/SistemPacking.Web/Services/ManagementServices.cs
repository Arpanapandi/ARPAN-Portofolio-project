using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;

namespace SistemPacking.Web.Services;

public interface IVerificationService
{
    Task<IEnumerable<VerificationDto>> GetPendingVerificationsAsync();
    Task<VerificationDto?> GetVerificationAsync(int orderId);
    Task<(bool Success, string Message)> ApproveAsync(int orderId, int leaderId, string? notes);
    Task<(bool Success, string Message)> RejectAsync(int orderId, int leaderId, string notes, int? reasonId);
    Task<(bool Success, string Message)> SubmitForVerificationAsync(int orderId, int leaderId);
}

public class VerificationService : IVerificationService
{
    private readonly IUnitOfWork _uow;

    public VerificationService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<IEnumerable<VerificationDto>> GetPendingVerificationsAsync()
    {
        var verifications = await _uow.Verifications.GetPendingAsync();
        return verifications.Select(v => new VerificationDto
        {
            Id = v.Id,
            OrderId = v.OrderId,
            OrderNo = v.Order?.OrderNo ?? "",
            CustomerName = v.Order?.Customer?.CustomerName ?? "",
            LeaderId = v.LeaderId,
            LeaderName = v.Leader?.FullName ?? "",
            Status = v.Status.ToString(),
            Notes = v.Notes,
            VerifiedAt = v.VerifiedAt
        });
    }

    public async Task<VerificationDto?> GetVerificationAsync(int orderId)
    {
        var order = await _uow.Orders.GetWithDetailsAsync(orderId);
        if (order == null) return null;

        var verification = await _uow.Verifications.GetByOrderAsync(orderId);
        var details = order.OrderDetails?.Select(od => new OrderDetailDto
        {
            Id = od.Id,
            OrderId = od.OrderId,
            ItemId = od.ItemId,
            ItemCode = od.Item?.ItemCode ?? "",
            ItemName = od.Item?.ItemName ?? "",
            Barcode = od.Item?.Barcode ?? "",
            UOM = od.Item?.UOM ?? "",
            FGLocation = od.Item?.FGLocation?.LocationCode,
            TargetQty = od.TargetQty,
            ShoppingQty = od.ShoppingQty,
            ActualQty = od.ActualQty
        }).ToList() ?? new();

        decimal totalTarget = details.Sum(d => d.TargetQty);
        decimal totalActual = details.Sum(d => d.ActualQty);

        return new VerificationDto
        {
            Id = verification?.Id ?? 0,
            OrderId = orderId,
            OrderNo = order.OrderNo,
            CustomerName = order.Customer?.CustomerName ?? "",
            LeaderId = verification?.LeaderId ?? 0,
            LeaderName = verification?.Leader?.FullName ?? "",
            Status = verification?.Status.ToString() ?? "Pending",
            Notes = verification?.Notes,
            RejectReason = verification?.RejectReason?.Description,
            VerifiedAt = verification?.VerifiedAt,
            OverallProgress = totalTarget > 0 ? Math.Min(100, totalActual / totalTarget * 100) : 0,
            Details = details
        };
    }

    public async Task<(bool Success, string Message)> SubmitForVerificationAsync(int orderId, int leaderId)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");
        if (order.Status != OrderStatus.PackingComplete)
            return (false, "Order belum selesai packing.");

        // Create verification record
        var verification = new Verification
        {
            OrderId = orderId,
            LeaderId = leaderId,
            Status = VerificationStatus.Pending
        };
        await _uow.Verifications.AddAsync(verification);

        order.Status = OrderStatus.WaitingVerification;
        await _uow.Orders.UpdateAsync(order);

        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = leaderId,
            Action = "SUBMIT_VERIFICATION",
            Description = $"Order {order.OrderNo} disubmit untuk verifikasi",
            Module = "Verification"
        });

        await _uow.SaveChangesAsync();
        return (true, "Order berhasil disubmit untuk verifikasi.");
    }

    public async Task<(bool Success, string Message)> ApproveAsync(int orderId, int leaderId, string? notes)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");
        if (order.Status != OrderStatus.WaitingVerification)
            return (false, "Order tidak dalam status Waiting Verification.");

        var verification = await _uow.Verifications.GetByOrderAsync(orderId);
        if (verification == null) return (false, "Data verifikasi tidak ditemukan.");

        verification.Status = VerificationStatus.Approved;
        verification.Notes = notes;
        verification.LeaderId = leaderId;
        verification.VerifiedAt = DateTime.Now;
        await _uow.Verifications.UpdateAsync(verification);

        order.Status = OrderStatus.Finished;
        await _uow.Orders.UpdateAsync(order);

        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = leaderId,
            Action = "APPROVE_ORDER",
            Description = $"Order {order.OrderNo} diapprove. Catatan: {notes}",
            Module = "Verification"
        });

        await _uow.SaveChangesAsync();
        return (true, $"Order {order.OrderNo} berhasil diapprove dan selesai.");
    }

    public async Task<(bool Success, string Message)> RejectAsync(int orderId, int leaderId, string notes, int? reasonId)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");
        if (order.Status != OrderStatus.WaitingVerification)
            return (false, "Order tidak dalam status Waiting Verification.");

        var verification = await _uow.Verifications.GetByOrderAsync(orderId);
        if (verification == null) return (false, "Data verifikasi tidak ditemukan.");

        verification.Status = VerificationStatus.Rejected;
        verification.Notes = notes;
        verification.LeaderId = leaderId;
        verification.RejectReasonId = reasonId;
        verification.VerifiedAt = DateTime.Now;
        await _uow.Verifications.UpdateAsync(verification);

        // Return to ReadyPacking so operator can redo
        order.Status = OrderStatus.ReadyPacking;
        await _uow.Orders.UpdateAsync(order);

        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = leaderId,
            Action = "REJECT_ORDER",
            Description = $"Order {order.OrderNo} direject. Alasan: {notes}",
            Module = "Verification"
        });

        await _uow.SaveChangesAsync();
        return (true, $"Order {order.OrderNo} direject dan dikembalikan ke Ready Packing.");
    }
}

public interface IDashboardService
{
    Task<DashboardKpiDto> GetDashboardKpiAsync();
    Task<IEnumerable<OrderSummaryDto>> GetOrderSummariesAsync();
}

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;

    public DashboardService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<DashboardKpiDto> GetDashboardKpiAsync()
    {
        var orders = await _uow.Orders.GetAllWithDetailsAsync();
        var orderList = orders.ToList();

        var recent = orderList.Take(10).Select(o =>
        {
            var details = o.OrderDetails?.ToList() ?? new();
            decimal totalTarget = details.Sum(d => d.TargetQty);
            decimal totalActual = details.Sum(d => d.ActualQty);
            return new OrderSummaryDto
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                CustomerName = o.Customer?.CustomerName ?? "",
                TargetQty = totalTarget,
                ShoppingQty = details.Sum(d => d.ShoppingQty),
                ActualQty = totalActual,
                Progress = totalTarget > 0 ? Math.Min(100, totalActual / totalTarget * 100) : 0,
                Status = o.Status,
                StatusLabel = OrderStatusHelper.GetLabel(o.Status),
                StatusColor = OrderStatusHelper.GetBadgeColor(o.Status),
                OrderDate = o.OrderDate
            };
        }).ToList();

        // Daily chart for last 7 days
        var dailyChart = Enumerable.Range(0, 7).Select(i =>
        {
            var date = DateTime.Today.AddDays(-i);
            var count = orderList.Count(o => o.OrderDate.Date == date.Date);
            return new ChartDataDto { Label = date.ToString("dd/MM"), Value = count };
        }).Reverse().ToList();

        return new DashboardKpiDto
        {
            TotalOrders = orderList.Count,
            WaitingShopping = orderList.Count(o => o.Status == OrderStatus.WaitingShopping),
            InShopping = orderList.Count(o => o.Status == OrderStatus.Shopping),
            ShoppingComplete = orderList.Count(o => o.Status == OrderStatus.ShoppingComplete),
            InPacking = orderList.Count(o => o.Status == OrderStatus.Packing),
            PackingComplete = orderList.Count(o => o.Status == OrderStatus.PackingComplete),
            WaitingVerification = orderList.Count(o => o.Status == OrderStatus.WaitingVerification),
            Approved = orderList.Count(o => o.Status == OrderStatus.Approved),
            Rejected = orderList.Count(o => o.Status == OrderStatus.Rejected),
            Finished = orderList.Count(o => o.Status == OrderStatus.Finished),
            RecentOrders = recent,
            DailyOrderChart = dailyChart
        };
    }

    public async Task<IEnumerable<OrderSummaryDto>> GetOrderSummariesAsync()
    {
        var orders = await _uow.Orders.GetAllWithDetailsAsync();
        return orders.Select(o =>
        {
            var details = o.OrderDetails?.ToList() ?? new();
            decimal totalTarget = details.Sum(d => d.TargetQty);
            decimal totalActual = details.Sum(d => d.ActualQty);
            return new OrderSummaryDto
            {
                Id = o.Id,
                OrderNo = o.OrderNo,
                CustomerName = o.Customer?.CustomerName ?? "",
                TargetQty = totalTarget,
                ShoppingQty = details.Sum(d => d.ShoppingQty),
                ActualQty = totalActual,
                Progress = totalTarget > 0 ? Math.Min(100, totalActual / totalTarget * 100) : 0,
                Status = o.Status,
                StatusLabel = OrderStatusHelper.GetLabel(o.Status),
                StatusColor = OrderStatusHelper.GetBadgeColor(o.Status),
                OrderDate = o.OrderDate
            };
        });
    }
}

// ============================================================
// Shipping Dashboard Service
// ============================================================
public interface IShippingDashboardService
{
    Task<ShippingDashboardKpiDto> GetShippingKpiAsync();
    Task<IEnumerable<ShoppingIndicatorRowDto>> GetShoppingProgressDataAsync();
    Task<IEnumerable<PackingIndicatorRowDto>> GetPackingProgressDataAsync();
    Task<IEnumerable<DeliveryIndicatorRowDto>> GetDeliveryProgressDataAsync();
    Task<IEnumerable<ShoppingIndicatorRowDto>> GetDelayShoppingDataAsync();
    Task<IEnumerable<PackingIndicatorRowDto>> GetDelayPackingDataAsync();
    Task<IEnumerable<DeliveryIndicatorRowDto>> GetDelayDeliveryDataAsync();
    Task<IEnumerable<FinishedRowDto>> GetFinishedDataAsync();
    Task<(bool Success, string Message)> BulkDeleteOrderDetailsAsync(int[] ids);
    Task<(bool Success, string Message)> BulkDeleteOrdersAsync(int[] ids);
    Task<(bool Success, string Message)> UpdateDeliveryAsync(int orderId, DateTime? planDeliveryDate, DateTime? actualDeliveryDate, string? planEtd, string? actualEtd, string? deliveryRemark);
}

public class ShippingDashboardService : IShippingDashboardService
{
    private readonly IUnitOfWork _uow;

    public ShippingDashboardService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ShippingDashboardKpiDto> GetShippingKpiAsync()
    {
        var orders = (await _uow.Orders.GetAllWithDetailsAsync()).ToList();
        var today = DateTime.Today;
        var h2 = today.AddDays(-2);
        var h1 = today.AddDays(-1);

        // Progres Shopping: order yg statusnya WaitingShopping/Shopping (masih aktif proses shopping)
        var shoppingOrders = orders.Where(o =>
            o.Status == OrderStatus.WaitingShopping ||
            o.Status == OrderStatus.Shopping).ToList();
        var shoppingDetails = shoppingOrders.SelectMany(o => o.OrderDetails ?? new List<OrderDetail>()).ToList();
        decimal shopTarget = shoppingDetails.Sum(d => d.TargetQty);
        decimal shopActual = shoppingDetails.Sum(d => d.ShoppingQty);

        // Progres Packing: order yg statusnya ReadyPacking/Packing
        var packingOrders = orders.Where(o =>
            o.Status == OrderStatus.ReadyPacking ||
            o.Status == OrderStatus.Packing ||
            o.Status == OrderStatus.ShoppingComplete).ToList();
        var packingDetails = packingOrders.SelectMany(o => o.OrderDetails ?? new List<OrderDetail>()).ToList();
        decimal packTarget = packingDetails.Sum(d => d.TargetQty);
        decimal packActual = packingDetails.Sum(d => d.ActualQty);

        // Progres Delivery: order yg statusnya PackingComplete/WaitingVerification/Approved
        var deliveryOrders = orders.Where(o =>
            o.Status == OrderStatus.PackingComplete ||
            o.Status == OrderStatus.WaitingVerification ||
            o.Status == OrderStatus.Approved).ToList();
        decimal delivTarget = deliveryOrders.SelectMany(o => o.OrderDetails ?? new List<OrderDetail>()).Sum(d => d.TargetQty);
        decimal delivActual = deliveryOrders.SelectMany(o => o.OrderDetails ?? new List<OrderDetail>()).Sum(d => d.ActualQty);

        // Delay Shopping: sudah H-2 dari OrderDate tapi shopping belum 100%
        var delayShoppingDetails = orders
            .Where(o => o.OrderDate.Date <= h2 &&
                        (o.Status == OrderStatus.WaitingShopping || o.Status == OrderStatus.Shopping))
            .SelectMany(o => o.OrderDetails ?? new List<OrderDetail>())
            .Where(d => d.ShoppingQty < d.TargetQty)
            .ToList();

        // Delay Packing: sudah H-1 dari OrderDate tapi packing belum 100%
        var delayPackingDetails = orders
            .Where(o => o.OrderDate.Date <= h1 &&
                        (o.Status == OrderStatus.ReadyPacking || o.Status == OrderStatus.Packing || o.Status == OrderStatus.ShoppingComplete))
            .SelectMany(o => o.OrderDetails ?? new List<OrderDetail>())
            .Where(d => d.ActualQty < d.TargetQty)
            .ToList();

        // Delay Delivery: sudah lewat PlanDeliveryDate tapi belum Finished
        var delayDeliveryOrders = orders.Where(o =>
            o.PlanDeliveryDate.HasValue &&
            o.PlanDeliveryDate.Value.Date < today &&
            o.Status != OrderStatus.Finished &&
            o.Status != OrderStatus.Cancelled).ToList();

        // Finished
        var finishedOrders = orders.Where(o => o.Status == OrderStatus.Finished).ToList();

        return new ShippingDashboardKpiDto
        {
            ProgresShoppingCount = shoppingOrders.Count,
            ProgresShoppingTarget = shopTarget,
            ProgresShoppingActual = shopActual,
            ProgresPackingCount = packingOrders.Count,
            ProgresPackingTarget = packTarget,
            ProgresPackingActual = packActual,
            ProgresDeliveryCount = deliveryOrders.Count,
            ProgresDeliveryTarget = delivTarget,
            ProgresDeliveryActual = delivActual,
            DelayShoppingCount = delayShoppingDetails.Count,
            DelayPackingCount = delayPackingDetails.Count,
            DelayDeliveryCount = delayDeliveryOrders.Count,
            FinishedCount = finishedOrders.Count
        };
    }

    public async Task<IEnumerable<ShoppingIndicatorRowDto>> GetShoppingProgressDataAsync()
    {
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.Status == OrderStatus.WaitingShopping || o.Status == OrderStatus.Shopping)
            .ToList();

        return orders.SelectMany(o => (o.OrderDetails ?? new List<OrderDetail>()).Select(d => new ShoppingIndicatorRowDto
        {
            OrderDetailId = d.Id,
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            ItemName = d.Item?.ItemName ?? "",
            CustPartNo = d.Item?.ItemCode ?? "",
            QtyOrder = d.TargetQty,
            QtyActShop = d.ShoppingQty,
            StockFG = "N/A"
        }));
    }

    public async Task<IEnumerable<PackingIndicatorRowDto>> GetPackingProgressDataAsync()
    {
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.Status == OrderStatus.ReadyPacking ||
                        o.Status == OrderStatus.Packing ||
                        o.Status == OrderStatus.ShoppingComplete)
            .ToList();

        return orders.SelectMany(o => (o.OrderDetails ?? new List<OrderDetail>()).Select(d => new PackingIndicatorRowDto
        {
            OrderDetailId = d.Id,
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            ItemName = d.Item?.ItemName ?? "",
            CustPartNo = d.Item?.ItemCode ?? "",
            QtyOrder = d.TargetQty,
            QtyPacked = d.ActualQty,
            QtyShopping = d.ShoppingQty
        }));
    }

    public async Task<IEnumerable<DeliveryIndicatorRowDto>> GetDeliveryProgressDataAsync()
    {
        var today = DateTime.Today;
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.Status == OrderStatus.PackingComplete ||
                        o.Status == OrderStatus.WaitingVerification ||
                        o.Status == OrderStatus.Approved)
            .ToList();

        return orders.Select(o => new DeliveryIndicatorRowDto
        {
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            OrderDate = o.OrderDate,
            PlanDeliveryDate = o.PlanDeliveryDate,
            TotalQty = (o.OrderDetails ?? new List<OrderDetail>()).Sum(d => d.TargetQty),
            PlanETD = o.PlanETD,
            ActualDeliveryDate = o.ActualDeliveryDate,
            DeliveryRemark = o.DeliveryRemark,
            ActualETD = o.ActualETD,
            IsDelay = o.PlanDeliveryDate.HasValue && o.PlanDeliveryDate.Value.Date < today
        });
    }

    public async Task<IEnumerable<ShoppingIndicatorRowDto>> GetDelayShoppingDataAsync()
    {
        var today = DateTime.Today;
        var h2 = today.AddDays(-2);
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.OrderDate.Date <= h2 &&
                        (o.Status == OrderStatus.WaitingShopping || o.Status == OrderStatus.Shopping))
            .ToList();

        return orders.SelectMany(o => (o.OrderDetails ?? new List<OrderDetail>())
            .Where(d => d.ShoppingQty < d.TargetQty)
            .Select(d => new ShoppingIndicatorRowDto
            {
                OrderDetailId = d.Id,
                OrderId = o.Id,
                OrderNo = o.OrderNo,
                ItemName = d.Item?.ItemName ?? "",
                CustPartNo = d.Item?.ItemCode ?? "",
                QtyOrder = d.TargetQty,
                QtyActShop = d.ShoppingQty,
                StockFG = "N/A"
            }));
    }

    public async Task<IEnumerable<PackingIndicatorRowDto>> GetDelayPackingDataAsync()
    {
        var today = DateTime.Today;
        var h1 = today.AddDays(-1);
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.OrderDate.Date <= h1 &&
                        (o.Status == OrderStatus.ReadyPacking ||
                         o.Status == OrderStatus.Packing ||
                         o.Status == OrderStatus.ShoppingComplete))
            .ToList();

        return orders.SelectMany(o => (o.OrderDetails ?? new List<OrderDetail>())
            .Where(d => d.ActualQty < d.TargetQty)
            .Select(d => new PackingIndicatorRowDto
            {
                OrderDetailId = d.Id,
                OrderId = o.Id,
                OrderNo = o.OrderNo,
                ItemName = d.Item?.ItemName ?? "",
                CustPartNo = d.Item?.ItemCode ?? "",
                QtyOrder = d.TargetQty,
                QtyPacked = d.ActualQty,
                QtyShopping = d.ShoppingQty
            }));
    }

    public async Task<IEnumerable<DeliveryIndicatorRowDto>> GetDelayDeliveryDataAsync()
    {
        var today = DateTime.Today;
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.PlanDeliveryDate.HasValue &&
                        o.PlanDeliveryDate.Value.Date < today &&
                        o.Status != OrderStatus.Finished &&
                        o.Status != OrderStatus.Cancelled)
            .ToList();

        return orders.Select(o => new DeliveryIndicatorRowDto
        {
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            OrderDate = o.OrderDate,
            PlanDeliveryDate = o.PlanDeliveryDate,
            TotalQty = (o.OrderDetails ?? new List<OrderDetail>()).Sum(d => d.TargetQty),
            PlanETD = o.PlanETD,
            ActualDeliveryDate = o.ActualDeliveryDate,
            DeliveryRemark = o.DeliveryRemark,
            ActualETD = o.ActualETD,
            IsDelay = true
        });
    }

    public async Task<IEnumerable<FinishedRowDto>> GetFinishedDataAsync()
    {
        var orders = (await _uow.Orders.GetAllWithDetailsAsync())
            .Where(o => o.Status == OrderStatus.Finished)
            .ToList();

        return orders.Select(o => new FinishedRowDto
        {
            OrderId = o.Id,
            OrderNo = o.OrderNo,
            OrderDate = o.OrderDate,
            PlanDeliveryDate = o.PlanDeliveryDate,
            TotalQty = (o.OrderDetails ?? new List<OrderDetail>()).Sum(d => d.TargetQty),
            PlanETD = o.PlanETD,
            ActualDeliveryDate = o.ActualDeliveryDate,
            DeliveryRemark = o.DeliveryRemark,
            ActualETD = o.ActualETD
        });
    }

    public async Task<(bool Success, string Message)> BulkDeleteOrderDetailsAsync(int[] ids)
    {
        if (ids == null || ids.Length == 0)
            return (false, "Tidak ada data yang dipilih.");

        int deleted = 0;
        foreach (var id in ids)
        {
            var detail = await _uow.OrderDetails.GetByIdAsync(id);
            if (detail != null)
            {
                detail.IsDeleted = true;
                detail.UpdatedAt = DateTime.Now;
                await _uow.OrderDetails.UpdateAsync(detail);
                deleted++;
            }
        }
        await _uow.SaveChangesAsync();
        return (true, $"{deleted} data berhasil dihapus.");
    }

    public async Task<(bool Success, string Message)> BulkDeleteOrdersAsync(int[] ids)
    {
        if (ids == null || ids.Length == 0)
            return (false, "Tidak ada data yang dipilih.");

        int deleted = 0;
        foreach (var id in ids)
        {
            var order = await _uow.Orders.GetByIdAsync(id);
            if (order != null)
            {
                order.IsDeleted = true;
                order.UpdatedAt = DateTime.Now;
                await _uow.Orders.UpdateAsync(order);
                deleted++;
            }
        }
        await _uow.SaveChangesAsync();
        return (true, $"{deleted} order berhasil dihapus.");
    }

    public async Task<(bool Success, string Message)> UpdateDeliveryAsync(int orderId, DateTime? planDeliveryDate, DateTime? actualDeliveryDate, string? planEtd, string? actualEtd, string? deliveryRemark)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");

        order.PlanDeliveryDate = planDeliveryDate;
        order.ActualDeliveryDate = actualDeliveryDate;
        order.PlanETD = planEtd;
        order.ActualETD = actualEtd;
        order.DeliveryRemark = deliveryRemark;
        order.UpdatedAt = DateTime.Now;

        await _uow.Orders.UpdateAsync(order);
        await _uow.SaveChangesAsync();
        return (true, "Data delivery berhasil diperbarui.");
    }
}
