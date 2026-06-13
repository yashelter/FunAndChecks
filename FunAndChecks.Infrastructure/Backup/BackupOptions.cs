namespace FunAndChecks.Infrastructure.Backup;

/// <summary>Настройки резервного копирования. Секция "Backup".</summary>
public class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Каталог для дампов БД (создаётся при необходимости).</summary>
    public string Directory { get; set; } = "backups";

    /// <summary>Путь к утилите pg_dump (по умолчанию ищется в PATH).</summary>
    public string PgDumpPath { get; set; } = "pg_dump";
}
