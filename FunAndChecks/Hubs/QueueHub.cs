using FunAndChecks.Application.Queues;
using FunAndChecks.Common;
using FunAndChecks.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FunAndChecks.Hubs;

[Authorize]
public class QueueHub(IQueueService queueService) : Hub
{
    public static string GroupName(int eventId) => $"queue-{eventId}";

    /// <summary>Подписка клиента на обновления конкретной очереди.</summary>
    public async Task SubscribeToQueue(int eventId)
    {
        if (Context.User?.IsInRole(Roles.Admin) == true)
            await queueService.EnsureEventAllowedAsync(Context.User.GetUserId(), eventId, Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(eventId));
    }

    public async Task UnsubscribeFromQueue(int eventId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(eventId));
    }
}
