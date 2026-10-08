using SistemPacking.Web.Helpers;
using ClosedXML.Excel;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;

using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Data;

namespace SistemPacking.Web.Services;

public interface IOrderService
{
    Task<IEnumerable<OrderDto>> GetAllOrdersAsync();
    Task<IEnumerable<OrderFlattenedDto>> GetAllOrderDetailsFlattenedAsync();
    Task<OrderDto?> GetOrderAsync(int id);
    Task<IEnumerable<OrderDto>> GetOrdersByStatusAsync(OrderStatus status);
    Task<UploadResultDto> UploadOrderExcelAsync(Stream fileStream, string fileName, int uploadedById);
    Task<(bool Success, string Message)> ReleaseOrderAsync(int orderId, int leaderId);
    Task<(bool Success, string Message)> CancelOrderAsync(int orderId, int userId, string reason);
    Task<IEnumerable<OrderUploadHistory>> GetUploadHistoriesAsync();
}

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _uow;
    private readonly SistemPacking.Web.Data.ApplicationDbContext _db;

    public OrderService(IUnitOfWork uow, SistemPacking.Web.Data.ApplicationDbContext db)
    {
        _uow = uow;
        _db = db;
    }

    public async Task<IEnumerable<OrderDto>> GetAllOrdersAsync()
    {
        var orders = await _uow.Orders.GetAllWithDetailsAsync();
        return orders.Select(MapToDto);
    }

    public async Task<IEnumerable<OrderFlattenedDto>> GetAllOrderDetailsFlattenedAsync()
    {
        var orders = await _uow.Orders.GetAllWithDetailsAsync();
        var flattened = new List<OrderFlattenedDto>();
        foreach (var order in orders)
        {
            var statusLabel = order.Status switch
            {
                OrderStatus.Draft => "Draft",
                OrderStatus.WaitingShopping => "Waiting Shopping",
                OrderStatus.Shopping => "Shopping",
                OrderStatus.ShoppingComplete => "Shopping Complete",
                OrderStatus.ReadyPacking => "Ready Packing",
                OrderStatus.Packing => "Packing",
                OrderStatus.PackingComplete => "Packing Complete",
                OrderStatus.WaitingVerification => "Waiting Verification",
                OrderStatus.Approved => "Approved",
                OrderStatus.Cancelled => "Cancelled",
                _ => "Unknown"
            };
            var statusColor = order.Status switch
            {
                OrderStatus.Draft => "secondary",
                OrderStatus.WaitingShopping => "primary",
                OrderStatus.Shopping => "info",
                OrderStatus.ShoppingComplete => "success",
                OrderStatus.ReadyPacking => "primary",
                OrderStatus.Packing => "warning",
                OrderStatus.PackingComplete => "success",
                OrderStatus.WaitingVerification => "warning",
                OrderStatus.Approved => "success",
                OrderStatus.Cancelled => "danger",
                _ => "secondary"
            };

            foreach (var detail in order.OrderDetails)
            {
                flattened.Add(new OrderFlattenedDto
                {
                    OrderId = order.Id,
                    OrderDetailId = detail.Id,
                    OrderDate = order.OrderDate,
                    OrderNo = order.OrderNo,
                    CustomerName = order.Customer?.CustomerName ?? string.Empty,
                    Dock = detail.Item?.Dock,
                    PartNo = detail.Item?.ItemCode ?? string.Empty,
                    VIN = detail.Item?.VIN,
                    TargetQty = detail.TargetQty,
                    ActualQty = detail.ActualQty,
                    Status = order.Status,
                    StatusLabel = statusLabel,
                    StatusColor = statusColor
                });
            }
        }
        return flattened.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.OrderId).ToList();
    }

    public async Task<OrderDto?> GetOrderAsync(int id)
    {
        var order = await _uow.Orders.GetWithDetailsAsync(id);
        return order == null ? null : MapToDto(order);
    }

    public async Task<IEnumerable<OrderDto>> GetOrdersByStatusAsync(OrderStatus status)
    {
        var orders = await _uow.Orders.GetByStatusAsync(status);
        return orders.Select(MapToDto);
    }

    public async Task<UploadResultDto> UploadOrderExcelAsync(Stream fileStream, string fileName, int uploadedById)
    {
        var result = new UploadResultDto();
        var errors = new List<string>();

        try
        {
            using var wb = new XLWorkbook(fileStream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            // Expected columns: A=OrderNo, B=CustomerCode, C=OrderDate, D=ItemBarcode, E=TargetQty, F=DeliveryNote
            var history = new OrderUploadHistory
            {
                FileName = fileName,
                UploadedById = uploadedById,
                UploadedAt = DateTime.Now,
                TotalRows = lastRow - 1 // exclude header
            };
            await _uow.OrderUploadHistories.AddAsync(history);
            await _uow.SaveChangesAsync();

            var orderCache = new Dictionary<string, Order>();
            int successRows = 0;

            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    // A=Jadwal, B=OrderNo, C=Dock, D=CustomerName, E=PartNo, F=VIN, G=QTY
                    var orderDateStr = ws.Cell(row, 1).GetString().Trim();
                    var orderNo = ws.Cell(row, 2).GetString().Trim();
                    var dock = ws.Cell(row, 3).GetString().Trim();
                    var customerName = ws.Cell(row, 4).GetString().Trim();
                    var partNo = ws.Cell(row, 5).GetString().Trim();
                    var vin = ws.Cell(row, 6).GetString().Trim();
                    var targetQtyStr = ws.Cell(row, 7).GetString().Trim();

                    if (string.IsNullOrEmpty(orderNo) || string.IsNullOrEmpty(partNo) || string.IsNullOrEmpty(vin))
                    {
                        errors.Add($"Row {row}: OrderNo, PartNo, atau VIN kosong.");
                        continue;
                    }

                    // Parse date
                    if (!DateTime.TryParse(orderDateStr, out var orderDate))
                        orderDate = DateTime.Today;

                    // Parse qty
                    if (!decimal.TryParse(targetQtyStr, out var targetQty) || targetQty <= 0)
                    {
                        errors.Add($"Row {row}: Target Qty tidak valid.");
                        continue;
                    }

                    // Find customer
                    var customers = await _uow.Customers.GetAllAsync();
                    var customer = customers.FirstOrDefault(c => c.CustomerName.Equals(customerName, StringComparison.OrdinalIgnoreCase));
                    if (customer == null)
                    {
                        errors.Add($"Row {row}: Customer '{customerName}' tidak ditemukan.");
                        continue;
                    }

                    // Find item mapping by partNo (KanbanCode)
                    Item item = null;
                    var mapping = await _db.ItemMappings.Include(m => m.Item)
                        .FirstOrDefaultAsync(m => m.KanbanCode == partNo);

                    if (mapping != null)
                    {
                        item = mapping.Item;
                    }
                    else
                    {
                        // Fallback to ItemCode
                        item = await _db.Items.FirstOrDefaultAsync(i => i.ItemCode == partNo && !i.IsDeleted);
                    }

                    if (item == null)
                    {
                        errors.Add($"Row {row}: Part No '{partNo}' tidak ditemukan di Mapping maupun Master Item.");
                        continue;
                    }

                    // Cross validate VIN
                    if (!string.Equals(item.VIN, vin, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Row {row}: VIN '{vin}' tidak cocok dengan Part No '{partNo}'. VIN di Master Item adalah '{item.VIN}'.");
                        continue;
                    }

                    // Get or create order
                    if (!orderCache.TryGetValue(orderNo, out var order))
                    {
                        // Check if order already exists
                        var existingOrders = await _uow.Orders.GetAllWithDetailsAsync();
                        order = existingOrders.FirstOrDefault(o => o.OrderNo == orderNo) ?? new Order
                        {
                            OrderNo = orderNo,
                            CustomerId = customer.Id,
                            OrderDate = orderDate,
                            Status = OrderStatus.ReadyPacking,
                            UploadHistoryId = history.Id
                        };

                        if (order.Id == 0)
                            await _uow.Orders.AddAsync(order);

                        await _uow.SaveChangesAsync();
                        orderCache[orderNo] = order;
                    }

                    // Check if order detail already exists for this item
                    var existingDetail = await _db.OrderDetails.FirstOrDefaultAsync(d => d.OrderId == order.Id && d.ItemId == item.Id);
                    if (existingDetail != null)
                    {
                        existingDetail.TargetQty += targetQty;
                        _db.OrderDetails.Update(existingDetail);
                    }
                    else
                    {
                        var detail = new OrderDetail
                        {
                            OrderId = order.Id,
                            ItemId = item.Id,
                            TargetQty = targetQty,
                            ActualQty = 0
                        };
                        await _uow.OrderDetails.AddAsync(detail);
                    }
                    successRows++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row}: {ex.Message}");
                }
            }

            history.SuccessRows = successRows;
            history.FailedRows = errors.Count;
            history.ErrorLog = errors.Count > 0 ? string.Join("\n", errors) : null;
            await _uow.OrderUploadHistories.UpdateAsync(history);
            await _uow.SaveChangesAsync();

            result.Success = successRows > 0;
            result.Message = $"Upload selesai. {successRows} baris berhasil, {errors.Count} baris gagal.";
            result.TotalRows = lastRow - 1;
            result.SuccessRows = successRows;
            result.FailedRows = errors.Count;
            result.Errors = errors;
            result.HistoryId = history.Id;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Gagal membaca file: {ex.Message}";
        }

        return result;
    }

    public async Task<(bool Success, string Message)> ReleaseOrderAsync(int orderId, int leaderId)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");
        if (order.Status != OrderStatus.Draft) return (false, "Order bukan dalam status Draft.");

        order.Status = OrderStatus.ReadyPacking;
        order.ReleasedById = leaderId;
        order.ReleasedAt = DateTime.Now;
        await _uow.Orders.UpdateAsync(order);

        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = leaderId,
            Action = "RELEASE_ORDER",
            Description = $"Order {order.OrderNo} dirilis ke ReadyPacking",
            Module = "Order"
        });

        await _uow.SaveChangesAsync();
        return (true, $"Order {order.OrderNo} berhasil dirilis.");
    }

    public async Task<(bool Success, string Message)> CancelOrderAsync(int orderId, int userId, string reason)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");

        var allowedStatuses = new[] { OrderStatus.Draft, OrderStatus.ReadyPacking };
        if (!allowedStatuses.Contains(order.Status))
            return (false, "Order tidak dapat dibatalkan pada status ini.");

        order.Status = OrderStatus.Cancelled;
        order.CancelledById = userId;
        order.CancelledAt = DateTime.Now;
        order.CancelReason = reason;
        await _uow.Orders.UpdateAsync(order);

        await _uow.ActivityLogs.AddAsync(new ActivityLog
        {
            UserId = userId,
            Action = "CANCEL_ORDER",
            Description = $"Order {order.OrderNo} dibatalkan. Alasan: {reason}",
            Module = "Order"
        });

        await _uow.SaveChangesAsync();
        return (true, $"Order {order.OrderNo} berhasil dibatalkan.");
    }

    public async Task<IEnumerable<OrderUploadHistory>> GetUploadHistoriesAsync()
        => await _uow.OrderUploadHistories.GetAllAsync();

    private static OrderDto MapToDto(Order order)
    {
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

        return new OrderDto
        {
            Id = order.Id,
            OrderNo = order.OrderNo,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer?.CustomerName ?? "",
            OrderDate = order.OrderDate,
            Status = order.Status,
            StatusLabel = OrderStatusHelper.GetLabel(order.Status),
            StatusColor = OrderStatusHelper.GetBadgeColor(order.Status),
            TotalItems = details.Count,
            TotalTarget = details.Sum(d => d.TargetQty),
            TotalShopped = details.Sum(d => d.ShoppingQty),
            TotalActual = details.Sum(d => d.ActualQty),
            PackingProgress = details.Count > 0 && details.Sum(d => d.TargetQty) > 0
                ? Math.Min(100, details.Sum(d => d.ActualQty) / details.Sum(d => d.TargetQty) * 100) : 0,
            Notes = order.Notes,
            CreatedAt = order.CreatedAt,
            Details = details
        };
    }
}
