using FunAndChecks.Application.Forum;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FunAndChecks.Hubs;

[Authorize]
public class ForumHub(IForumAccess access) : Hub
{
    public static string GroupName(int topicId) => $"forum-topic-{topicId}";

    /// <summary>Подписка на открытую тему; чужую тему не выдаём даже по событиям.</summary>
    public async Task SubscribeToTopic(int topicId)
    {
        var viewer = await access.GetViewerAsync(Context.User!.GetUserId(), Context.ConnectionAborted);
        await access.RequireTopicAsync(viewer, topicId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(topicId));
    }

    public async Task UnsubscribeFromTopic(int topicId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(topicId));
    }
}
