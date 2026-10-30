using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Forum;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Students;
using FunAndChecks.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FunAndChecks.Realtime;

public class QueueNotifier(IHubContext<QueueHub> hubContext) : IQueueNotifier
{
    public Task QueueEntryUpdatedAsync(QueueEntryUpdateDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(QueueHub.GroupName(update.EventId))
            .SendAsync("QueueEntryUpdated", update, cancellationToken);

    public Task StudentColorUpdatedAsync(int eventId, StudentColorUpdateDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(QueueHub.GroupName(eventId))
            .SendAsync("StudentColorUpdated", update, cancellationToken);

    public Task QueueChangedAsync(int eventId, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(QueueHub.GroupName(eventId))
            .SendAsync("QueueChanged", eventId, cancellationToken);
}

public class ResultsNotifier(IHubContext<ResultsHub> hubContext) : IResultsNotifier
{
    public Task ResultUpdatedAsync(int subjectId, ResultUpdateDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(ResultsHub.GroupName(subjectId))
            .SendAsync("ResultUpdated", update, cancellationToken);

    public Task GradeUpdatedAsync(int subjectId, GradeUpdateDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(ResultsHub.GroupName(subjectId))
            .SendAsync("GradeUpdated", update, cancellationToken);

    public Task StudentColorUpdatedAsync(int subjectId, StudentColorUpdateDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .Group(ResultsHub.GroupName(subjectId))
            .SendAsync("StudentColorUpdated", update, cancellationToken);
}

public class UserNotifier(IHubContext<NotificationsHub> hubContext) : IUserNotifier
{
    public Task NotificationCreatedAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default) =>
        hubContext.Clients.User(userId.ToString()).SendAsync("NotificationCreated", notification, cancellationToken);

    public Task SurveysChangedAsync(Guid userId, CancellationToken cancellationToken = default) =>
        hubContext.Clients.User(userId.ToString()).SendAsync("SurveysChanged", cancellationToken);
}

public class ForumNotifier(IHubContext<ForumHub> hubContext) : IForumNotifier
{
    public Task TopicChangedAsync(ForumTopicEventDto update, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(ForumHub.GroupName(update.TopicId)).SendAsync("TopicChanged", update, cancellationToken);
}
