# Слой Presentation / API

HTTP-контроллеры и SignalR-хабы. Контроллеры тонкие: принимают запрос, вызывают use-case сервис
Application, возвращают результат. Composition root и middleware-конвейер — в
[`Program.cs`](../FunAndChecks/Program.cs).

- Базовый префикс — `/api`.
- Документация и пробные запросы — **Scalar** на `/scalar`, спецификация OpenAPI — `/openapi/v1.json`.
- Аутентификация — JWT Bearer. Роли: `User` (студент), `Admin`, `SuperAdmin`. Подробно —
  [docs/security.md](security.md).

Обозначения столбца «Доступ»: 🌐 публично · 🔒 любой авторизованный · 👤 студент · 🛡 админ · ⭐ супер-админ.

## Auth — `/api/auth`

Все эндпоинты под rate-limit политикой `auth` (10 запросов/мин с IP). Дополнительно — не чаще
**одного письма в минуту на email** (подтверждение/сброс); превышение → `429` с понятным сообщением.

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| POST | `/register` | 🌐 | Регистрация студента (неактивен до подтверждения); на почту уходит код → `201 {id}`. Повторная регистрация на тот же неподтверждённый email затирает старую запись; подтверждённый email → `409` |
| POST | `/confirm-email` | 🌐 | Подтверждение почты по коду → `204` |
| POST | `/resend-confirmation` | 🌐 | Повторно отправить код подтверждения → `202` |
| POST | `/login` | 🌐 | Вход по email+паролю → `200 {accessToken, refreshToken}` |
| POST | `/refresh` | 🌐 | Обмен refresh-токена на новую пару (с ротацией) → `200 {accessToken, refreshToken}` |
| POST | `/logout` | 🌐 | Отзыв refresh-токена → `204` |
| POST | `/forgot-password` | 🌐 | Запросить код сброса пароля → `202` |
| POST | `/reset-password` | 🌐 | Сбросить пароль по коду → `204` |

## Текущий пользователь — `/api/me`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/` | 🔒 | Профиль текущего пользователя (`MeDto`) |
| GET | `/subjects` | 🔒 | Предметы, доступные группе студента |
| GET | `/group` | 🔒 | Группа студента |
| GET | `/queue-events?includePast=` | 🔒 | События, где студент записан (по умолчанию активные; `includePast=true` — с прошедшими) |
| GET | `/available-queue-events?includePast=` | 🔒 | События, доступные для записи (опц. с прошедшими) |
| GET | `/results/subjects/{subjectId}` | 🔒 | Детальные результаты студента по предмету |
| GET | `/access` | 🛡 | Собственные ограничения и скрытия админа |
| PUT | `/subjects/{subjectId}/hidden` | 🛡 | Скрыть/показать предмет в своих списках |
| PUT | `/groups/{groupId}/hidden` | 🛡 | Скрыть/показать группу в своих списках |

## Студенты — `/api/students`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/search?query=` | 🛡 | Поиск студентов по фамилии/имени (до 500) |
| GET | `/{studentId}` | 🌐 | Публичная карточка студента |
| GET | `/{studentId}/details` | 🛡 | Полная карточка (email, GitHub, цвет, группа) |
| PUT | `/{studentId}/color` | 🛡 | Задать цвет заливки ячейки ФИО (null — убрать) |
| GET | `/{studentId}/subjects/{subjectId}/tasks` | 🌐 | Задания предмета со статусами сдачи студента |
| GET | `/{studentId}/subjects/{subjectId}/grades` | 🛡 | Баллы студента по оценочным колонкам |

## Предметы — `/api/subjects`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/` | 🌐 | Список предметов |
| GET | `/{subjectId}` | 🌐 | Предмет |
| POST | `/` | 🛡 | Создать предмет → `201` |
| PUT | `/{subjectId}` | 🛡 | Переименовать предмет |
| DELETE | `/{subjectId}` | ⭐ | Удалить каскадно (задачи, история) → `204` |
| GET | `/{subjectId}/tasks` | 🌐 | Задачи предмета |
| POST | `/{subjectId}/tasks` | 🛡 | Создать задачу → `201` |
| GET | `/{subjectId}/students` | 🛡 | Студенты, чьи группы имеют доступ к предмету |
| GET | `/{subjectId}/grade-components` | 🌐 | Оценочные колонки (билет, курсовая) |
| POST | `/{subjectId}/grade-components` | 🛡 | Добавить оценочную колонку → `201` |

## Задачи — `/api/tasks`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| PUT | `/{taskId}` | 🛡 | Изменить задачу (название, описание, баллы) |
| DELETE | `/{taskId}` | ⭐ | Удалить задачу каскадно с историей сдач → `204` |

## Оценки — `/api/grade-components` (🛡)

| Метод | Путь | Описание |
|-------|------|----------|
| PUT | `/{componentId}` | Изменить колонку (название, диапазон баллов Min..Max) |
| DELETE | `/{componentId}` | Удалить колонку со всеми баллами → `204` |
| PUT | `/{componentId}/students/{studentId}` | Выставить/обновить баллы студента (в пределах Min..Max) → `204` |
| DELETE | `/{componentId}/students/{studentId}` | Удалить баллы студента → `204` |

> Оценочные колонки поддерживают диапазон `MinPoints..MaxPoints` (например, 2–5 для билета/курсовой). Балл вне диапазона → `409`.

