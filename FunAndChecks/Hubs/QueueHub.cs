using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FunAndChecks.Hubs;

[Authorize]
public class QueueHub : Hub
{
    public static string GroupName(int eventId) => $"queue-{eventId}";

    /// <summary>Подписка клиента на обновления конкретной очереди.</summary>
    public async Task SubscribeToQueue(int eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(eventId));
    }

    public async Task UnsubscribeFromQueue(int eventId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(eventId));
    }
}
