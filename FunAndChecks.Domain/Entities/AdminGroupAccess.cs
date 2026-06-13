namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Настройки видимости группы для конкретного админа.
/// Запись существует, только если задействован хотя бы один флаг.
/// </summary>
public class AdminGroupAccess
{
    public Guid AdminId { get; set; }
    public Admin Admin { get; set; } = null!;

    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    /// <summary>Глобальный запрет супер-админа: админ не видит группу и не может с ней работать.</summary>
    public bool IsRestricted { get; set; }

    /// <summary>Локальная настройка самого админа: скрыть группу из своих списков.</summary>
    public bool IsHidden { get; set; }
}
