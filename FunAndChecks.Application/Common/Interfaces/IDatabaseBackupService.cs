using FunAndChecks.Application.Backups;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>Создание резервной копии БД. Реализуется в Infrastructure (pg_dump).</summary>
public interface IDatabaseBackupService
{
    /// <summary>Создаёт дамп БД в настроенном каталоге и возвращает путь к файлу.</summary>
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Возвращает завершённые резервные копии, начиная с самой новой.</summary>
    Task<IReadOnlyList<BackupFileDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Открывает выбранную резервную копию для скачивания.</summary>
    Task<Stream> OpenReadAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>Удаляет выбранную резервную копию из настроенного каталога.</summary>
    Task DeleteAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет загруженный дамп (формат custom) и необязательный архив вложений в каталог бэкапов
    /// под новым именем; возвращает описание сохранённого дампа.
    /// </summary>
    Task<BackupFileDto> ImportAsync(Stream dump, Stream? attachmentsArchive, CancellationToken cancellationToken = default);
}
