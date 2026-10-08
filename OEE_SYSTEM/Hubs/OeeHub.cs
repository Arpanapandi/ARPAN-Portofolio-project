using Microsoft.AspNetCore.SignalR;

namespace OeeSystem.Hubs;

public class OeeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        Console.WriteLine($"✅ SignalR Client Connected: {Context.ConnectionId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Console.WriteLine($"❌ SignalR Client Disconnected: {Context.ConnectionId}");
        if (exception != null)
        {
            Console.WriteLine($"   Error: {exception.Message}");
        }
        await base.OnDisconnectedAsync(exception);
    }

    // Optional: Method untuk join group berdasarkan machineId (untuk broadcast selektif)
    public async Task JoinMachineGroup(int machineId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"machine_{machineId}");
    }

    public async Task LeaveMachineGroup(int machineId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"machine_{machineId}");
    }
}

