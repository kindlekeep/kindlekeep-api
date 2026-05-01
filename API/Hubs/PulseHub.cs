using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace KindleKeep.Api.API.Hubs;

public class PulseHub : Hub
{
    public async Task SubscribeToMonitor(string monitorId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, monitorId);
    }

    public async Task UnsubscribeFromMonitor(string monitorId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, monitorId);
    }
}