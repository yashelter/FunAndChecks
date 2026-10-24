using FunAndChecks.Application.Backups;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>Восстановление БД (и вложений) из резервной копии в каталоге бэкапов. Реализуется в Infrastructure.</summary>
public interface IDatabaseRestoreService
{
    /// <summary>Состояние текущего или последнего восстановления.</summary>
    RestoreStatusDto GetStatus();

    /// <summary>
    /// Запускает восстановление из <paramref name="fileName"/> (.dump) в фоне и сразу возвращает статус.
    /// Одновременно выполняется только одно восстановление (иначе 409 <c>backup.restore_in_progress</c>).
    /// </summary>
    RestoreStatusDto Start(string fileName);
}
