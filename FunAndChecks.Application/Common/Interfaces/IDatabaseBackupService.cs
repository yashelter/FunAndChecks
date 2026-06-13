namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>Создание резервной копии БД. Реализуется в Infrastructure (pg_dump).</summary>
public interface IDatabaseBackupService
{
    /// <summary>Создаёт дамп БД в настроенном каталоге и возвращает путь к файлу.</summary>
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);
}
