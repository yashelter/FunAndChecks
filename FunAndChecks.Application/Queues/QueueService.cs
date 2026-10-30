using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
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
    ILogger<QueueService> logger)
    : IQueueService
{
    /// <summary>Сколько времени событие остаётся в списке активных после своей даты.</summary>
    private static readonly TimeSpan ActiveEventGracePeriod = TimeSpan.FromDays(1);

    private async Task<IQueryable<QueueEvent>> VisibleEventsAsync(Guid? adminId, CancellationToken cancellationToken)
    {
        if (!adminId.HasValue || await accessService.IsSuperAdminAsync(adminId.Value, cancellationToken))
            return db.QueueEvents;
        return db.QueueEvents.Where(qe => db.AdminSubjectAccesses.Any(
            a => a.AdminId == adminId && a.SubjectId == qe.SubjectId && !a.IsRestricted));
    }

    public async Task<List<QueueEventDto>> GetActiveEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - ActiveEventGracePeriod;
        return await (await VisibleEventsAsync(adminId, cancellationToken))
            .Where(qe => qe.EventDateTime > threshold)
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<QueueEventDto>> GetAllEventsAsync(Guid? adminId = null, CancellationToken cancellationToken = default) =>
        await (await VisibleEventsAsync(adminId, cancellationToken))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);

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
                p.Student.Color,
                CanManage = adminId.HasValue && !db.AdminGroupAccesses.Any(a =>
                    a.AdminId == adminId && a.GroupId == p.Student.GroupId && a.IsRestricted),
            })
            .ToListAsync(cancellationToken);

        // Баллы (сумма MaxPoints принятых задач предмета) считаем в памяти —
        // так избегаем непереводимого Distinct().Sum() и лишних подзапросов.
        var studentIds = entries.Select(e => e.StudentId).ToList();
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
                e.Round))
            .ToList();

        return new QueueDetailsDto(
            queueEvent.Id,
            queueEvent.Name,
            queueEvent.SubjectName,
            queueEvent.SubjectId,
            queueEvent.EventDateTime,
            queueEvent.AllowSelfJoin,
            participants);
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
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
    }

    public async Task<QueueEventDto> UpdateEventAsync(Guid adminId, int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await updateEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var queueEvent = await db.QueueEvents.FindAsync([eventId], cancellationToken)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, queueEvent.SubjectId, cancellationToken);

        queueEvent.Name = request.Name;
        queueEvent.EventDateTime = request.EventDateTime;
        if (request.AllowSelfJoin is { } allowSelfJoin)
            queueEvent.AllowSelfJoin = allowSelfJoin;
        await db.SaveChangesAsync(cancellationToken);

        try { await queueNotifier.QueueChangedAsync(eventId, cancellationToken); }
        catch (Exception ex) { logger.LogWarning(ex, "Queue event {EventId} updated, but notification failed.", eventId); }

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
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

        var queueEvent = await db.QueueEvents.FindAsync([eventId], cancellationToken)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        if (!queueEvent.AllowSelfJoin)
            throw new ForbiddenException("Self-enrollment is disabled for this queue.", "queue.self_join_disabled");

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
                var latest = await LatestEntryQuery(eventId, studentId).AsNoTracking().FirstOrDefaultAsync(ct);
                if (latest?.Status is QueueEntryStatus.Waiting or QueueEntryStatus.Checking)
                    throw AlreadyJoined();
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
        // Выходят из последнего круга; прошлые круги (и их результат) остаются.
        var entry = await LatestEntryQuery(eventId, studentId).Include(e => e.QueueEvent).FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("You are not in this queue.", "queue.not_joined");
        // Без самозаписи встать обратно нельзя — значит, и уйти самому тоже.
        if (!entry.QueueEvent.AllowSelfJoin || entry.Status is QueueEntryStatus.Checking or QueueEntryStatus.Finished)
            throw new ConflictException("You cannot leave this queue now: it was filled by the teacher, or you are being checked or finished.", "queue.leave_unavailable");
        db.QueueEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        await NotifyBestEffortAsync(new QueueEntryUpdateDto(eventId, studentId, QueueEntryStatus.Finished, null), cancellationToken);
    }

    public async Task<List<QueueEventDto>> GetMyEventsAsync(Guid studentId, bool includePast, CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - ActiveEventGracePeriod;
        var events = await db.QueueEvents.AsNoTracking()
            .Where(qe => includePast || qe.EventDateTime > threshold)
            .Where(qe => qe.Participants.Any(p => p.StudentId == studentId))
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new { qe.Id })
            .ToListAsync(cancellationToken);
        var result = new List<QueueEventDto>(events.Count);
        foreach (var queueEvent in events)
        {
            var details = await GetDetailsAsync(queueEvent.Id, null, cancellationToken);
            var waiting = QueueOrder.Waiting(details.Participants);
            var me = details.Participants.Where(p => p.StudentId == studentId).MaxBy(p => p.Round);
            var position = waiting.FindIndex(p => p.StudentId == studentId);
            result.Add(new QueueEventDto(details.EventId, details.EventName, details.EventDateTime, details.AllowSelfJoin,
                details.SubjectName, me?.Status, position >= 0 ? position + 1 : null, waiting.Count));
        }
        return result;
    }

    public async Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default)
    {
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
            await accessService.EnsureSubjectAllowedAsync(adminId, entry.QueueEvent.SubjectId, ct);
            if (entry.Student.GroupId is int currentGroupId)
                await accessService.EnsureGroupAllowedAsync(adminId, currentGroupId, ct);

            var existingTracked = db.QueueEntries.Local.FirstOrDefault(qu => qu.Id == current.Id);
            if (existingTracked is not null) db.QueueEntries.Entry(existingTracked).State = EntityState.Detached;
            db.QueueEntries.Attach(current);
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
