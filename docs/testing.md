# Тестирование

Тесты в проекте [`FunAndChecks.Tests`](../FunAndChecks.Tests): юнит-тесты use-case сервисов и
интеграционные тесты API.

## Стек

- **xUnit** — фреймворк тестов
- **FluentAssertions** — выразительные проверки
- **NSubstitute** — моки интерфейсов инфраструктуры (нотификаторы, кэш, отправка писем)
- **SQLite in-memory** — реальная реляционная семантика (FK, уникальные индексы) без внешней СУБД

## Юнит-тесты сервисов — `Application/`

Каждый сервис тестируется на реальном `ApplicationDbContext` поверх in-memory SQLite, а внешние
интерфейсы подменяются моками. Базу даёт
[`TestDatabase`](../FunAndChecks.Tests/Common/TestDatabase.cs) (соединение держится открытым на время
теста), наполнение — хелперы [`Seed`](../FunAndChecks.Tests/Common/Seed.cs).

Покрыто: `AuthService`, `StudentService`, `AdminService`, `AdminAccessService`, `GradeService`,
`SubmissionService`, `ResultsService`, `QueueService`.

```csharp
using var db = new TestDatabase();
await using var ctx = db.NewContext();
var group = ctx.Group();          // хелперы Seed
var subject = ctx.Subject();
await ctx.SaveChangesAsync();
// ... вызов сервиса и проверки
```

> Профили `Student`/`Admin` связаны FK с учётной записью по общему Id — `Seed.AddAccount`
> добавляет `ApplicationUser`, иначе под включёнными FK вставка профиля упадёт.

## Интеграционные тесты — `Integration/`

[`TestWebAppFactory`](../FunAndChecks.Tests/Integration/TestWebAppFactory.cs) поднимает реальное
API (`WebApplicationFactory<Program>`) в окружении `Testing`:

- провайдер EF/Npgsql снимается из DI и заменяется на SQLite in-memory;
- `IEmailSender` подменяется на [`CapturingEmailSender`](../FunAndChecks.Tests/Integration/CapturingEmailSender.cs),
  чтобы прочитать отправленный код прямо в тесте;
- JWT-настройки задаются через `UseSetting`;
- роли создаются после сборки хоста; в окружении `Testing` `Program` пропускает авто-миграции и
  сидинг (БД готовит сам фабричный хост).

[`AuthFlowTests`](../FunAndChecks.Tests/Integration/AuthFlowTests.cs) проверяет сквозной сценарий:
регистрация → получение кода из перехваченного письма → подтверждение почты → вход.

## Запуск

```bash
# все тесты решения
dotnet test FunAndChecks.slnx

# только тесты проекта
dotnet test FunAndChecks.Tests

# с подробным выводом
dotnet test FunAndChecks.slnx --logger "console;verbosity=detailed"
```

Тесты не требуют внешней БД, почты или сети — всё in-memory.
