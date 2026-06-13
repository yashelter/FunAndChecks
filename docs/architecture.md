# Архитектура

Бэкенд построен по принципам **Clean Architecture**: код разделён на четыре слоя-проекта,
зависимости направлены строго внутрь — к ядру. Внешние детали (БД, Identity, почта, веб)
вынесены на внешние слои и подключаются через интерфейсы, объявленные внутри.

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation (FunAndChecks)                │
│   Контроллеры · SignalR-хабы · Program/DI · middleware        │
│        реализует интерфейсы нотификаторов (SignalR)           │
└───────────────┬─────────────────────────────┬────────────────┘
                │                             │
                ▼                             ▼
┌───────────────────────────┐   ┌──────────────────────────────┐
│  Infrastructure           │   │      Application              │
│  EF Core · Identity · JWT  │──▶│  Use-case сервисы · DTO ·     │
│  SMTP · бэкап · кэш        │   │  валидаторы · интерфейсы      │
│  реализует интерфейсы      │   │  (IApplicationDbContext,      │
│  Application               │   │   IEmailSender, ITokenService…)│
└───────────────┬───────────┘   └──────────────┬───────────────┘
                │                             │
                └──────────────┬──────────────┘
                               ▼
                ┌──────────────────────────────┐
                │          Domain               │
                │  Сущности · перечисления ·    │
                │  бизнес-правила. Без зависимостей│
                └──────────────────────────────┘
```

## Направление зависимостей

- **Domain** ни от кого не зависит. Здесь только POCO-сущности и перечисления — никаких EF Core,
  Identity или сторонних библиотек.
- **Application** зависит только от Domain. Описывает сценарии использования (use-case сервисы),
  DTO, валидаторы и **интерфейсы** того, что должна предоставить инфраструктура
  (`IApplicationDbContext`, `IIdentityService`, `ITokenService`, `IEmailSender`,
  `IResultsCacheService`, `IDatabaseBackupService`, нотификаторы).
- **Infrastructure** зависит от Application (и через него от Domain). Реализует объявленные
  интерфейсы: EF Core `DbContext`, ASP.NET Identity, выпуск JWT, отправку почты, бэкап, кэш.
- **Presentation** зависит от Application и Infrastructure. Принимает HTTP/SignalR-запросы,
  вызывает сервисы Application, возвращает результат. Здесь же — composition root (регистрация DI
  в `Program.cs`) и реализация realtime-нотификаторов через SignalR.

Ключевой приём: и Infrastructure, и Presentation реализуют интерфейсы, **объявленные в Application**.
Application остаётся независимым от деталей — это позволяет подменять реализации (например, SQLite
в тестах вместо PostgreSQL) и держать бизнес-логику изолированной.

## Карта проектов

| Проект | Назначение | Зависит от |
|--------|-----------|-----------|
| `FunAndChecks.Domain` | Сущности, перечисления, константы ролей | — |
| `FunAndChecks.Application` | Use-case сервисы, DTO, валидаторы, интерфейсы инфраструктуры | Domain |
| `FunAndChecks.Infrastructure` | EF Core, Identity, JWT, SMTP, бэкап, кэш, сидинг, миграции | Application |
| `FunAndChecks` (Presentation) | Контроллеры, SignalR-хабы, middleware, DI, конфигурация | Application, Infrastructure, AdminUI |
| `FunAndChecks.Tests` | Юнит- и интеграционные тесты | все слои |
| `AdminUI` | Blazor WASM фронтенд (пока не адаптирован к новому API) | — |

## Регистрация зависимостей

Каждый слой предоставляет свой extension-метод для DI:

- `services.AddApplication()` — регистрирует use-case сервисы и валидаторы FluentValidation
  (см. [`FunAndChecks.Application/DependencyInjection.cs`](../FunAndChecks.Application/DependencyInjection.cs)).
- `services.AddInfrastructure(configuration)` — `DbContext`, Identity, токен-провайдеры, `ITokenService`,
  `IEmailSender` (SMTP), `IDatabaseBackupService`, кэш, сидер
  (см. [`FunAndChecks.Infrastructure/DependencyInjection.cs`](../FunAndChecks.Infrastructure/DependencyInjection.cs)).
- В `Program.cs` дополнительно регистрируются аутентификация/авторизация, CORS, rate limiting,
  SignalR и реализации нотификаторов.

## Сквозные решения

- **Обработка ошибок** — единый `ExceptionHandlingMiddleware` переводит исключения прикладного слоя
  (`NotFoundException`, `ConflictException`, `ForbiddenException`, `ValidationException`) в
  `ProblemDetails` с подходящим HTTP-кодом. См. [docs/api.md](api.md#обработка-ошибок).
- **Кэш результатов** — ленивый, инвалидируется по событиям (а не по таймеру). См.
  [docs/application.md](application.md#кэш-результатов).
- **Realtime** — изменения очереди и результатов рассылаются через SignalR. Интерфейсы нотификаторов
  объявлены в Application, реализованы в Presentation.

Дальше: [Domain](domain.md) · [Application](application.md) · [Infrastructure](infrastructure.md) · [API](api.md)