## Группы — `/api/groups`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/` | 🌐 | Список групп (используется формой регистрации) |
| GET | `/{groupId}` | 🌐 | Группа |
| POST | `/` | 🛡 | Создать группу → `201` |
| PUT | `/{groupId}` | 🛡 | Переименовать группу → `200` |
| DELETE | `/{groupId}` | ⭐ | Удалить (студенты остаются без группы) → `204` |
| PUT | `/{groupId}/subjects/{subjectId}` | 🛡 | Дать группе доступ к предмету (идемпотентно) → `204` |
| DELETE | `/{groupId}/subjects/{subjectId}` | 🛡 | Отозвать доступ (идемпотентно) → `204` |
| GET | `/{groupId}/subject-ids` | 🛡 | Id предметов, доступных группе (для настройки доступа) |
| GET | `/for-subject/{subjectId}` | 🛡 | Id групп, которым доступен предмет |
| GET | `/{groupId}/students` | 🌐 | Студенты группы (краткие карточки) |
| GET | `/{groupId}/students/details` | 🛡 | Студенты группы с email/контактами |

## Очереди — `/api/queues`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/` | 🌐 | Активные события (не истёкшие > 2 дней) |
| GET | `/all` | 🌐 | Все события |
| GET | `/{eventId}` | 🌐 | Состав очереди с баллами, статусами и временем записи (можно смотреть без вступления) |
| POST | `/` | 🛡 | Создать событие. Параметры `AllowSelfJoin` и `AutoFillGroupId` (см. ниже) → `201` |
| PUT | `/{eventId}` | 🛡 | Изменить название/время события |
| DELETE | `/{eventId}` | 🛡 | Удалить событие с записями участников → `204` |
| POST | `/{eventId}/join` | 👤 | Студент встаёт в очередь (если разрешена самозапись) → `204` |
| PUT | `/{eventId}/students/{studentId}/status` | 🛡 | Сменить статус участника → `204` |

> **Авто-очереди**: при создании можно передать `AutoFillGroupIds` (одна или несколько групп) — в очередь
> сразу добавляются все студенты этих групп, а самостоятельная запись отключается (`AllowSelfJoin=false`).
> Попытка `join` в такую очередь → `403`.

## Сдачи — `/api/submissions` (🛡)

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/` | Зафиксировать попытку сдачи от имени текущего админа → `204` |
| GET | `/students/{studentId}/tasks/{taskId}` | История попыток сдачи задания |

## Результаты — `/api/results`

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/subjects/{subjectId}` | 🌐 | Сводная таблица результатов по предмету (строится по требованию, кэшируется) |
| GET | `/subjects/{subjectId}/export` | 🌐 | Таблица результатов в XLSX с цветовой заливкой ячеек |

## Админы — `/api/admins` (🛡)

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/` | 🛡 | Список админов |
| POST | `/` | ⭐ | Создать админа → `201 {id}` |
| PUT | `/{adminId}` | ⭐ | Обновить данные админа → `204` |
| DELETE | `/{adminId}` | ⭐ | Удалить админа (нельзя себя; нельзя с историей) → `204` |
| GET | `/{adminId}/access` | ⭐ | Ограничения и скрытия админа |
| PUT | `/{adminId}/subjects/{subjectId}/restriction` | ⭐ | Запретить/разрешить предмет админу → `204` |
| PUT | `/{adminId}/groups/{groupId}/restriction` | ⭐ | Запретить/разрешить группу админу → `204` |

## Бэкап БД — `/api/admin/backup` (⭐)

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/` | Создать дамп БД через `pg_dump` → `200 {path}` |

## Служебное

| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/health` | Health-check (включая доступность PostgreSQL) |
| GET | `/openapi/v1.json` | Спецификация OpenAPI |
| GET | `/scalar` | UI документации (Scalar) |

## SignalR-хабы

Токен передаётся в query-string (`?access_token=...`) — JWT-конвейер достаёт его для путей `/apiHub`.

### Очередь — `/apiHub/queueHub` ([`QueueHub`](../FunAndChecks/Hubs/QueueHub.cs))

- Клиент → сервер: `SubscribeToQueue(eventId)`, `UnsubscribeFromQueue(eventId)`.
- Сервер → клиент: `QueueEntryUpdated` (payload `QueueEntryUpdateDto`) — при входе в очередь или
  смене статуса участника.

### Результаты — `/apiHub/resultsHub` ([`ResultsHub`](../FunAndChecks/Hubs/ResultsHub.cs))

- Клиент → сервер: `SubscribeToSubjectResults(subjectId)`, `UnsubscribeFromSubjectResults(subjectId)`.
- Сервер → клиент: `ResultUpdated` (payload `ResultUpdateDto`) — изменился статус задачи;
  `GradeUpdated` (payload `GradeUpdateDto`) — изменился балл за колонку.

## Обработка ошибок

[`ExceptionHandlingMiddleware`](../FunAndChecks/Middleware/ExceptionHandlingMiddleware.cs) переводит
исключения прикладного слоя в `ProblemDetails`:

| Исключение | HTTP |
|-----------|------|
| `ValidationException` | 400 (с разбивкой ошибок по полям) |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException` | 409 |
| `RateLimitException` (письма на email) | 429 |
| прочее | 500 |

Rate-limit отдаёт `429`. Подробнее об авторизации — [docs/security.md](security.md).
