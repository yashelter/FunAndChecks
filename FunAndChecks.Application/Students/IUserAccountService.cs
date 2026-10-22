namespace FunAndChecks.Application.Students;

public interface IUserAccountService
{
    Task<UserAccountPageDto> GetAsync(Guid actingAdminId, string? query, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid actingAdminId, Guid userId, CancellationToken cancellationToken = default);
}
