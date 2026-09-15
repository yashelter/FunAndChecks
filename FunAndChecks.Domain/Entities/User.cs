namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Базовая доменная сущность пользователя.
/// Учётные данные (email, пароль, роли) — забота Infrastructure (Identity),
/// связь осуществляется по общему первичному ключу <see cref="Id"/>.
/// </summary>
public abstract class User
{
    public Guid Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }

    public string FullName => $"{LastName} {FirstName}";
}
