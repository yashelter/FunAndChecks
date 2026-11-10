using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Admins;

public interface IAdminService
{
    Task<List<AdminDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Админы с почтой и признаком супер-админа — для экрана управления.</summary>
    Task<List<AdminDetailsDto>> GetDetailsAsync(CancellationToken cancellationToken = default);

    /// <summary>Создаёт админа (и при необходимости супер-админа). Почта сразу подтверждена.</summary>
    Task<Guid> CreateAsync(CreateAdminRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет профиль и, если переданы, почту/пароль и роль супер-админа.
    /// Снять роль супер-админа с себя или с последнего супер-админа нельзя.
    /// </summary>
    Task UpdateAsync(Guid actingAdminId, Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken = default);

    /// <summary>Удаляет админа вместе с учётной записью. Нельзя удалить самого себя.</summary>
    Task DeleteAsync(Guid actingAdminId, Guid adminId, CancellationToken cancellationToken = default);
}
