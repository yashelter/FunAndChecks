namespace FunAndChecks.Domain.Enums;

/// <summary>
/// Статус записи в очереди. Порядок значений влияет на сортировку!
/// </summary>
public enum QueueEntryStatus
{
    /// <summary>Проверяется прямо сейчас.</summary>
    Checking = 0,

    /// <summary>Ожидает своей очереди.</summary>
    Waiting = 1,

    /// <summary>Пропущен (был не готов, когда подошла очередь).</summary>
    Skipped = 2,

    /// <summary>Закончил сдачу.</summary>
    Finished = 3,
}
