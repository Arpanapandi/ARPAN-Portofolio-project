using Microsoft.AspNetCore.SignalR;

namespace SistemPacking.Web.Hubs;

public class OrderHub : Hub
{
    public async Task JoinOrderGroup(string orderId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"order-{orderId}");

    public async Task LeaveOrderGroup(string orderId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order-{orderId}");

    public static async Task NotifyOrderStatusChanged(IHubContext<OrderHub> hub, int orderId, string status, string statusLabel)
        => await hub.Clients.Group($"order-{orderId}").SendAsync("OrderStatusChanged", new { orderId, status, statusLabel });

    public static async Task NotifyPackingProgress(IHubContext<OrderHub> hub, int orderId, decimal progress, int detailId, decimal actual, decimal target)
        => await hub.Clients.Group($"order-{orderId}").SendAsync("PackingProgressUpdated", new { orderId, progress, detailId, actual, target });

    public static async Task NotifyShoppingLog(IHubContext<OrderHub> hub, int orderId, object logData)
        => await hub.Clients.Group($"order-{orderId}").SendAsync("ShoppingLogAdded", logData);
}

public class DashboardHub : Hub
{
    public async Task JoinDashboard()
        => await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");

    public static async Task NotifyKpiUpdated(IHubContext<DashboardHub> hub, object kpiData)
        => await hub.Clients.Group("dashboard").SendAsync("KPIRefreshed", kpiData);
}
