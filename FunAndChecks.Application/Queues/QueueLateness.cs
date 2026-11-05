namespace FunAndChecks.Application.Queues;

/// <summary>
/// Опоздание в очереди. Записавшийся на первый круг позже порога события (минуты после начала) — опоздавший:
/// он ждёт после пришедших вовремя, но перед повторными кругами. Преподаватель может снять опоздание.
/// Тот же порядок на клиенте — <c>QueueStyles.ByStatusThenRejoin</c>.
/// </summary>
public static class QueueLateness
{
    /// <summary>Самый большой порог — 4 часа: дольше пара не идёт.</summary>
    public const int MaxLateAfterMinutes = 240;

    /// <summary>
    /// На сколько минут от начала опоздал записавшийся, если он позже порога; иначе null.
    /// Повторные круги не опаздывают: они и так ждут после всех первых.
    /// </summary>
    public static int? LateMinutes(DateTime eventStart, int? lateAfterMinutes, int round, DateTime joinedAt)
    {
        if (lateAfterMinutes is not int threshold || round > 1)
            return null;
        var sinceStart = joinedAt - eventStart;
        return sinceStart > TimeSpan.FromMinutes(threshold) ? (int)Math.Ceiling(sinceStart.TotalMinutes) : null;
    }

    public static bool IsLate(QueueParticipantDto participant) =>
        participant.LateMinutes is not null && !participant.LateExcused;
}
