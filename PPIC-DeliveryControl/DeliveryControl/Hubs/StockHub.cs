using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Hubs
{
    public class StockHub : Hub
    {
        public async Task NotifyStockUpdate()
        {
            await Clients.All.SendAsync("updateStock");
        }
    }
}
