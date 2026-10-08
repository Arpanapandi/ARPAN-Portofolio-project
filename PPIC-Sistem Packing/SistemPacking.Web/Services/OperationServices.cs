using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;

namespace SistemPacking.Web.Services;

public interface IShoppingService
{
    Task<ScanResultDto> ScanBarcodeAsync(string barcode, int operatorId, int? shiftId, string moduleContext = "Shopping");
    Task<IEnumerable<ShoppingLog>> GetShoppingLogsAsync(int orderId);
    Task<bool> CheckAndCompleteShoppingAsync(int orderId);
}

public class ShoppingService : IShoppingService
{
    private readonly IUnitOfWork _uow;
    private readonly IDeliveryControlIntegrationService _deliveryIntegration;

    public ShoppingService(IUnitOfWork uow, IDeliveryControlIntegrationService deliveryIntegration)
    {
        _uow = uow;
        _deliveryIntegration = deliveryIntegration;
    }

    public async Task<ScanResultDto> ScanBarcodeAsync(string barcode, int operatorId, int? shiftId, string moduleContext = "Shopping")
    {
        // Find item by barcode
        var item = await _uow.Items.GetByBarcodeAsync(barcode);
        if (item == null)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                Category = "Unknown",
                Module = moduleContext,
                ErrorMessage = "Barcode tidak ditemukan dalam master item",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Barcode '{barcode}' tidak ditemukan dalam master item." };
        }

        decimal qty = 1;

        // INTEGRASI: Kurangi stok FG di Delivery Control DULU, baru update DB lokal
        var operatorUser = await _uow.Users.GetByIdAsync(operatorId);
        var apiRequest = new DeliveryControlScanRequest
        {
            tag = item.VIN ?? "",
            label = barcode,
            kanban = item.ItemCode,
            user = operatorUser?.FullName ?? operatorUser?.Username ?? "Unknown"
        };
        
        var apiResponse = await _deliveryIntegration.SendShoppingScanAsync(apiRequest);

        if (apiResponse == null || !apiResponse.success)
        {
            // API Gagal -> Jangan simpan ShoppingLog, catat ke NG Log
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                ItemCode = item.ItemCode,
                Category = item.Category,
                Module = moduleContext,
                ErrorMessage = $"Delivery Control API Failed: {apiResponse?.message ?? "Unknown Error"}",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Gagal memotong stok Delivery Control: {apiResponse?.message}" };
        }

        // Save shopping log
        var log = new ShoppingLog
        {
            OrderDetailId = null,
            ItemId = item.Id, // Link ke master item
            ScannedBarcode = barcode,
            ScannedQty = qty,
            OperatorId = operatorId,
            ShiftId = shiftId,
            IsValid = true,
            InvalidReason = null,
            ScannedAt = DateTime.Now
        };
        await _uow.ShoppingLogs.AddAsync(log);

        // Update physical stock (+1 for shopping)
        item.ActQty += qty;
        await _uow.Items.UpdateAsync(item);

        await _uow.SaveChangesAsync();

        return new ScanResultDto
        {
            Success = true,
            Message = "✅ Scan Shopping berhasil",
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Barcode = barcode,
            UOM = item.UOM,
            TargetQty = 0,
            ScannedQty = qty,
            Remaining = 0,
            SpisImagePath = item.SpisImagePath,
            SppsImagePath = item.SppsImagePath,
            Point1 = item.Point1,
            Point2 = item.Point2,
            Point3 = item.Point3,
            Point4 = item.Point4,
            Point5 = item.Point5,
            VIN = item.VIN,
            Dock = item.Dock,
            CustomerName = item.Customer?.CustomerName,
            OrderDetailId = 0,
            OrderId = 0,
            IsOverQty = false,
            IsComplete = false
        };
    }

    public async Task<IEnumerable<ShoppingLog>> GetShoppingLogsAsync(int orderId)
    {
        var order = await _uow.Orders.GetWithDetailsAsync(orderId);
        if (order?.OrderDetails == null) return Enumerable.Empty<ShoppingLog>();

        var logs = new List<ShoppingLog>();
        foreach (var detail in order.OrderDetails)
        {
            var detailLogs = await _uow.ShoppingLogs.GetByOrderDetailAsync(detail.Id);
            logs.AddRange(detailLogs);
        }
        return logs.OrderByDescending(l => l.ScannedAt);
    }

    public async Task<bool> CheckAndCompleteShoppingAsync(int orderId)
    {
        var order = await _uow.Orders.GetWithDetailsAsync(orderId);
        if (order == null || order.Status != OrderStatus.Shopping) return false;

        bool allShopped = order.OrderDetails?.All(od => od.ShoppingQty >= od.TargetQty) ?? false;

        if (allShopped)
        {
            order.Status = OrderStatus.ShoppingComplete;
            await _uow.Orders.UpdateAsync(order);
            await _uow.SaveChangesAsync();
            return true;
        }
        return false;
    }
}

