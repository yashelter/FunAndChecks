using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FunAndChecks.Infrastructure.Persistence;
using FunAndChecks.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FunAndChecks.Infrastructure.Backup;

/// <summary>Операции над БД при восстановлении; отделены от оркестрации, чтобы её можно было тестировать без PostgreSQL.</summary>
public interface IDatabaseRestoreTarget
{
    /// <summary>Проверяет дамп и возвращает id EF-миграций, записанных в нём.</summary>
    Task<IReadOnlyList<string>> ReadMigrationIdsAsync(string dumpPath, CancellationToken cancellationToken);

    /// <summary>Миграции, известные текущей версии приложения.</summary>
    IReadOnlyCollection<string> GetKnownMigrations();

    /// <summary>Закрывает пул соединений и обрывает остальные сеансы БД, чтобы они не держали блокировки.</summary>
    Task TerminateOtherSessionsAsync(CancellationToken cancellationToken);

    /// <summary>Атомарно заменяет содержимое БД содержимым дампа: при ошибке БД остаётся прежней.</summary>
    Task ReplaceDatabaseAsync(string dumpPath, CancellationToken cancellationToken);

    /// <summary>Догоняет схему миграциями (дамп мог быть снят старой версией) и повторяет сидинг.</summary>
    Task MigrateAndSeedAsync(CancellationToken cancellationToken);
}

