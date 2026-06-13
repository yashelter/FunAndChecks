namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Выпуск токенов доступа. Реализуется в Infrastructure (JWT).
/// </summary>
public interface ITokenService
{
    Task<string> CreateTokenAsync(Guid userId);
}
