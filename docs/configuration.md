# Конфигурация

Конфигурация собирается из (в порядке возрастания приоритета): `appsettings.json` →
`appsettings.{Environment}.json` → `secrets.json` (опционально, не в репозитории) →
user-secrets → переменные окружения.

## Несекретное — `appsettings.json`

Хранит только то, что не является секретом
([`FunAndChecks/appsettings.json`](../FunAndChecks/appsettings.json)):

| Секция | Поля | Назначение |
|--------|------|-----------|
| `Jwt` | `Issuer`, `Audience`, `TokenLifetimeDays` | Параметры токена (без `Key`) |
| `Backup` | `Directory` | Каталог для дампов БД |
| `Cors` | `AllowedOrigins[]` | Разрешённые origin в проде |
| `Serilog` | … | Логирование |

## Секреты — `secrets.json` / user-secrets / env

Шаблон: [`FunAndChecks/secrets.template.json`](../FunAndChecks/secrets.template.json). Реальный
`secrets.json` — в `.gitignore`. Содержит:

| Секция | Поля | Назначение |
|--------|------|-----------|
| `ConnectionStrings` | `DefaultConnection` | Подключение к PostgreSQL |
| `Jwt` | `Key` | Секрет подписи JWT (HS512, ≥ 64 байт) |
| `Smtp` | `Host`, `Port`, `UseStartTls`, `User`, `Password`, `FromEmail`, `FromName` | SMTP-отправка писем |
| `InitialAdmins` | массив админов | Стартовые админы, создаются при сидинге |

> `InitialAdmins` берётся **только** из секретов (отдельного `config/initial-admins.json` больше нет).

Локально удобно использовать user-secrets (у проекта есть `UserSecretsId`):

```bash
dotnet user-secrets --project FunAndChecks set "Jwt:Key" "<длинный-секрет>"
dotnet user-secrets --project FunAndChecks set "ConnectionStrings:DefaultConnection" "Host=...;Database=...;Username=...;Password=..."
```

### Пример `secrets.json`

```jsonc
{
  "ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=funandchecks;Username=postgres;Password=..." },
  "Jwt": { "Key": "длинный_случайный_секрет_минимум_64_символа" },
  "Smtp": { "Host": "localhost", "Port": 25, "UseStartTls": false, "FromEmail": "noreply@example.ru", "FromName": "FunAndChecks" },
  "InitialAdmins": [
    { "FirstName": "Super", "LastName": "Admin", "Email": "you@example.ru", "Password": "...", "Color": "#3366FF", "Letter": "S", "IsSuperAdmin": true }
  ]
}
```

## Переменные окружения (Docker / прод)

В контейнере секции конфигурации задаются через переменные с разделителем `__`. Примеры из
[`compose.yaml`](../compose.yaml):

| Переменная | Соответствует |
|-----------|---------------|
| `ConnectionStrings__DefaultConnection` | строка подключения |
| `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` | параметры JWT |
| `Smtp__Host`, `Smtp__Port`, `Smtp__UseStartTls`, `Smtp__FromEmail`, `Smtp__FromName` | SMTP |
| `Backup__Directory` | каталог бэкапов |
| `Cors__AllowedOrigins__0` | первый разрешённый origin |

`InitialAdmins` в compose передаётся файлом `secrets.json`, смонтированным в `/app/secrets.json`.

## Классы Options

| Класс | Секция |
|-------|--------|
| [`JwtOptions`](../FunAndChecks.Infrastructure/Identity/JwtOptions.cs) | `Jwt` |
| [`SmtpOptions`](../FunAndChecks.Infrastructure/Email/SmtpOptions.cs) | `Smtp` |
| [`BackupOptions`](../FunAndChecks.Infrastructure/Backup/BackupOptions.cs) | `Backup` |

## CORS

- В **Development** бэкенд разрешает запросы с любого origin (с учётными данными) — фронтенд можно
  запускать локально отдельно от бэкенда.
- В прочих окружениях разрешённые origin берутся из `Cors:AllowedOrigins`.

Полное развёртывание (compose, Postfix, DNS, бэкапы) — в [DEPLOY.md](../DEPLOY.md).
