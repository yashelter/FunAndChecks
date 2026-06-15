# Слой Infrastructure

Реализация всего, что объявлено интерфейсами в Application: доступ к БД (EF Core), учётные записи
(ASP.NET Identity), выпуск JWT, отправка почты (SMTP), резервное копирование, кэш, сидинг.

Исходники: [`FunAndChecks.Infrastructure`](../FunAndChecks.Infrastructure).
Регистрация — `services.AddInfrastructure(configuration)` в
[`DependencyInjection.cs`](../FunAndChecks.Infrastructure/DependencyInjection.cs).

## Персистентность (EF Core)

- [`ApplicationDbContext`](../FunAndChecks.Infrastructure/Persistence/ApplicationDbContext.cs) —
  наследует `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` и реализует
  `IApplicationDbContext`. Конфигурации сущностей подключаются через
  `ApplyConfigurationsFromAssembly`.
- СУБД — **PostgreSQL** (Npgsql). Строка подключения — `ConnectionStrings:DefaultConnection`.

### Конфигурации EF

Каталог [`Persistence/Configurations`](../FunAndChecks.Infrastructure/Persistence/Configurations).
Ключевые решения:

- **`User` — стратегия TPC** (`UseTpcMappingStrategy`): отдельные таблицы `Students` и `Admins`,
  без общей таблицы. `Id` задаётся приложением (`ValueGeneratedNever`) и совпадает с Id учётки.
- `Student`/`Admin` связаны с `ApplicationUser` один-к-одному по общему PK с каскадным удалением
  (профиль не живёт без аккаунта).
- `Student.Group` — `OnDelete(SetNull)`: при удалении группы студенты остаются без группы.
- `Subject` → `Tasks`/`GradeComponents` — каскадное удаление.
- `Submission`/`StudentGrade` → `Admin` — **`Restrict`**: нельзя удалить админа, за которым числится
  история проверок/оценок (защита авторства).
- `StudentGrade` — уникальный индекс на пару `(GradeComponentId, StudentId)`.
- `AdminSubjectAccess`/`AdminGroupAccess` — составной ключ `(AdminId, SubjectId|GroupId)`.

### Миграции

- Лежат в [`Persistence/Migrations`](../FunAndChecks.Infrastructure/Persistence/Migrations);
  применяются автоматически при старте приложения (вне окружения `Testing`).
- Генерация новой миграции (startup = сам Infrastructure, в нём есть
  [`DesignTimeDbContextFactory`](../FunAndChecks.Infrastructure/Persistence/DesignTimeDbContextFactory.cs)):

  ```bash
  dotnet ef migrations add <Name> \
    --project FunAndChecks.Infrastructure \
    --startup-project FunAndChecks.Infrastructure \
    --output-dir Persistence/Migrations
  ```

## Identity и учётные записи

- [`ApplicationUser : IdentityUser<Guid>`](../FunAndChecks.Infrastructure/Identity/ApplicationUser.cs) —
  тонкая учётка (логин/email/пароль/роли). Доменные данные — в профилях `Student`/`Admin` с тем же Id.
- [`IdentityService`](../FunAndChecks.Infrastructure/Identity/IdentityService.cs) реализует
  `IIdentityService`: создание/удаление аккаунта, проверка пароля с lockout, генерация и проверка
  кодов подтверждения почты и сброса пароля.
- Настройки Identity (`AddInfrastructure`): пароль от 6 символов, **уникальный email**,
  **обязательное подтверждение почты** (`SignIn.RequireConfirmedEmail`), lockout — 5 неудач /
  15 минут.
- **Коды подтверждения и сброса** — короткие 6-значные TOTP через `EmailTokenProvider`
  (`AddTokenProvider<EmailTokenProvider>`), без DataProtection. Покрытие безопасности — в
  [docs/security.md](security.md).

## JWT

- [`TokenService`](../FunAndChecks.Infrastructure/Identity/TokenService.cs) реализует `ITokenService`,
  выпускает HS512-токен с claim-ами id и ролей.
- Параметры — секция `Jwt` → [`JwtOptions`](../FunAndChecks.Infrastructure/Identity/JwtOptions.cs)
  (`Issuer`, `Audience`, `Key`, `AccessTokenMinutes`, `RefreshTokenDays`). `Key` для HS512 — не короче 64 байт.

## Почта (SMTP)

- [`SmtpEmailSender`](../FunAndChecks.Infrastructure/Email/SmtpEmailSender.cs) реализует `IEmailSender`
  на **MailKit** — подключается к собственному SMTP-серверу (Postfix на VDS), без внешних сервисов
  и лимитов.
- Настройки — секция `Smtp` → [`SmtpOptions`](../FunAndChecks.Infrastructure/Email/SmtpOptions.cs)
  (`Host`, `Port`, `UseStartTls`, `User`, `Password`, `FromEmail`, `FromName`).
- Если `Host` не задан (локальная разработка) — письма не отправляются, а логируются (код виден в логах).

## Резервное копирование

- [`PgDumpBackupService`](../FunAndChecks.Infrastructure/Backup/PgDumpBackupService.cs) реализует
  `IDatabaseBackupService`: запускает `pg_dump -Fc`, пароль передаёт через переменную окружения
  `PGPASSWORD` (не в аргументах).
- Настройки — секция `Backup` → [`BackupOptions`](../FunAndChecks.Infrastructure/Backup/BackupOptions.cs)
  (`Directory`, `PgDumpPath`).
- Эндпоинт — `POST /api/admin/backup` (только супер-админ).

## Кэш результатов

- [`ResultsCacheService`](../FunAndChecks.Infrastructure/Caching/ResultsCacheService.cs) —
  потокобезопасный (`ConcurrentDictionary`) singleton-кэш `SubjectResultsDto` по `subjectId`.
  Наполняется лениво, инвалидируется сервисами Application (см.
  [docs/application.md](application.md#кэш-результатов)).

## Сидинг

- [`DataSeeder`](../FunAndChecks.Infrastructure/Persistence/Seeding/DataSeeder.cs) при старте создаёт
  роли (`User`, `Admin`, `SuperAdmin`) и стартовых админов из секции `InitialAdmins`
  ([`AdminSeedModel`](../FunAndChecks.Infrastructure/Persistence/Seeding/AdminSeedModel.cs)).
  Источник `InitialAdmins` — только секреты (`secrets.json` / user-secrets / переменные окружения),
  см. [docs/configuration.md](configuration.md).

Дальше: [API](api.md) · [Безопасность](security.md) · [Конфигурация](configuration.md)
