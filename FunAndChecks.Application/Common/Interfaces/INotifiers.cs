using FunAndChecks.Application.Grades;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Submissions;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Уведомления об изменениях в очереди (реализуется через SignalR в Presentation).
/// </summary>
public interface IQueueNotifier
{
    Task QueueEntryUpdatedAsync(QueueEntryUpdateDto update, CancellationToken cancellationToken = default);
}

/// <summary>
/// Уведомления об изменениях результатов (реализуется через SignalR в Presentation).
/// </summary>
public interface IResultsNotifier
{
    Task ResultUpdatedAsync(int subjectId, ResultUpdateDto update, CancellationToken cancellationToken = default);

    Task GradeUpdatedAsync(int subjectId, GradeUpdateDto update, CancellationToken cancellationToken = default);
}
