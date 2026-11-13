using FluentValidation;
using FluentValidation.Results;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunAndChecks.Application.Queues;

public class QueueService(
    IApplicationDbContext db,
    IQueueNotifier queueNotifier,
    IAdminAccessService accessService,
    IValidator<CreateQueueEventRequest> createEventValidator,
    IValidator<UpdateQueueEventRequest> updateEventValidator,
    ILogger<QueueService> logger,
    IProfileService? profiles = null)
    : IQueueService
{
    private static readonly SetQueueLateRuleRequestValidator LateRuleValidator = new();

    private async Task<IQueryable<QueueEvent>> VisibleEventsAsync(Guid? adminId, CancellationToken cancellationToken)
    {
        if (!adminId.HasValue || await accessService.IsSuperAdminAsync(adminId.Value, cancellationToken))
            return db.QueueEvents;
        return db.QueueEvents.Where(qe => db.AdminSubjectAccesses.Any(
            a => a.AdminId == adminId && a.SubjectId == qe.SubjectId && !a.IsRestricted));
    }

    public Task<List<QueueEventDto>> GetActiveEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default) =>
        GetEventsAsync(adminId, QueueListScope.Current, cancellationToken);

    public Task<List<QueueEventDto>> GetAllEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default) =>
        GetEventsAsync(adminId, QueueListScope.All, cancellationToken);

    public async Task<List<QueueEventDto>> GetEventsAsync(Guid? adminId, QueueListScope scope, CancellationToken cancellationToken = default)
    {
        var events = (await VisibleEventsAsync(adminId, cancellationToken)).InScope(scope, DateTime.UtcNow);
        // Архив читают от свежих к старым; текущие и полный список — по времени начала.
        events = scope == QueueListScope.Archive
            ? events.OrderByDescending(qe => qe.EventDateTime)
            : events.OrderBy(qe => qe.EventDateTime);
        return await events
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin,
                qe.Subject.Name, null, null, null, qe.ArchivedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureEventAllowedAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default)
    {
        var subjectId = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => (int?)qe.SubjectId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);
    }

    public async Task<QueueDetailsDto> GetDetailsAsync(int eventId, Guid? adminId = null, CancellationToken cancellationToken = default)
    {
        var queueEvent = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => new
            {
                qe.Id,
                qe.Name,
                qe.SubjectId,
                SubjectName = qe.Subject.Name,
                qe.EventDateTime,
                qe.AllowSelfJoin,
                qe.LateAfterMinutes,
                qe.ArchivedAt,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        if (adminId.HasValue)
            await accessService.EnsureSubjectAllowedAsync(adminId.Value, queueEvent.SubjectId, cancellationToken);

        var entries = await db.QueueEntries
            .AsNoTracking()
            .Where(p => p.QueueEventId == eventId)
            .OrderBy(p => p.JoinedAt)
            .ThenBy(p => p.Round)
            .Select(p => new
            {
                p.Id,
                p.Round,
                p.StudentId,
                p.Student.FirstName,
                p.Student.LastName,
                GroupName = p.Student.Group != null ? p.Student.Group.Name : "N/A",
                p.Status,
                AdminName = p.Status == QueueEntryStatus.Checking && p.CurrentAdmin != null ? p.CurrentAdmin.FullName : null,
                CheckingByAdminId = p.Status == QueueEntryStatus.Checking ? p.CurrentAdminId : null,
                p.JoinedAt,
                p.LateExcused,
                p.Student.Color,
                CanManage = adminId.HasValue && !db.AdminGroupAccesses.Any(a =>
                    a.AdminId == adminId && a.GroupId == p.Student.GroupId && a.IsRestricted),
            })
            .ToListAsync(cancellationToken);

        // Баллы (сумма MaxPoints принятых задач предмета) считаем в памяти —
        // так избегаем непереводимого Distinct().Sum() и лишних подзапросов.
        var studentIds = entries.Select(e => e.StudentId).ToList();
        var activeAchievements = profiles is null
            ? new Dictionary<Guid, ActiveAchievementDto>()
            : await profiles.GetActiveAchievementsAsync(studentIds, cancellationToken);
        var allSubmissions = await db.Submissions
            .Where(s => studentIds.Contains(s.StudentId)
                        && s.Task.SubjectId == queueEvent.SubjectId)
            // Последняя попытка (StudentId, TaskId) с детерминированным тай-брейком по Id,
            // иначе при равных SubmittedAt в выборку попадают обе и баллы недетерминированы.
            .Where(s => s.Id == db.Submissions
                .Where(s2 => s2.StudentId == s.StudentId && s2.TaskId == s.TaskId)
                .OrderByDescending(s2 => s2.SubmittedAt)
                .ThenByDescending(s2 => s2.Id)
                .Select(s2 => (int?)s2.Id)
                .FirstOrDefault())
            .Select(s => new { s.StudentId, s.TaskId, s.Status, s.SubmittedAt, s.Task.MaxPoints })
            .ToListAsync(cancellationToken);

        var pointsByStudent = allSubmissions
            .GroupBy(s => s.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(s => s.TaskId)
                      .Select(tg => tg.OrderByDescending(x => x.SubmittedAt).First())
                      .Where(s => s.Status == SubmissionStatus.Accepted)
                      .Sum(s => s.MaxPoints));

        var participants = entries
            .Select(e => new QueueParticipantDto(
                e.StudentId,
                e.FirstName,
                e.LastName,
                e.GroupName,
                pointsByStudent.GetValueOrDefault(e.StudentId),
                e.Status,
                e.AdminName,
                e.JoinedAt,
                e.Color,
                e.CanManage,
                e.CheckingByAdminId,
                e.Id,
                e.Round,
                activeAchievements.GetValueOrDefault(e.StudentId),
                QueueLateness.LateMinutes(queueEvent.EventDateTime, queueEvent.LateAfterMinutes, e.Round, e.JoinedAt),
                e.LateExcused))
            .ToList();

        return new QueueDetailsDto(
            queueEvent.Id,
            queueEvent.Name,
            queueEvent.SubjectName,
            queueEvent.SubjectId,
            queueEvent.EventDateTime,
            queueEvent.AllowSelfJoin,
            participants,
            queueEvent.LateAfterMinutes,
            queueEvent.ArchivedAt);
    }

    public async Task<QueueEventDto> CreateEventAsync(Guid adminId, CreateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await createEventValidator.ValidateAndThrowAsync(request, cancellationToken);
        await accessService.EnsureSubjectAllowedAsync(adminId, request.SubjectId, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {request.SubjectId} not found.");

        var autoFillGroupIds = request.AutoFillGroupIds?.Distinct().ToList() ?? [];

        // Самозапись не зависит от автозаполнения: преподаватель решает сам и может поменять позже.
        var queueEvent = new QueueEvent
        {
            Name = request.Name,
            EventDateTime = request.EventDateTime,
            SubjectId = request.SubjectId,
            AllowSelfJoin = request.AllowSelfJoin,
            LateAfterMinutes = request.LateAfterMinutes,
        };
        db.QueueEvents.Add(queueEvent);

        if (autoFillGroupIds.Count > 0)
        {
            var groups = await db.Groups
                .Where(g => autoFillGroupIds.Contains(g.Id))
                .ToListAsync(cancellationToken);

            var missing = autoFillGroupIds.Except(groups.Select(g => g.Id)).ToList();
            if (missing.Count > 0)
                throw new NotFoundException($"Group(s) not found: {string.Join(", ", missing)}.");

            await accessService.EnsureGroupsAllowedAsync(adminId, autoFillGroupIds, cancellationToken);

            var linkedGroupIds = await db.GroupSubjects
                .Where(gs => gs.SubjectId == request.SubjectId && autoFillGroupIds.Contains(gs.GroupId))
                .Select(gs => gs.GroupId)
                .ToListAsync(cancellationToken);
            var unrelated = autoFillGroupIds.Except(linkedGroupIds).ToList();
            if (unrelated.Count > 0)
                throw new ConflictException(
                    $"Group(s) are not linked to subject {request.SubjectId}: {string.Join(", ", unrelated)}.",
                    "queue.autofill_group_not_enrolled",
                    new Dictionary<string, object?> { ["subjectId"] = request.SubjectId, ["groupIds"] = string.Join(",", unrelated) });

            // Группы запоминаем: подтвердившиеся позже студенты попадут в очередь сами (QueueAutoFillService).
            queueEvent.AutoFillGroups = groups;

            // Только подтверждённые студенты выбранных групп.
            var studentIds = await db.Students
                .Where(s => s.IsActive && s.GroupId != null && autoFillGroupIds.Contains(s.GroupId.Value))
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var studentId in studentIds)
            {
                db.QueueEntries.Add(new QueueEntry
                {
                    QueueEvent = queueEvent,
                    StudentId = studentId,
                    JoinedAt = now,
                    Status = QueueEntryStatus.Waiting,
                    // Записала автозапись, а не сам студент: опоздавшим он не считается.
                    LateExcused = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (autoFillGroupIds.Count > 0)
            await TopUpAutoFillAsync(queueEvent.Id, autoFillGroupIds, cancellationToken);

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
    }

    /// <summary>
    /// Студент мог подтвердиться между выборкой состава и коммитом очереди: состав он уже пропустил, а
    /// <see cref="QueueAutoFillService"/> ещё не видел очередь. Повторная выборка после коммита закрывает это окно.
    /// Ошибка здесь не отменяет созданную очередь.
    /// </summary>
    private async Task TopUpAutoFillAsync(int eventId, List<int> groupIds, CancellationToken cancellationToken)
    {
        List<QueueEntry> added = [];
        try
        {
            await db.ExecuteSerializableAsync(async ct =>
            {
                var missing = await db.Students
                    .Where(s => s.IsActive && s.GroupId != null && groupIds.Contains(s.GroupId.Value)
                                && !db.QueueEntries.Any(e => e.QueueEventId == eventId && e.StudentId == s.Id))
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                if (missing.Count == 0) return;

                var now = DateTime.UtcNow;
                added = missing.Select(studentId => new QueueEntry
                {
                    QueueEventId = eventId,
                    StudentId = studentId,
                    JoinedAt = now,
                    Status = QueueEntryStatus.Waiting,
                    LateExcused = true,
                }).ToList();
                db.QueueEntries.AddRange(added);
                await db.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Параллельный QueueAutoFillService успел добавить того же студента — уникальный индекс отверг дубль.
            foreach (var entry in added)
                db.QueueEntries.Entry(entry).State = EntityState.Detached;
            logger.LogWarning(ex, "Auto-fill top-up for queue {EventId} failed.", eventId);
        }
    }

    public async Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await updateEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectId = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => (int?)qe.SubjectId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);

        QueueEvent queueEvent = null!;
        string? adminName = null;
        var selfJoinChanged = false;
        await db.ExecuteSerializableAsync(async ct =>
        {
            queueEvent = await db.QueueEvents.FindAsync([eventId], ct)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
            EnsureNotArchived(queueEvent);

            var admin = await db.Admins.FindAsync([adminId], ct);
            adminName = admin?.FullName;

            queueEvent.Name = request.Name;
            queueEvent.EventDateTime = request.EventDateTime;
            selfJoinChanged = request.AllowSelfJoin is { } allowSelfJoin && allowSelfJoin != queueEvent.AllowSelfJoin;
            if (selfJoinChanged)
                queueEvent.AllowSelfJoin = request.AllowSelfJoin!.Value;
            await db.SaveChangesAsync(ct);
        }, cancellationToken);

        // QueueChanged — перечитать очередь (название, время); QueueSettingsChanged — уведомление о самозаписи,
        // только если она действительно поменялась. Уведомления не должны ронять уже сохранённую правку.
        try
        {
            await queueNotifier.QueueChangedAsync(eventId, cancellationToken);
            if (selfJoinChanged)
                await queueNotifier.QueueSettingsChangedAsync(
                    new QueueSettingsUpdateDto(queueEvent.Id, queueEvent.AllowSelfJoin, adminName, adminId), cancellationToken);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Queue event {EventId} updated, but notification failed.", eventId); }

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
    }

    public async Task<QueueEventDto> ArchiveEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default)
    {
        var subjectId = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => (int?)qe.SubjectId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);

        QueueEvent queueEvent = null!;
        var archivedNow = false;
        // Проверка «никого не проверяют» и отметка — в одной serializable-транзакции с изменениями статусов:
        // параллельные «взять на проверку» и «архивировать» не могут оба зафиксироваться.
        await db.ExecuteSerializableAsync(async ct =>
        {
            archivedNow = false;
            queueEvent = await db.QueueEvents.FindAsync([eventId], ct)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
            await accessService.EnsureSubjectAllowedAsync(adminId, queueEvent.SubjectId, ct);
            // Повторное архивирование ничего не меняет: отметка остаётся первой.
            if (queueEvent.ArchivedAt is not null)
                return;
            if (await db.QueueEntries.AnyAsync(e => e.QueueEventId == eventId && e.Status == QueueEntryStatus.Checking, ct))
                throw new ConflictException("Finish the review in progress before archiving the queue.", "queue.archive_checking");
            queueEvent.ArchivedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            archivedNow = true;
        }, cancellationToken);

        // QueueChanged — открытые списки и детали перечитают очередь и увидят отметку архива.
        if (archivedNow)
        {
            try { await queueNotifier.QueueChangedAsync(eventId, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Queue {EventId} archived, but notification failed.", eventId); }
        }

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin,
            ArchivedAt: queueEvent.ArchivedAt);
    }

    public async Task DeleteEventAsync(Guid adminId, int eventId, CancellationToken cancellationToken = default)
    {
        var queueEvent = await db.QueueEvents.FindAsync([eventId], cancellationToken)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, queueEvent.SubjectId, cancellationToken);

        db.QueueEvents.Remove(queueEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.FindAsync([studentId], cancellationToken)
                      ?? throw new NotFoundException("Student not found.");

        if (student.GroupId == null)
            throw new ForbiddenException("You are not assigned to any group.");

        if (!await db.QueueEvents.AnyAsync(qe => qe.Id == eventId, cancellationToken))
            throw new NotFoundException($"Queue event with ID {eventId} not found.");

        var isGroupAllowed = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .AnyAsync(qe => qe.Subject.GroupSubjects.Any(gs => gs.GroupId == student.GroupId.Value), cancellationToken);
        if (!isGroupAllowed)
            throw new ForbiddenException("Your group does not have access to the subject of this event.");

        try
        {
            // Сдавший или пропущенный встаёт снова новым кругом в конец ожидающих; прошлые круги остаются.
            // Активная запись одна: двойной клик отвечает 409 до INSERT, настоящую гонку ловит
            // фильтрованный уникальный индекс (catch ниже).
            await db.ExecuteSerializableAsync(async ct =>
            {
                var queueEvent = await db.QueueEvents.FindAsync([eventId], ct)
                                 ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
                // Архивная очередь закрыта и для первой записи, и для повторного круга.
                EnsureNotArchived(queueEvent);
                // Перепроверка в транзакции записи: студента могли перевести в группу без этого предмета
                // или отвязать группу от предмета после проверок выше.
                var groupId = await db.Students.Where(s => s.Id == studentId).Select(s => s.GroupId).SingleOrDefaultAsync(ct);
                if (groupId is null || !await db.GroupSubjects.AnyAsync(gs => gs.GroupId == groupId && gs.SubjectId == queueEvent.SubjectId, ct))
                    throw new ForbiddenException("Your group does not have access to the subject of this event.");
                var latest = await LatestEntryQuery(eventId, studentId).AsNoTracking().FirstOrDefaultAsync(ct);
                if (latest?.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Checking)
                    throw AlreadyJoined();
                // The switch blocks only a first entry. Students already in the queue may
                // leave and later rejoin, even while new self-enrollment is frozen.
                if (latest is null && !queueEvent.AllowSelfJoin)
                    throw new ForbiddenException("Self-enrollment is disabled for this queue.", "queue.self_join_disabled");
                db.QueueEntries.Add(new QueueEntry
                {
                    QueueEventId = eventId,
                    StudentId = studentId,
                    JoinedAt = DateTime.UtcNow,
                    Status = QueueEntryStatus.Waiting,
                    Round = (latest?.Round ?? 0) + 1,
                });
                await db.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicate = await db.QueueEntries.AsNoTracking()
                .AnyAsync(e => e.QueueEventId == eventId && e.StudentId == studentId
                               && (e.Status == QueueEntryStatus.Waiting || e.Status == QueueEntryStatus.Checking), cancellationToken);
            if (duplicate)
                throw AlreadyJoined();
            throw;
        }

        await NotifyBestEffortAsync(
            new QueueEntryUpdateDto(eventId, studentId, QueueEntryStatus.Waiting, null), cancellationToken);
    }

    public async Task LeaveAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default)
    {
        // Чтение, проверка статуса и удаление — в одной serializable-транзакции: иначе преподаватель
        // мог бы взять студента на проверку между проверкой и DELETE, и начатая проверка исчезла бы.
        await db.ExecuteSerializableAsync(async ct =>
        {
            // Выходят из последнего круга; прошлые круги (и их результат) остаются.
            var entry = await LatestEntryQuery(eventId, studentId).Include(e => e.QueueEvent).FirstOrDefaultAsync(ct)
                        ?? throw new NotFoundException("You are not in this queue.", "queue.not_joined");
            EnsureNotArchived(entry.QueueEvent);
            // Без самозаписи встать можно только тому, у кого уже есть круг (JoinAsync), а выход удаляет строку:
            // ушедший с первого круга не вернулся бы. Поэтому из первого круга при закрытой записи выйти нельзя.
            if (entry.Status is QueueEntryStatus.Checking or QueueEntryStatus.Finished
                || (entry.Round == 1 && !entry.QueueEvent.AllowSelfJoin))
                throw new ConflictException("You cannot leave this queue now: it was filled by the teacher, or you are being checked or finished.", "queue.leave_unavailable");
            db.QueueEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        await NotifyBestEffortAsync(new QueueEntryUpdateDto(eventId, studentId, QueueEntryStatus.Finished, null), cancellationToken);
    }

    public async Task<List<QueueEventDto>> GetMyEventsAsync(Guid studentId, QueueListScope scope, CancellationToken cancellationToken = default)
    {
        var mine = db.QueueEvents.AsNoTracking()
            .InScope(scope, DateTime.UtcNow)
            .Where(qe => qe.Participants.Any(p => p.StudentId == studentId));
        var events = await (scope == QueueListScope.Archive ? mine.OrderByDescending(qe => qe.EventDateTime) : mine.OrderBy(qe => qe.EventDateTime))
            .Select(qe => new { qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin, qe.SubjectId, SubjectName = qe.Subject.Name, qe.LateAfterMinutes, qe.ArchivedAt })
            .ToListAsync(cancellationToken);
        if (events.Count == 0) return [];

        // Для карточек нужны только статус, место и длина очереди: участники всех событий и их баллы — двумя
        // запросами на все события, без имён, цветов и достижений, которые собирает полный GetDetailsAsync.
        var eventIds = events.Select(e => e.Id).ToList();
        var entries = await db.QueueEntries.AsNoTracking()
            .Where(e => eventIds.Contains(e.QueueEventId))
            .Select(e => new { e.QueueEventId, e.Id, e.StudentId, e.Status, e.Round, e.JoinedAt, e.LateExcused })
            .ToListAsync(cancellationToken);
        var subjectIds = events.Select(e => e.SubjectId).Distinct().ToList();
        var points = await AcceptedPointsAsync(entries.Select(e => e.StudentId).Distinct().ToList(), subjectIds, cancellationToken);

        var entriesByEvent = entries.ToLookup(e => e.QueueEventId);
        var result = new List<QueueEventDto>(events.Count);
        foreach (var queueEvent in events)
        {
            var participants = entriesByEvent[queueEvent.Id].Select(e => new QueueParticipantDto(
                    e.StudentId, "", "", "", points.GetValueOrDefault((e.StudentId, queueEvent.SubjectId)), e.Status, null, e.JoinedAt,
                    null, false, null, e.Id, e.Round, null,
                    QueueLateness.LateMinutes(queueEvent.EventDateTime, queueEvent.LateAfterMinutes, e.Round, e.JoinedAt),
                    e.LateExcused))
                .ToList();
            var waiting = QueueOrder.Waiting(participants);
            var me = participants.Where(p => p.StudentId == studentId).MaxBy(p => p.Round);
            var position = waiting.FindIndex(p => p.StudentId == studentId);
            result.Add(new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin,
                queueEvent.SubjectName, me?.Status, position >= 0 ? position + 1 : null, waiting.Count, queueEvent.ArchivedAt));
        }
        return result;
    }

    /// <summary>
    /// Баллы студентов по предметам: сумма MaxPoints задач, последняя попытка по которым принята
    /// (тай-брейк по Id — как в таблице результатов и GetDetailsAsync).
    /// </summary>
    private async Task<Dictionary<(Guid StudentId, int SubjectId), int>> AcceptedPointsAsync(
        List<Guid> studentIds, List<int> subjectIds, CancellationToken cancellationToken)
    {
        var latest = await db.Submissions
            .Where(s => studentIds.Contains(s.StudentId) && subjectIds.Contains(s.Task.SubjectId))
            .Where(s => s.Id == db.Submissions
                .Where(s2 => s2.StudentId == s.StudentId && s2.TaskId == s.TaskId)
                .OrderByDescending(s2 => s2.SubmittedAt)
                .ThenByDescending(s2 => s2.Id)
                .Select(s2 => (int?)s2.Id)
                .FirstOrDefault())
            .Where(s => s.Status == SubmissionStatus.Accepted)
            .Select(s => new { s.StudentId, s.Task.SubjectId, s.Task.MaxPoints })
            .ToListAsync(cancellationToken);
        return latest.GroupBy(s => (s.StudentId, s.SubjectId)).ToDictionary(g => g.Key, g => g.Sum(s => s.MaxPoints));
    }

    public async Task SetLateRuleAsync(Guid adminId, int eventId, SetQueueLateRuleRequest request, CancellationToken cancellationToken = default)
    {
        await LateRuleValidator.ValidateAndThrowAsync(request, cancellationToken);
        var subjectId = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => (int?)qe.SubjectId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
        await accessService.EnsureSubjectAllowedAsync(adminId, subjectId, cancellationToken);

        // В транзакции: правка не должна проскочить в уже архивированную очередь.
        await db.ExecuteSerializableAsync(async ct =>
        {
            var queueEvent = await db.QueueEvents.FindAsync([eventId], ct)
                             ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
            EnsureNotArchived(queueEvent);
            queueEvent.LateAfterMinutes = request.LateAfterMinutes;
            await db.SaveChangesAsync(ct);
        }, cancellationToken);

        try { await queueNotifier.QueueChangedAsync(eventId, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Late rule of queue {EventId} changed, but notification failed.", eventId); }
    }

    public async Task SetLateExcusedAsync(Guid adminId, int eventId, int entryId, bool excused, CancellationToken cancellationToken = default)
    {
        var entry = await db.QueueEntries
            .Include(e => e.QueueEvent)
            .Include(e => e.Student)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.QueueEventId == eventId, cancellationToken)
            ?? throw new NotFoundException("Queue entry not found.", "queue.entry_not_found");
        await accessService.EnsureSubjectAllowedAsync(adminId, entry.QueueEvent.SubjectId, cancellationToken);
        if (entry.Student.GroupId is int groupId)
            await accessService.EnsureGroupAllowedAsync(adminId, groupId, cancellationToken);

        await db.ExecuteSerializableAsync(async ct =>
        {
            var current = await db.QueueEntries.Include(e => e.QueueEvent)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.QueueEventId == eventId, ct)
                ?? throw new NotFoundException("Queue entry not found.", "queue.entry_not_found");
            EnsureNotArchived(current.QueueEvent);
            current.LateExcused = excused;
            await db.SaveChangesAsync(ct);
        }, cancellationToken);

        await NotifyBestEffortAsync(new QueueEntryUpdateDto(eventId, entry.StudentId, entry.Status, null), cancellationToken);
    }

    public async Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default)
    {
        // Числовой JSON пропускает любое значение enum; неизвестный статус выпал бы из активного индекса очереди.
        if (!Enum.IsDefined(status))
            throw new ValidationException([new ValidationFailure(nameof(status), "Unknown queue entry status.")]);

        // Преподаватель работает с последним кругом студента; прошлые круги — история.
        var entry = await LatestEntryQuery(eventId, studentId)
            .AsNoTracking()
            .Include(qu => qu.QueueEvent)
            .Include(qu => qu.Student)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Student not found in this queue.");

        var admin = await db.Admins.FindAsync([adminId], cancellationToken)
                    ?? throw new ForbiddenException("Admin profile not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, entry.QueueEvent.SubjectId, cancellationToken);
        if (entry.Student.GroupId is int groupId)
            await accessService.EnsureGroupAllowedAsync(adminId, groupId, cancellationToken);

        // Read the actual previous status/reviewer in the committing transaction.
        // An already tracked row from an earlier screen must not supply stale audit values.
        await db.ExecuteSerializableAsync(async ct =>
        {
            var current = await db.QueueEntries.AsNoTracking()
                .FirstOrDefaultAsync(qu => qu.Id == entry.Id, ct)
                ?? throw new NotFoundException("Student not found in this queue.");
            // Отметку архива читаем в той же транзакции: «взять на проверку» и «архивировать» не проходят вместе.
            var archivedAt = await db.QueueEvents.Where(qe => qe.Id == eventId).Select(qe => qe.ArchivedAt).FirstOrDefaultAsync(ct);
            if (archivedAt is not null)
                throw Archived();
            await accessService.EnsureSubjectAllowedAsync(adminId, entry.QueueEvent.SubjectId, ct);
            if (entry.Student.GroupId is int currentGroupId)
                await accessService.EnsureGroupAllowedAsync(adminId, currentGroupId, ct);

            var existingTracked = db.QueueEntries.Local.FirstOrDefault(qu => qu.Id == current.Id);
            if (existingTracked is not null) db.QueueEntries.Entry(existingTracked).State = EntityState.Detached;
            // Повторное «Завершено» (повтор запроса, второе нажатие) ничего не меняет: иначе зачёт за проверку
            // перешёл бы к нажавшему, ведь CurrentAdminId после первого завершения уже пуст.
            if (current.Status == QueueEntryStatus.Finished && status == QueueEntryStatus.Finished)
                return;
            db.QueueEntries.Attach(current);
            // Для статистики: круг засчитывается последнему, кто вёл студента, а без взятия на проверку — нажавшему.
            // Читаем владельца до того, как CurrentAdminId перезапишется ниже.
            current.FinishedByAdminId = status == QueueEntryStatus.Finished ? current.CurrentAdminId ?? admin.Id : null;
            current.Status = status;
            current.CurrentAdminId = status == QueueEntryStatus.Checking ? admin.Id : null;
            await db.SaveChangesAsync(ct);
            db.QueueEntries.Entry(current).State = EntityState.Detached;
        }, cancellationToken);

        await NotifyBestEffortAsync(
            new QueueEntryUpdateDto(eventId, studentId, status,
                status == QueueEntryStatus.Checking ? admin.FullName : null), cancellationToken);
    }

    private IQueryable<QueueEntry> LatestEntryQuery(int eventId, Guid studentId) => db.QueueEntries
        .Where(e => e.QueueEventId == eventId && e.StudentId == studentId)
        .OrderByDescending(e => e.Round)
        .ThenByDescending(e => e.Id);

    private static ConflictException AlreadyJoined() => new("Student is already in the queue.", "queue.already_joined");

    private static ConflictException Archived() => new("The queue is archived and read-only.", "queue.archived");

    private static void EnsureNotArchived(QueueEvent queueEvent)
    {
        if (queueEvent.ArchivedAt is not null)
            throw Archived();
    }

    private async Task NotifyBestEffortAsync(QueueEntryUpdateDto update, CancellationToken cancellationToken)
    {
        try
        {
            await queueNotifier.QueueEntryUpdatedAsync(update, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Queue change committed, but notification failed for event {EventId}.", update.EventId);
        }
    }
}
