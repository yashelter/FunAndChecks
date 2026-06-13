namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Настройки видимости предмета для конкретного админа.
/// Запись существует, только если задействован хотя бы один флаг.
/// </summary>
public class AdminSubjectAccess
{
    public Guid AdminId { get; set; }
    public Admin Admin { get; set; } = null!;

    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    /// <summary>Глобальный запрет супер-админа: админ не видит предмет и не может с ним работать.</summary>
    public bool IsRestricted { get; set; }

    /// <summary>Локальная настройка самого админа: скрыть предмет из своих списков.</summary>
    public bool IsHidden { get; set; }
}
