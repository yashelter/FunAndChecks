# Слой Application

Прикладной слой описывает сценарии использования (use-case сервисы), DTO, валидаторы и
**интерфейсы** того, что должна предоставить инфраструктура. Зависит только от Domain.

Исходники: [`FunAndChecks.Application`](../FunAndChecks.Application).

## Use-case сервисы

Каждый сервис инкапсулирует сценарии одной области. Контроллеры вызывают только их.

| Сервис | Интерфейс | Что делает |
|--------|-----------|-----------|
| `AuthService` | `IAuthService` | Регистрация студента, подтверждение почты, вход, сброс пароля |
| `StudentService` | `IStudentService` | Карточки студента, профиль `me`, обновление профиля, студенты по предмету, очереди студента |
| `AdminService` | `IAdminService` | CRUD админов (супер-админ), защита от удаления себя и админов с историей |
| `AdminAccessService` | `IAdminAccessService` | Видимость/ограничения предметов и групп, проверка доступа |
| `SubjectService` | `ISubjectService` | Предметы и задачи, статусы задач студента |
| `GroupService` | `IGroupService` | Группы, переименование, связи с предметами, списки студентов |
| `GradeService` | `IGradeService` | Оценочные колонки и баллы студентов |
| `SubmissionService` | `ISubmissionService` | Фиксация попыток сдачи, история |
| `QueueService` | `IQueueService` | События очереди, запись, смена статуса участника |
| `ResultsService` | `IResultsService` | Сводная таблица результатов по предмету и детальные результаты студента |

Регистрация — в [`DependencyInjection.cs`](../FunAndChecks.Application/DependencyInjection.cs)
(`services.AddApplication()`), все сервисы scoped.

## Интерфейсы инфраструктуры

Объявлены в Application, реализованы в Infrastructure/Presentation
([`Common/Interfaces`](../FunAndChecks.Application/Common/Interfaces)):

| Интерфейс | Реализация | Назначение |
|-----------|-----------|-----------|
| `IApplicationDbContext` | `ApplicationDbContext` (Infra) | Доступ к `DbSet`-ам и `SaveChangesAsync` |
| `IIdentityService` | `IdentityService` (Infra) | Учётные записи, проверка пароля, коды подтверждения/сброса |
| `ITokenService` | `TokenService` (Infra) | Выпуск JWT |
| `IEmailSender` | `SmtpEmailSender` (Infra) | Отправка писем по SMTP |
| `IResultsCacheService` | `ResultsCacheService` (Infra) | Кэш таблиц результатов |
| `IDatabaseBackupService` | `PgDumpBackupService` (Infra) | Бэкап БД через pg_dump |
| `IQueueNotifier`, `IResultsNotifier` | SignalR-нотификаторы (Presentation) | Realtime-уведомления |

## DTO и валидаторы

DTO — `record`-типы, сгруппированы по областям (`Auth`, `Students`, `Grades`, `Admins`, `Subjects`,
`Groups`, `Queues`, `Submissions`, `Results`, `Tasks`). Валидаторы — на **FluentValidation**,
регистрируются автоматически (`AddValidatorsFromAssembly`) и вызываются внутри сервисов
(`ValidateAndThrowAsync`). Брошенный `ValidationException` превращается в HTTP 400 middleware-ом.

Общее правило `HexColor()` ([`Common/Validation/ValidationRules.cs`](../FunAndChecks.Application/Common/Validation/ValidationRules.cs))
проверяет формат `#RRGGBB` для цветов студента и админа.

## Исключения

Прикладные исключения ([`Common/Exceptions`](../FunAndChecks.Application/Common/Exceptions)) задают
семантику ошибки независимо от HTTP; маппинг в коды — в Presentation:

| Исключение | HTTP | Когда |
|-----------|------|-------|
| `NotFoundException` | 404 | Сущность не найдена |
| `ConflictException` | 409 | Конфликт состояния (дубль, удаление себя, баллы > максимума) |
| `ForbiddenException` | 403 | Нет прав / неверные креды / ограничение доступа |
| `ValidationException` (FluentValidation) | 400 | Не прошла валидация |

## Кэш результатов

Сводная таблица результатов по предмету (`SubjectResultsDto`) кэшируется. Стратегия —
**ленивая загрузка + инвалидация по событиям**, без фонового воркера по таймеру:

- `ResultsService.GetSubjectResultsAsync` отдаёт значение из кэша; при промахе строит таблицу
  (студенты группы предмета × задачи + оценочные колонки, с подсчётом суммы баллов), кладёт в кэш
  и возвращает.
- Любая мутация, влияющая на таблицу, вызывает `cache.Invalidate(subjectId)`:

  | Источник изменения | Сервис |
  |--------------------|--------|
  | Новая сдача | `SubmissionService` |
  | Колонка/балл оценки | `GradeService` |
  | Создание/удаление задачи, удаление предмета | `SubjectService` |
  | Связь группа↔предмет, переименование/удаление группы | `GroupService` |

  > При нагрузке проекта построение таблицы — миллисекунды, поэтому прогрев по таймеру не нужен;
  > кэш просто гасит повторные чтения между изменениями.

## Realtime-события

Сервисы публикуют доменные события через нотификаторы (реализация — SignalR в Presentation):

- `IQueueNotifier.QueueEntryUpdatedAsync` — запись в очереди изменилась (вход, смена статуса).
- `IResultsNotifier.ResultUpdatedAsync` — изменился статус задачи студента.
- `IResultsNotifier.GradeUpdatedAsync` — изменился балл за оценочную колонку.

Подробнее о хабах — в [docs/api.md](api.md#signalr-хабы).

Дальше: [Infrastructure](infrastructure.md) · [API](api.md) · [Безопасность](security.md)
