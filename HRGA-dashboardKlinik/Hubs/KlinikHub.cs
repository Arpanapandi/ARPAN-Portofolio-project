using Microsoft.AspNetCore.SignalR;

namespace dashboardKlinik.Hubs
{
    public class KlinikHub : Hub
    {
        private readonly ILogger<KlinikHub> _logger;

        public KlinikHub(ILogger<KlinikHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Connected", new
            {
                connectionId = Context.ConnectionId,
                message = "Terhubung ke real-time sync",
                timestamp = DateTime.Now
            });
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Client can request a manual sync trigger
        /// </summary>
        public async Task RequestSync()
        {
            _logger.LogInformation("Manual sync requested by client: {ConnectionId}", Context.ConnectionId);
            // The background service will pick this up, but we notify other clients
            await Clients.All.SendAsync("SyncStarted", new
            {
                message = "Sinkronisasi dimulai...",
                triggeredBy = Context.ConnectionId,
                timestamp = DateTime.Now
            });
        }
    }
}
