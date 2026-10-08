using SistemPacking.Web.Models;

namespace SistemPacking.Web.Helpers;

public static class OrderStatusHelper
{
    public static string GetLabel(OrderStatus status)
    {
        return status switch
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
            OrderStatus.Rejected => "Rejected",
            OrderStatus.Cancelled => "Cancelled",
            OrderStatus.Finished => "Finished",
            _ => status.ToString()
        };
    }

    public static string GetBadgeColor(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Draft => "secondary",
            OrderStatus.WaitingShopping => "warning",
            OrderStatus.Shopping => "info",
            OrderStatus.ShoppingComplete => "primary",
            OrderStatus.ReadyPacking => "info",
            OrderStatus.Packing => "info",
            OrderStatus.PackingComplete => "success",
            OrderStatus.WaitingVerification => "warning",
            OrderStatus.Approved => "success",
            OrderStatus.Rejected => "danger",
            OrderStatus.Cancelled => "danger",
            OrderStatus.Finished => "success",
            _ => "secondary"
        };
    }
}
