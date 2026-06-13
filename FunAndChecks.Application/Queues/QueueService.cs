using FluentValidation;
using FunAndChecks.Application.Admins;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Domain.Entities;
using FunAndChecks.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Queues;

public class QueueService(
    IApplicationDbContext db,
    IQueueNotifier queueNotifier,
    IAdminAccessService accessService,
    IValidator<CreateQueueEventRequest> createEventValidator,
    IValidator<UpdateQueueEventRequest> updateEventValidator)
    : IQueueService
{
    /// <summary>Сколько времени событие остаётся видимым после своей даты.</summary>
    private static readonly TimeSpan ActiveEventGracePeriod = TimeSpan.FromDays(2);

    public Task<List<QueueEventDto>> GetActiveEventsAsync(CancellationToken cancellationToken = default)
    {
        var threshold = DateTime.UtcNow - ActiveEventGracePeriod;
        return db.QueueEvents
            .Where(qe => qe.EventDateTime > threshold)
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);
    }

    public Task<List<QueueEventDto>> GetAllEventsAsync(CancellationToken cancellationToken = default) =>
        db.QueueEvents
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime, qe.AllowSelfJoin))
            .ToListAsync(cancellationToken);

    public async Task<QueueDetailsDto> GetDetailsAsync(int eventId, CancellationToken cancellationToken = default)
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

        var entries = await db.QueueEntries
            .Where(p => p.QueueEventId == eventId)
            .OrderBy(p => p.JoinedAt)
            .Select(p => new
            {
                p.StudentId,
                p.Student.FirstName,
                p.Student.LastName,
                GroupName = p.Student.Group != null ? p.Student.Group.Name : "N/A",
                p.Status,
                AdminName = p.CurrentAdmin != null ? p.CurrentAdmin.FirstName : null,
                p.JoinedAt,
            })
            .ToListAsync(cancellationToken);

        // Баллы (сумма MaxPoints принятых задач предмета) считаем в памяти —
        // так избегаем непереводимого Distinct().Sum() и лишних подзапросов.
        var studentIds = entries.Select(e => e.StudentId).ToList();
        var accepted = await db.Submissions
            .Where(s => studentIds.Contains(s.StudentId)
                        && s.Status == SubmissionStatus.Accepted
                        && s.Task.SubjectId == queueEvent.SubjectId)
            .Select(s => new { s.StudentId, s.TaskId, s.Task.MaxPoints })
            .ToListAsync(cancellationToken);

        var pointsByStudent = accepted
            .GroupBy(a => a.StudentId)
            .ToDictionary(
                g => g.Key,
                g => g.DistinctBy(x => x.TaskId).Sum(x => x.MaxPoints));

        var participants = entries
            .Select(e => new QueueParticipantDto(
                e.StudentId,
                e.FirstName,
                e.LastName,
                e.GroupName,
                pointsByStudent.GetValueOrDefault(e.StudentId),
                e.Status,
                e.AdminName,
                e.JoinedAt))
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

    public async Task<QueueEventDto> CreateEventAsync(CreateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await createEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {request.SubjectId} not found.");

        // Авто-заполнение по группе фиксирует состав: самостоятельная запись отключается.
        var allowSelfJoin = request.AutoFillGroupId.HasValue ? false : request.AllowSelfJoin;

        var queueEvent = new QueueEvent
        {
            Name = request.Name,
            EventDateTime = request.EventDateTime,
            SubjectId = request.SubjectId,
            AllowSelfJoin = allowSelfJoin,
        };
        db.QueueEvents.Add(queueEvent);
        await db.SaveChangesAsync(cancellationToken);

        if (request.AutoFillGroupId is { } groupId)
        {
            var groupExists = await db.Groups.AnyAsync(g => g.Id == groupId, cancellationToken);
            if (!groupExists)
                throw new NotFoundException($"Group with ID {groupId} not found.");

            var studentIds = await db.Students
                .Where(s => s.GroupId == groupId)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var studentId in studentIds)
            {
                db.QueueEntries.Add(new QueueEntry
                {
                    QueueEventId = queueEvent.Id,
                    StudentId = studentId,
                    JoinedAt = now,
                    Status = QueueEntryStatus.Waiting,
                });
            }

            if (studentIds.Count > 0)
                await db.SaveChangesAsync(cancellationToken);
        }

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
    }

    public async Task<QueueEventDto> UpdateEventAsync(int eventId, UpdateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await updateEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var queueEvent = await db.QueueEvents.FindAsync([eventId], cancellationToken)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

        queueEvent.Name = request.Name;
        queueEvent.EventDateTime = request.EventDateTime;
        await db.SaveChangesAsync(cancellationToken);

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime, queueEvent.AllowSelfJoin);
    }

    public async Task DeleteEventAsync(int eventId, CancellationToken cancellationToken = default)
    {
        var queueEvent = await db.QueueEvents.FindAsync([eventId], cancellationToken)
                         ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");

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
            throw new ForbiddenException("Self-enrollment is disabled for this queue.");

        var isGroupAllowed = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .AnyAsync(qe => qe.Subject.GroupSubjects.Any(gs => gs.GroupId == student.GroupId.Value), cancellationToken);
        if (!isGroupAllowed)
            throw new ForbiddenException("Your group does not have access to the subject of this event.");

        var alreadyInQueue = await db.QueueEntries
            .AnyAsync(qu => qu.QueueEventId == eventId && qu.StudentId == studentId, cancellationToken);
        if (alreadyInQueue)
            throw new ConflictException("Student is already in the queue.");

        db.QueueEntries.Add(new QueueEntry
        {
            QueueEventId = eventId,
            StudentId = studentId,
            JoinedAt = DateTime.UtcNow,
            Status = QueueEntryStatus.Waiting,
        });
        await db.SaveChangesAsync(cancellationToken);

        await queueNotifier.QueueEntryUpdatedAsync(
            new QueueEntryUpdateDto(eventId, studentId, QueueEntryStatus.Waiting, null),
            cancellationToken);
    }

    public async Task UpdateParticipantStatusAsync(
        int eventId, Guid studentId, Guid adminId, QueueEntryStatus status,
        CancellationToken cancellationToken = default)
    {
        var entry = await db.QueueEntries
            .Include(qu => qu.QueueEvent)
            .FirstOrDefaultAsync(qu => qu.QueueEventId == eventId && qu.StudentId == studentId, cancellationToken)
            ?? throw new NotFoundException("Student not found in this queue.");

        var admin = await db.Admins.FindAsync([adminId], cancellationToken)
                    ?? throw new ForbiddenException("Admin profile not found.");

        await accessService.EnsureSubjectAllowedAsync(adminId, entry.QueueEvent.SubjectId, cancellationToken);

        entry.Status = status;
        entry.CurrentAdminId = admin.Id;
        await db.SaveChangesAsync(cancellationToken);

        await queueNotifier.QueueEntryUpdatedAsync(
            new QueueEntryUpdateDto(eventId, studentId, status, admin.FullName),
            cancellationToken);
    }
}
