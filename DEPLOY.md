# Развёртывание FunAndChecks

Бэкенд (ASP.NET, .NET 10) + PostgreSQL + собственный почтовый сервер (Postfix) + Caddy (авто-HTTPS).
Всё поднимается через `docker compose`. Письма (подтверждение почты и сброс пароля) уходят
через свой Postfix — без внешних сервисов и лимитов.

---

## 1. Предварительные требования

- Docker + Docker Compose
- Домен (далее `example.ru`) и доступ к его DNS
- VPS со статическим IP и **возможностью настроить PTR (rDNS)** — иначе письма будут попадать в спам
- Открытые порты: `80`, `443` (веб) и `25` исходящий (для отправки почты — у некоторых хостеров закрыт, нужно попросить открыть)

---

## 2. Секреты

Секреты **не лежат в репозитории**. Шаблон — [`FunAndChecks/secrets.template.json`](FunAndChecks/secrets.template.json).

### Для compose (прод)
Скопируйте шаблон в **корень репозитория** как `secrets.json` и оставьте там как минимум `InitialAdmins`
(остальное в проде передаётся через переменные окружения из `.env`):

```jsonc
{
  "InitialAdmins": [
    {
      "FirstName": "Super", "LastName": "Admin",
      "Email": "you@example.ru", "Password": "СИЛЬНЫЙ_ПАРОЛЬ",
      "Color": "#3366FF", "Letter": "S", "IsSuperAdmin": true
    }
  ]
}
```

Файл монтируется в контейнер только для чтения (`./secrets.json:/app/secrets.json:ro`).

### Переменные окружения compose — файл `.env` в корне

```dotenv
POSTGRES_DB=funandchecks
POSTGRES_USER=funandchecks
POSTGRES_PASSWORD=СГЕНЕРИРУЙТЕ
API_INTERNAL_PORT=8080

JWT_KEY=ДЛИННЫЙ_СЛУЧАЙНЫЙ_СЕКРЕТ_МИН_64_СИМВОЛА   # HS512
JWT_ISSUER=FunAndChecks
JWT_AUDIENCE=FunAndChecksClient

APP_DOMAIN=example.ru          # домен фронтенда/API (для CORS и Caddy)
MAIL_DOMAIN=example.ru         # домен отправителя писем (noreply@example.ru)
```

> `JWT_KEY` для HS512 должен быть не короче 64 байт. Сгенерировать: `openssl rand -base64 64`.

### Для локальной разработки через IDE
Положите `secrets.json` рядом с проектом — `FunAndChecks/secrets.json` (он в `.gitignore`),
заполнив все секции из шаблона (ConnectionStrings, Jwt:Key, Smtp, InitialAdmins).
Альтернатива — `dotnet user-secrets` (проект уже имеет `UserSecretsId`).

---

## 3. Настройки DNS

Пусть IP сервера — `203.0.113.10`, домен — `example.ru`.

| Тип   | Имя                          | Значение                                   | Зачем |
|-------|------------------------------|--------------------------------------------|-------|
| A     | `example.ru`                 | `203.0.113.10`                             | сайт/API |
| A     | `mail.example.ru`            | `203.0.113.10`                             | имя почтового хоста |
| MX    | `example.ru`                 | `10 mail.example.ru.`                       | приём (для совместимости/DMARC) |
| TXT   | `example.ru`                 | `v=spf1 a mx ip4:203.0.113.10 -all`         | SPF: кто вправе слать почту |
| TXT   | `_dmarc.example.ru`          | `v=DMARC1; p=quarantine; rua=mailto:postmaster@example.ru` | DMARC-политика |
| TXT   | `mail._domainkey.example.ru` | публичный DKIM-ключ (см. ниже)              | DKIM-подпись |
| PTR   | (у хостера, не в зоне домена) | `203.0.113.10 → mail.example.ru`           | rDNS, критично для доставляемости |

**PTR/rDNS** настраивается в панели VPS-провайдера (обратная зона), а не в DNS домена.

### Где взять DKIM-ключ
Postfix-контейнер генерирует ключ при первом старте (`DKIM_AUTOGENERATE=1`). После `docker compose up`:

```bash
docker exec funandchecks-mail sh -c 'cat /etc/opendkim/keys/*/mail.txt'
```

Выведет TXT-запись вида `mail._domainkey ... v=DKIM1; k=rsa; p=...`. Значение `p=...` положите
в TXT-запись `mail._domainkey.example.ru`. Ключ сохраняется в томе `maildkim`, поэтому при
перезапусках не меняется.

> Caddy сам получает TLS-сертификат для `APP_DOMAIN` (Let's Encrypt) — отдельных DNS-записей для HTTPS не нужно, только A-запись.

---

## 4. Запуск

```bash
docker compose up -d --build
```

При старте API:
- применяет миграции БД автоматически;
- создаёт роли и стартовых админов из `InitialAdmins`.

Проверка: `https://example.ru/health` и документация API на `https://example.ru/scalar`.

---

## 5. Резервные копии БД

Ручка (только супер-админ):

```
POST /api/admin/backup
```

Делает `pg_dump -Fc` в каталог `Backup:Directory` (в контейнере `/app/backups`, том `backups`).
Скопировать дампы на хост:

```bash
docker cp funandchecks-api:/app/backups ./db-backups
```

Автоматизация по расписанию — обычным `cron` на хосте, дёргающим эндпоинт с токеном супер-админа,
либо `docker exec funandchecks-api pg_dump ...` напрямую.

---

## 6. Локальная разработка через IDE

Можно запускать бэкенд из IDE и фронтенд отдельно (не обязательно весь `compose`):

1. Поднимите только БД: `docker compose up -d db` (или свой локальный PostgreSQL) и пропишите
   `ConnectionStrings:DefaultConnection` в `FunAndChecks/secrets.json`.
2. Запустите API из IDE (профиль `http`/`https`, окружение `Development`).
3. **CORS**: в `Development` бэкенд разрешает запросы с любого origin (с учётными данными),
   поэтому фронт на `http://localhost:5173` и т.п. ходит к API без правок. В проде список
   разрешённых origin задаётся в `Cors:AllowedOrigins` (compose выставляет туда `https://APP_DOMAIN`).
4. Документация и пробные запросы: `https://localhost:7169/scalar`, спецификация — `/openapi/v1.json`.
5. Почта без SMTP: если `Smtp:Host` пуст, письма не отправляются, а логируются (код подтверждения
   виден в логах) — удобно для отладки регистрации.

---

## 7. Доставляемость почты — чек-лист

- [ ] PTR (rDNS) указывает на `mail.example.ru`
- [ ] SPF, DKIM, DMARC добавлены и проверены (`https://www.mail-tester.com`)
- [ ] Исходящий порт 25 открыт хостером
- [ ] `Smtp__FromEmail` на домене `MAIL_DOMAIN`

Если письма всё же не уходят из контейнера на `587` без TLS — переключите на STARTTLS
(`Smtp__UseStartTls=true`) или порт `25`; для внутренней сети compose обычно достаточно plain `587`.
