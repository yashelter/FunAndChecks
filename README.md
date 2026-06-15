# FunAndChecks

Сервис учёта сдач заданий и виртуальных очередей для учебных групп. Преподаватели (админы)
проверяют работы студентов, выставляют баллы за задания и глобальные оценки (билет, курсовая),
управляют очередями на сдачу; студенты регистрируются, отслеживают свой прогресс и записываются
в очереди. Результаты по предмету собираются в сводную таблицу с обновлением в реальном времени.

Бэкенд построен по принципам **Clean Architecture** на **.NET 10 / ASP.NET Core**, PostgreSQL,
JWT-аутентификация, SignalR для realtime, собственная отправка почты по SMTP.

---

## Документация

| Раздел | О чём |
|--------|-------|
| [Архитектура](docs/architecture.md) | Слои, направление зависимостей, карта проектов |
| [Слой Domain](docs/domain.md) | Сущности, перечисления, связи, схема данных |
| [Слой Application](docs/application.md) | Use-case сервисы, DTO, валидаторы, кэш, исключения |
| [Слой Infrastructure](docs/infrastructure.md) | EF Core, Identity, JWT, SMTP, бэкап, миграции, сидинг |
| [Слой Presentation / API](docs/api.md) | REST-эндпоинты, SignalR-хабы, обработка ошибок |
| [Безопасность](docs/security.md) | Аутентификация, подтверждение почты, сброс пароля, роли и политики, видимость |
| [Конфигурация](docs/configuration.md) | appsettings, секреты, переменные окружения, Options |
| [Тестирование](docs/testing.md) | Структура тестов, как запускать |
| [Развёртывание](DEPLOY.md) | Docker Compose, Postfix, DNS (SPF/DKIM/DMARC/PTR), бэкапы |

---

## Технологии

- **.NET 10**, ASP.NET Core, C# 14
- **PostgreSQL** + EF Core 9 (Npgsql)
- **ASP.NET Core Identity** — учётные записи, роли, токены (только в Infrastructure)
- **JWT** (HS512) — bearer-токены
- **SignalR** — realtime обновления очередей и результатов
- **FluentValidation** — валидация запросов
- **MailKit** — отправка писем через собственный SMTP
- **Serilog** — логирование
- **Scalar** — UI документации OpenAPI
- **xUnit + SQLite in-memory + NSubstitute + FluentAssertions** — тесты

---

## Структура решения

```
FunAndChecks.Domain          # Ядро: сущности и бизнес-правила, без зависимостей
FunAndChecks.Application     # Use-case сервисы, DTO, валидаторы, интерфейсы инфраструктуры
FunAndChecks.Infrastructure  # EF Core, Identity, JWT, SMTP, бэкап, кэш, сидинг
FunAndChecks                 # Presentation: контроллеры, SignalR-хабы, Program, конфигурация DI
FunAndChecks.Tests           # Юнит- и интеграционные тесты
Frontend                     # Blazor WASM хост (собирает всё вместе)
Frontend.Admin               # Административный UI (управление предметами, очередями, оценками)
Frontend.Student             # Студенческий UI (прогресс, очереди, результаты)
Frontend.Shared              # Общие компоненты, API-клиент, модели
```

Подробнее — в [docs/architecture.md](docs/architecture.md).

---

## Быстрый старт (локальная разработка)

1. Поднимите PostgreSQL (или `docker compose up -d db`).
2. Создайте `FunAndChecks/secrets.json` из [`FunAndChecks/secrets.template.json`](FunAndChecks/secrets.template.json)
   и заполните `ConnectionStrings`, `Jwt:Key`, при необходимости `Smtp` и `InitialAdmins`.
   Альтернатива — `dotnet user-secrets`.
3. Запустите API:
   ```bash
   dotnet run --project FunAndChecks
   ```
   Миграции применяются автоматически при старте, стартовые админы создаются из `InitialAdmins`.
4. Документация API и пробные запросы — на `/scalar`, спецификация OpenAPI — на `/openapi/v1.json`.

> В окружении `Development` CORS разрешает любой origin — фронтенд можно запускать отдельно от бэкенда.

Сборка и тесты:

```bash
dotnet build FunAndChecks.slnx
dotnet test  FunAndChecks.slnx
```

Полное развёртывание в проде (Docker Compose, почта, DNS, бэкапы) — в [DEPLOY.md](DEPLOY.md).
