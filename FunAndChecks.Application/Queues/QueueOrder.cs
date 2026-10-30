using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Queues;

/// <summary>
/// Порядок вызова ожидающих: сначала первый круг (больше баллов — раньше, при равенстве — кто раньше записался),
/// затем повторные круги по времени повторного вступления, сколько бы кругов ни было. Тот же порядок
/// у преподавателя и у студента (Frontend.Shared/UI/QueueStyles).
/// </summary>
public static class QueueOrder
{
    public static List<QueueParticipantDto> Waiting(IEnumerable<QueueParticipantDto> participants) => participants
        .Where(p => p.Status == QueueEntryStatus.Waiting)
        .OrderBy(p => p.Round > 1)
        .ThenByDescending(p => p.Round > 1 ? 0 : p.TotalPoints)
        .ThenBy(p => p.JoinedAt)
        .ToList();
}
