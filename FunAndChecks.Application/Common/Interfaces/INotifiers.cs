using FunAndChecks.Application.Forum;
using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Notifications;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Submissions;
using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Уведомления об изменениях в очереди (реализуется через SignalR в Presentation).
/// </summary>
public interface IQueueNotifier
{
    Task QueueEntryUpdatedAsync(QueueEntryUpdateDto update, CancellationToken cancellationToken = default);

    Task StudentColorUpdatedAsync(int eventId, StudentColorUpdateDto update, CancellationToken cancellationToken = default);
}

/// <summary>
/// Уведомления об изменениях результатов (реализуется через SignalR в Presentation).
/// </summary>
public interface IResultsNotifier
{
    Task ResultUpdatedAsync(int subjectId, ResultUpdateDto update, CancellationToken cancellationToken = default);

    Task GradeUpdatedAsync(int subjectId, GradeUpdateDto update, CancellationToken cancellationToken = default);

    Task StudentColorUpdatedAsync(int subjectId, StudentColorUpdateDto update, CancellationToken cancellationToken = default);
}

/// <summary>Персональные события пользователя (уведомления, появление опросов).</summary>
public interface IUserNotifier
{
    Task NotificationCreatedAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default);

    /// <summary>Подсказка клиенту перечитать список доступных опросов.</summary>
    Task SurveysChangedAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Живые обновления открытой темы форума.</summary>
public interface IForumNotifier
{
    Task TopicChangedAsync(ForumTopicEventDto update, CancellationToken cancellationToken = default);
}
