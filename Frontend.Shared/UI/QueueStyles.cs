using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.UI;

/// <summary>Единые стили очереди (строки по статусу, заливка ФИО) — общие для админа и студента.</summary>
public static class QueueStyles
{
    /// <summary>Заливка фона строки по статусу. Для «в очереди» (Waiting) — без заливки.</summary>
    public static string RowStyle(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => "background-color: rgba(33,150,243,0.18);",
        QueueEntryStatus.Skipped => "background-color: rgba(244,67,54,0.16);",
        QueueEntryStatus.Finished => "background-color: rgba(76,175,80,0.18);",
        _ => string.Empty,
    };

    /// <summary>Заливка фона ФИО цветом студента (как в таблице результатов). null — без заливки.</summary>
    public static string NameStyle(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return "font-weight:600;";
        return $"background-color:{color};color:{ColorUtils.ContrastText(color)};" +
               "padding:2px 8px;border-radius:6px;display:inline-block;font-weight:600;";
    }

    public static string StatusText(QueueEntryStatus status, string? adminName, IStringLocalizer<AppStrings> loc) => status switch
    {
        QueueEntryStatus.Waiting => loc["Queue_StatusWaiting"],
        QueueEntryStatus.Skipped => loc["Queue_StatusSkipped"],
        QueueEntryStatus.Checking => adminName is null ? loc["Queue_StatusChecking"] : loc["Queue_StatusCheckingWithAdmin", adminName],
        QueueEntryStatus.Finished => loc["Queue_StatusFinished"],
        _ => loc["Queue_StatusUnknown"],
    };

    /// <summary>
    /// Первые ключи порядка очереди: статус, затем внутри ожидающих повторные круги — после всех первых кругов,
    /// между собой по времени повторного вступления (сколько бы кругов ни было). Дальше вызывающий добавляет
    /// свои ключи для первого круга (баллы, группа, время записи). Опоздавшие на первом круге — после пришедших
    /// вовремя, но перед повторными кругами. Тот же порядок на сервере — QueueOrder.
    /// </summary>
    public static IOrderedEnumerable<QueueParticipantDto> ByStatusThenRejoin(IEnumerable<QueueParticipantDto> participants) => participants
        .OrderBy(p => StatusOrder(p.Status))
        .ThenBy(p => WaitsAsRejoin(p))
        .ThenBy(p => p.Status == QueueEntryStatus.Waiting && p.IsLate)
        .ThenBy(p => WaitsAsRejoin(p) ? p.JoinedAt : DateTime.MinValue);

    private static bool WaitsAsRejoin(QueueParticipantDto p) => p.Status == QueueEntryStatus.Waiting && p.IsRejoin;

    /// <summary>Строка прошлого круга: у этого студента есть круг новее. Только история — работать с ней нельзя.</summary>
    public static bool IsPastRound(QueueParticipantDto participant, IEnumerable<QueueParticipantDto> all) =>
        all.Any(p => p.StudentId == participant.StudentId && p.Round > participant.Round);

    /// <summary>Последний круг каждого студента — один проход по списку вместо поиска на каждую строку.</summary>
    public static Dictionary<Guid, int> LatestRounds(IEnumerable<QueueParticipantDto> all)
    {
        var latest = new Dictionary<Guid, int>();
        foreach (var p in all)
            if (!latest.TryGetValue(p.StudentId, out var round) || p.Round > round)
                latest[p.StudentId] = p.Round;
        return latest;
    }

    public static bool IsPastRound(QueueParticipantDto participant, IReadOnlyDictionary<Guid, int> latestRounds) =>
        latestRounds.TryGetValue(participant.StudentId, out var latest) && latest > participant.Round;

    /// <summary>
    /// Кэш <see cref="LatestRounds"/> для одного списка: страницы заменяют список целиком при перечитывании,
    /// поэтому пересчёт нужен только при смене ссылки на список.
    /// </summary>
    public sealed class RoundIndex
    {
        private IReadOnlyCollection<QueueParticipantDto>? _source;
        private Dictionary<Guid, int> _latest = [];

        public bool IsPastRound(QueueParticipantDto participant, IReadOnlyCollection<QueueParticipantDto> all)
        {
            if (!ReferenceEquals(_source, all))
            {
                _latest = LatestRounds(all);
                _source = all;
            }
            return QueueStyles.IsPastRound(participant, _latest);
        }
    }

    /// <summary>Порядок статусов: сдают → в очереди → пропущенные → завершившие.</summary>
    public static int StatusOrder(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => 0,
        QueueEntryStatus.Waiting => 1,
        QueueEntryStatus.Skipped => 2,
        QueueEntryStatus.Finished => 3,
        _ => 4,
    };
}
