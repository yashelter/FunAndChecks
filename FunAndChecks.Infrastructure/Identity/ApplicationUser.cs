using Microsoft.AspNetCore.Identity;

namespace FunAndChecks.Infrastructure.Identity;

/// <summary>
/// Учётная запись (логин, email, пароль, роли). Только аутентификация —
/// доменные данные лежат в профилях Student/Admin с тем же Id.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>;