public interface IPackingService
{
    Task<ScanResultDto> ScanBarcodeAsync(string barcode, int operatorId, int? shiftId, string moduleContext = "Packing");
    Task<ScanResultDto> ScanKanbanLabelAsync(string kanbanItemCode, string labelVin, int operatorId, int? shiftId, string moduleContext = "Packing");
    Task<OrderDto?> GetPackingProgressAsync(int orderId);
    Task<(bool Success, string Message)> SetReadyPackingAsync(int orderId, int userId);
}

public class PackingService : IPackingService
{
    private readonly IUnitOfWork _uow;

    public PackingService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<ScanResultDto> ScanBarcodeAsync(string barcode, int operatorId, int? shiftId, string moduleContext = "Packing")
    {
        // Find item by barcode
        var item = await _uow.Items.GetByBarcodeAsync(barcode);
        if (item == null)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                Category = "Unknown",
                Module = moduleContext,
                ErrorMessage = "Barcode tidak ditemukan dalam master item",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Barcode '{barcode}' tidak ditemukan." };
        }

        // Cek kesesuaian kategori item dengan module yang sedang dibuka
        if ((moduleContext == "SparePart" || moduleContext == "Export") && item.Category != moduleContext)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                ItemCode = item.ItemCode,
                Category = item.Category,
                Module = moduleContext,
                ErrorMessage = $"Salah menu! Item ini adalah {item.Category} bukan {moduleContext}",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Salah menu scan! Item '{item.ItemName}' adalah tipe {item.Category}." };
        }

        // Find active orders for packing
        var readyOrders = await _uow.Orders.GetByStatusAsync(OrderStatus.ReadyPacking);
        var packingOrders = await _uow.Orders.GetByStatusAsync(OrderStatus.Packing);
        var activeOrders = readyOrders.Concat(packingOrders).OrderBy(o => o.CreatedAt).ToList();

        Order? targetOrder = null;
        OrderDetail? targetDetail = null;

        foreach (var order in activeOrders)
        {
            var detail = order.OrderDetails?.FirstOrDefault(od => od.ItemId == item.Id && od.ActualQty < od.TargetQty);
            if (detail != null)
            {
                targetOrder = order;
                targetDetail = detail;
                break;
            }
        }

        if (targetOrder == null || targetDetail == null)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                ItemCode = item.ItemCode,
                Category = item.Category,
                Module = moduleContext,
                ErrorMessage = "Item tidak dibutuhkan pada order packing",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Item '{item.ItemName}' tidak dibutuhkan pada order packing manapun saat ini." };
        }

        decimal qty = 1;

        if (targetOrder.Status == OrderStatus.ReadyPacking)
        {
            // Auto start packing
            targetOrder.Status = OrderStatus.Packing;
            await _uow.Orders.UpdateAsync(targetOrder);
            await _uow.SaveChangesAsync();
        }

        // Check over qty (should not happen because we filtered by ActualQty < TargetQty, but just in case)
        var currentActual = targetDetail.ActualQty;
        bool isOverQty = currentActual + qty > targetDetail.TargetQty;

        if (isOverQty)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = barcode,
                ItemCode = item.ItemCode,
                Category = item.Category,
                Module = moduleContext,
                ErrorMessage = $"Qty melebihi target! Actual: {currentActual}, Target: {targetDetail.TargetQty}",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto
            {
                Success = false,
                Message = $"⚠️ Qty sudah melebihi target! Actual: {currentActual}, Target: {targetDetail.TargetQty}",
                IsOverQty = true
            };
        }

        // Save packing log
        var log = new PackingLog
        {
            OrderDetailId = targetDetail.Id,
            ScannedBarcode = barcode,
            PackedQty = qty,
            OperatorId = operatorId,
            ShiftId = shiftId,
            PackedAt = DateTime.Now
        };
        await _uow.PackingLogs.AddAsync(log);

        // UPDATE ACTUAL QTY
        targetDetail.ActualQty = currentActual + qty;
        await _uow.OrderDetails.UpdateAsync(targetDetail);

        // Update physical stock (-1 for packing)
        item.ActQty -= qty;
        await _uow.Items.UpdateAsync(item);

        await _uow.SaveChangesAsync();

        // Check if all items packed
        // Re-fetch order with details to check total completeness
        var fullOrder = await _uow.Orders.GetWithDetailsAsync(targetOrder.Id);
        var allPacked = fullOrder?.OrderDetails?.All(od => od.ActualQty >= od.TargetQty) ?? false;
        
        if (allPacked && fullOrder != null)
        {
            fullOrder.Status = OrderStatus.PackingComplete;
            await _uow.Orders.UpdateAsync(fullOrder);
            await _uow.SaveChangesAsync();
        }

        return new ScanResultDto
        {
            Success = true,
            Message = allPacked ? "🎉 Semua item selesai dipacking!" : "✅ Scan packing berhasil",
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            Barcode = barcode,
            UOM = item.UOM,
            TargetQty = targetDetail.TargetQty,
            ActualQty = currentActual + qty,
            Remaining = Math.Max(0, targetDetail.TargetQty - currentActual - qty),
            SpisImagePath = item.SpisImagePath,
            SppsImagePath = item.SppsImagePath,
            Point1 = item.Point1,
            Point2 = item.Point2,
            Point3 = item.Point3,
            Point4 = item.Point4,
            Point5 = item.Point5,
            VIN = item.VIN,
            Dock = item.Dock,
            CustomerName = item.Customer?.CustomerName,
            OrderDetailId = targetDetail.Id,
            OrderId = targetOrder.Id,
            IsOverQty = false,
            IsComplete = allPacked
        };
    }

    public async Task<ScanResultDto> ScanKanbanLabelAsync(string kanbanItemCode, string labelVin, int operatorId, int? shiftId, string moduleContext = "Packing")
    {
        // Find item by Kanban (ItemCode)
        var item = await _uow.Items.GetByCodeAsync(kanbanItemCode);
        if (item == null)
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = kanbanItemCode,
                Category = "Unknown",
                Module = moduleContext,
                ErrorMessage = "Kanban Part No tidak ditemukan",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Kanban Part No '{kanbanItemCode}' tidak ditemukan dalam database." };
        }

        // Validate Label (VIN)
        if (string.IsNullOrWhiteSpace(item.VIN) || !item.VIN.Equals(labelVin, StringComparison.OrdinalIgnoreCase))
        {
            await _uow.ScanNgLogs.AddAsync(new ScanNgLog {
                ScannedBarcode = labelVin,
                ItemCode = item.ItemCode,
                Category = item.Category,
                Module = moduleContext,
                ErrorMessage = $"Mismatch! Label VIN '{labelVin}' tidak cocok dengan Part No '{kanbanItemCode}' (Expected: '{item.VIN}')",
                OperatorId = operatorId,
                ShiftId = shiftId
            });
            await _uow.SaveChangesAsync();
            return new ScanResultDto { Success = false, Message = $"Barcode Label (VIN) tidak cocok dengan Kanban Part No '{kanbanItemCode}'." };
        }

        // Proceed to scan using the item's barcode (or itemCode) to trigger the same packing logic.
        // We will just call the existing ScanBarcodeAsync using the item's Barcode.
        // Usually, item.Barcode might be different or same as ItemCode. If item.Barcode is empty, fallback to itemCode.
        string scanTarget = !string.IsNullOrEmpty(item.Barcode) ? item.Barcode : item.ItemCode;
        return await ScanBarcodeAsync(scanTarget, operatorId, shiftId, moduleContext);
    }

    public async Task<OrderDto?> GetPackingProgressAsync(int orderId)
    {
        var order = await _uow.Orders.GetWithDetailsAsync(orderId);
        if (order == null) return null;

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
            CustomerName = order.Customer?.CustomerName ?? "",
            Status = order.Status,
            StatusLabel = Helpers.OrderStatusHelper.GetLabel(order.Status),
            StatusColor = Helpers.OrderStatusHelper.GetBadgeColor(order.Status),
            TotalItems = details.Count,
            TotalTarget = details.Sum(d => d.TargetQty),
            TotalActual = details.Sum(d => d.ActualQty),
            PackingProgress = details.Count > 0 && details.Sum(d => d.TargetQty) > 0
                ? Math.Min(100, details.Sum(d => d.ActualQty) / details.Sum(d => d.TargetQty) * 100) : 0,
            Details = details
        };
    }

    public async Task<(bool Success, string Message)> SetReadyPackingAsync(int orderId, int userId)
    {
        var order = await _uow.Orders.GetByIdAsync(orderId);
        if (order == null) return (false, "Order tidak ditemukan.");
        if (order.Status != OrderStatus.ShoppingComplete)
            return (false, "Order belum selesai shopping.");

        order.Status = OrderStatus.ReadyPacking;
        await _uow.Orders.UpdateAsync(order);
        await _uow.SaveChangesAsync();
        return (true, "Order siap untuk packing.");
    }
}