/// <summary>Ошибка этапа восстановления с машинным кодом <c>restore.*</c> для UI.</summary>
public sealed class RestoreStepException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// Восстановление через pg_restore → psql. pg_restore превращает дамп в SQL-скрипт, psql выполняет его
/// одной транзакцией после пролога, удаляющего все объекты схемы public. Так из БД пропадают и таблицы,
/// которых нет в старом дампе (иначе последующие миграции упали бы на «already exists»), а любая ошибка
/// откатывает всё целиком: COMMIT отправляется, только когда pg_restore отдал дамп без ошибок.
/// </summary>
public sealed partial class PostgresRestoreTarget(
    IConfiguration configuration,
    IOptions<BackupOptions> options,
    IServiceScopeFactory scopes,
    ILogger<PostgresRestoreTarget> logger) : IDatabaseRestoreTarget
{
    private readonly BackupOptions _options = options.Value;

    /// <summary>Начало транзакции восстановления; NOTICE о каскадном удалении не засоряют текст ошибки.</summary>
    internal const string BeginTransactionSql = "BEGIN;\nSET LOCAL client_min_messages = warning;\n";

    /// <summary>Дописывается в конец скрипта, только если pg_restore выдал дамп целиком.</summary>
    internal const string CommitSql = "\nCOMMIT;\n";

    /// <summary>
    /// Пролог: удалить все пользовательские объекты схемы public (кроме объектов расширений).
    /// Объекты с внутренней зависимостью (deptype 'i', например identity-последовательности) напрямую
    /// удалить нельзя — они уходят вместе с владельцем.
    /// </summary>
    internal const string DropPublicObjectsSql = """
        DO $restore$
        DECLARE r record;
        BEGIN
            FOR r IN
                SELECT c.oid, c.relname, c.relkind FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')
                  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = c.oid AND d.deptype IN ('e', 'i'))
            LOOP
                EXECUTE format(CASE r.relkind
                        WHEN 'v' THEN 'DROP VIEW IF EXISTS public.%I CASCADE'
                        WHEN 'm' THEN 'DROP MATERIALIZED VIEW IF EXISTS public.%I CASCADE'
                        WHEN 'f' THEN 'DROP FOREIGN TABLE IF EXISTS public.%I CASCADE'
                        WHEN 'S' THEN 'DROP SEQUENCE IF EXISTS public.%I CASCADE'
                        ELSE 'DROP TABLE IF EXISTS public.%I CASCADE' END, r.relname);
            END LOOP;
            FOR r IN
                SELECT p.oid::regprocedure AS signature FROM pg_proc p
                JOIN pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'public'
                  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype IN ('e', 'i'))
            LOOP
                EXECUTE format('DROP ROUTINE IF EXISTS %s CASCADE', r.signature);
            END LOOP;
            FOR r IN
                SELECT t.typname FROM pg_type t
                JOIN pg_namespace n ON n.oid = t.typnamespace
                WHERE n.nspname = 'public' AND t.typtype IN ('e', 'd', 'c', 'r', 'm')
                  AND (t.typrelid = 0 OR EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind = 'c'))
                  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = t.oid AND d.deptype IN ('e', 'i'))
            LOOP
                EXECUTE format('DROP TYPE IF EXISTS public.%I CASCADE', r.typname);
            END LOOP;
        END
        $restore$;

        """;

    public async Task<IReadOnlyList<string>> ReadMigrationIdsAsync(string dumpPath, CancellationToken cancellationToken)
    {
        // --list читает оглавление: битый или чужой файл отсекается до любых изменений.
        var list = await RunToStringAsync(_options.PgRestorePath, ["--list", dumpPath], cancellationToken);
        if (list.ExitCode != 0)
            throw new RestoreStepException("restore.invalid_dump", $"pg_restore --list failed: {list.Error}");

        var data = await RunToStringAsync(_options.PgRestorePath,
            ["--data-only", "--table=__EFMigrationsHistory", "--file=-", dumpPath], cancellationToken);
        if (data.ExitCode != 0)
            throw new RestoreStepException("restore.invalid_dump", $"pg_restore could not read migrations: {data.Error}");
        return ParseMigrationIds(data.Output);
    }

    /// <summary>Строки данных из блока <c>COPY ... "__EFMigrationsHistory" ... FROM stdin;</c> до <c>\.</c>.</summary>
    internal static IReadOnlyList<string> ParseMigrationIds(string sql)
    {
        var ids = new List<string>();
        var inCopy = false;
        foreach (var rawLine in sql.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (!inCopy)
            {
                inCopy = line.StartsWith("COPY ", StringComparison.Ordinal) &&
                         line.Contains("__EFMigrationsHistory", StringComparison.Ordinal) &&
                         line.EndsWith("FROM stdin;", StringComparison.Ordinal);
                continue;
            }
            if (line == "\\.") break;
            var id = line.Split('\t')[0];
            if (id.Length > 0) ids.Add(id);
        }
        return ids;
    }

    public IReadOnlyCollection<string> GetKnownMigrations()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
    }

    public async Task ReplaceDatabaseAsync(string dumpPath, CancellationToken cancellationToken)
    {
        var listFile = Path.Combine(Path.GetTempPath(), $"funandchecks_restore_{Guid.NewGuid():N}.list");
        try
        {
            // Схему public создаёт сам PostgreSQL; её запись в оглавлении («CREATE SCHEMA public»)
            // пропускаем, иначе скрипт упадёт на уже существующей схеме.
            var list = await RunToStringAsync(_options.PgRestorePath, ["--list", dumpPath], cancellationToken);
            if (list.ExitCode != 0)
                throw new RestoreStepException("restore.invalid_dump", $"pg_restore --list failed: {list.Error}");
            var entries = list.Output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !PublicSchemaEntry().IsMatch(l));
            await File.WriteAllLinesAsync(listFile, entries, cancellationToken);

            await PipeRestoreAsync(dumpPath, listFile, cancellationToken);
        }
        finally
        {
            try { File.Delete(listFile); }
            catch { /* Best effort */ }
        }
    }

    private async Task PipeRestoreAsync(string dumpPath, string listFile, CancellationToken cancellationToken)
    {
        var csb = ConnectionString();
        using var restore = new Process { StartInfo = StartInfo(_options.PgRestorePath, csb,
            ["--no-owner", "--no-privileges", $"--use-list={listFile}", "--file=-", dumpPath]) };
        restore.StartInfo.RedirectStandardOutput = true;

        using var psql = new Process { StartInfo = StartInfo(_options.PsqlPath, csb,
        [
            "--no-psqlrc", "--quiet", "--set=ON_ERROR_STOP=1",
            $"--host={csb.Host}", $"--port={(csb.Port == 0 ? 5432 : csb.Port)}", $"--username={csb.Username}",
            "--no-password", $"--dbname={csb.Database}", "--file=-",
        ]) };
        psql.StartInfo.RedirectStandardInput = true;
        psql.StartInfo.RedirectStandardOutput = true;

        StartOrThrow(psql, _options.PsqlPath, "Backup:PsqlPath");
        try
        {
            StartOrThrow(restore, _options.PgRestorePath, "Backup:PgRestorePath");
            try
            {
                var restoreErrors = restore.StandardError.ReadToEndAsync(cancellationToken);
                var psqlErrors = psql.StandardError.ReadToEndAsync(cancellationToken);
                // Вывод psql (результаты SELECT set_config и т.п.) не нужен, но его надо вычитывать — иначе зависнет.
                var psqlOutput = psql.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancellationToken);

                // Транзакцию открываем сами, а COMMIT дописываем только после успешного pg_restore: с --single-transaction
                // psql фиксирует всё полученное, как только кончится вход, и оборванный дамп оставил бы БД пустой.
                var psqlExitedEarly = false;
                try
                {
                    var input = psql.StandardInput.BaseStream;
                    await input.WriteAsync(Encoding.UTF8.GetBytes(BeginTransactionSql + DropPublicObjectsSql), cancellationToken);
                    await restore.StandardOutput.BaseStream.CopyToAsync(input, cancellationToken);
                    await restore.WaitForExitAsync(cancellationToken);
                    if (restore.ExitCode == 0)
                        await input.WriteAsync(Encoding.UTF8.GetBytes(CommitSql), cancellationToken);
                    await input.FlushAsync(cancellationToken);
                }
                catch (IOException)
                {
                    // psql завершился раньше — причину покажет его код выхода и stderr. pg_restore больше никто
                    // не читает: без Kill он повис бы на записи в переполненный канал.
                    psqlExitedEarly = true;
                    Kill(restore);
                }
                finally
                {
                    // Конец входа без COMMIT: psql отключается, и сервер откатывает транзакцию.
                    try { psql.StandardInput.Close(); }
                    catch (IOException) { psqlExitedEarly = true; }
                }

                await restore.WaitForExitAsync(cancellationToken);
                await psql.WaitForExitAsync(cancellationToken);
                await psqlOutput;
                var restoreError = await restoreErrors;
                var psqlError = await psqlErrors;

                if (!psqlExitedEarly && restore.ExitCode != 0)
                {
                    logger.LogError("pg_restore exited with code {Code}: {Error}", restore.ExitCode, restoreError);
                    throw new RestoreStepException("restore.database_failed", $"pg_restore failed (exit code {restore.ExitCode}). {restoreError}");
                }
                if (psqlExitedEarly || psql.ExitCode != 0)
                {
                    logger.LogError("psql exited with code {Code}: {Error}", psql.ExitCode, psqlError);
                    throw new RestoreStepException("restore.database_failed", $"psql failed (exit code {psql.ExitCode}). {psqlError}");
                }
            }
            finally { Kill(restore); }
        }
        finally { Kill(psql); }
    }

    public async Task MigrateAndSeedAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection.ClearAllPools();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        // Сидер возвращает начальных супер-админов: после отката к старому дампу доступ не теряется.
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync();
    }

    public async Task TerminateOtherSessionsAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string GetConnectionString() =>
        configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection is not configured.");

    private NpgsqlConnectionStringBuilder ConnectionString() => new(GetConnectionString());

    private async Task<(int ExitCode, string Output, string Error)> RunToStringAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo(fileName, ConnectionString(), arguments) };
        process.StartInfo.RedirectStandardOutput = true;
        StartOrThrow(process, fileName, "Backup:PgRestorePath");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, await output, await error);
        }
        finally { Kill(process); }
    }

    private static ProcessStartInfo StartInfo(string fileName, NpgsqlConnectionStringBuilder csb, IEnumerable<string> arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        // Пароль — через PGPASSWORD, не в аргументах.
        psi.Environment["PGPASSWORD"] = csb.Password ?? string.Empty;
        return psi;
    }

    private void StartOrThrow(Process process, string fileName, string setting)
    {
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start {Tool}.", fileName);
            throw new RestoreStepException("restore.tools_unavailable",
                $"Could not start {fileName}. Make sure PostgreSQL client tools are installed and {setting} is correct.", ex);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch { /* Процесс не стартовал или уже завершился. */ }
    }

    /// <summary>Строка оглавления pg_restore вида «2; 2615 2200 SCHEMA - public pg_database_owner».</summary>
    [GeneratedRegex(@"^\s*\d+;\s*\d+\s+\d+\s+SCHEMA\s+-\s+public(\s|$)")]
    private static partial Regex PublicSchemaEntry();
}
