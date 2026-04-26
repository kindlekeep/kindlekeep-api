using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KindleKeep.Api.API.Hubs;

// Secures the entire Hub, rejecting unauthorized socket connections
[Authorize]
public class PulseHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
        {
            // Groups connections by user for targeted multicasting
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }
        
        await base.OnConnectedAsync();
    }
}