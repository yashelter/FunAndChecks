using FunAndChecks.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Tests.Common;

/// <summary>
/// In-memory SQLite база для тестов: реальная реляционная семантика (FK, уникальные индексы),
/// но без внешней СУБД. Соединение держится открытым на время жизни объекта.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        // Production PostgreSQL lower() handles Cyrillic. SQLite's built-in lower()
        // only handles ASCII, so relational tests need the same Unicode semantics.
        _connection.CreateFunction<string?, string?>("lower", value => value?.ToLowerInvariant(), isDeterministic: true);
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureCreated();
    }

    public ApplicationDbContext NewContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
