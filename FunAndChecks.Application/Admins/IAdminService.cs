using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Admins;

public interface IAdminService
{
    Task<List<AdminDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Создаёт админа (и при необходимости супер-админа). Почта сразу подтверждена.</summary>
    Task<Guid> CreateAsync(CreateAdminRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid adminId, UpdateAdminRequest request, CancellationToken cancellationToken = default);

    /// <summary>Удаляет админа вместе с учётной записью. Нельзя удалить самого себя.</summary>
    Task DeleteAsync(Guid actingAdminId, Guid adminId, CancellationToken cancellationToken = default);
}
