namespace FunAndChecks.Application.Admins;

/// <summary>
/// Видимость и ограничения предметов/групп для админов.
/// Глобальные ограничения (IsRestricted) выставляет супер-админ — они блокируют действия.
/// Локальные скрытия (IsHidden) админ задаёт сам — они лишь фильтруют его списки.
/// </summary>
public interface IAdminAccessService
{
    /// <summary>Текущие ограничения и скрытия админа.</summary>
    Task<AdminAccessDto> GetAccessAsync(Guid adminId, CancellationToken cancellationToken = default);

    /// <summary>Кидает ForbiddenException, если предмет глобально запрещён админу.</summary>
    Task EnsureSubjectAllowedAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default);

    Task<bool> IsSubjectRestrictedAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default);

    // --- Глобальные ограничения (только супер-админ) ---
    Task SetSubjectRestrictedAsync(Guid adminId, int subjectId, bool restricted, CancellationToken cancellationToken = default);
    Task SetGroupRestrictedAsync(Guid adminId, int groupId, bool restricted, CancellationToken cancellationToken = default);

    // --- Локальные скрытия (сам админ) ---
    Task SetSubjectHiddenAsync(Guid adminId, int subjectId, bool hidden, CancellationToken cancellationToken = default);
    Task SetGroupHiddenAsync(Guid adminId, int groupId, bool hidden, CancellationToken cancellationToken = default);
}
