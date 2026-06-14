using Frontend.Shared.Models;

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

    public static string StatusText(QueueEntryStatus status, string? adminName) => status switch
    {
        QueueEntryStatus.Waiting => "В очереди",
        QueueEntryStatus.Skipped => "Пропущен",
        QueueEntryStatus.Checking => $"Сдаёт{(adminName is null ? "" : $" ({adminName})")}",
        QueueEntryStatus.Finished => "Завершил",
        _ => "Неизвестно",
    };

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
