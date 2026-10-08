using Microsoft.AspNetCore.SignalR;

namespace DeliveryControl.Hubs
{
    /// <summary>
    /// SignalR Hub untuk real-time updates delivery schedule
    /// Digunakan untuk broadcast perubahan status delivery ke semua client yang terhubung
    /// </summary>
    public class DeliveryHub : Hub
    {
        /// <summary>
        /// Event ketika client terhubung
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Event ketika client disconnect
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Broadcast delivery update ke semua client
        /// Dipanggil dari controller ketika ada perubahan status delivery
        /// </summary>
        public async Task NotifyDeliveryUpdate(string scheduleNumber, string action, string message)
        {
            await Clients.All.SendAsync("deliveryUpdated", new
            {
                ScheduleNumber = scheduleNumber,
                Action = action, // "arrival", "departure", "update"
                Message = message,
                Timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Broadcast statistics update ke semua client
        /// </summary>
        public async Task NotifyStatisticsUpdate()
        {
            await Clients.All.SendAsync("statisticsUpdated", DateTime.Now);
        }
    }
}

