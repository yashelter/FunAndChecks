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
    IValidator<CreateQueueEventRequest> createEventValidator)
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
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime))
            .ToListAsync(cancellationToken);
    }

    public Task<List<QueueEventDto>> GetAllEventsAsync(CancellationToken cancellationToken = default) =>
        db.QueueEvents
            .OrderBy(qe => qe.EventDateTime)
            .Select(qe => new QueueEventDto(qe.Id, qe.Name, qe.EventDateTime))
            .ToListAsync(cancellationToken);

    public async Task<QueueDetailsDto> GetDetailsAsync(int eventId, CancellationToken cancellationToken = default)
    {
        var details = await db.QueueEvents
            .Where(qe => qe.Id == eventId)
            .Select(qe => new QueueDetailsDto(
                qe.Id,
                qe.Name,
                qe.Subject.Name,
                qe.SubjectId,
                qe.EventDateTime,
                qe.Participants
                    .OrderBy(p => p.JoinedAt)
                    .Select(p => new QueueParticipantDto(
                        p.StudentId,
                        p.Student.FirstName,
                        p.Student.LastName,
                        p.Student.Group != null ? p.Student.Group.Name : "N/A",
                        p.Student.Submissions
                            .Where(s => s.Status == SubmissionStatus.Accepted && s.Task.SubjectId == qe.SubjectId)
                            .Select(s => s.Task)
                            .Distinct()
                            .Sum(t => t.MaxPoints),
                        p.Status,
                        p.CurrentAdmin != null ? p.CurrentAdmin.FirstName : null
                    )).ToList()
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return details ?? throw new NotFoundException($"Queue event with ID {eventId} not found.");
    }

    public async Task<QueueEventDto> CreateEventAsync(CreateQueueEventRequest request, CancellationToken cancellationToken = default)
    {
        await createEventValidator.ValidateAndThrowAsync(request, cancellationToken);

        var subjectExists = await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
            throw new NotFoundException($"Subject with ID {request.SubjectId} not found.");

        var queueEvent = new QueueEvent
        {
            Name = request.Name,
            EventDateTime = request.EventDateTime,
            SubjectId = request.SubjectId,
        };
        db.QueueEvents.Add(queueEvent);
        await db.SaveChangesAsync(cancellationToken);

        return new QueueEventDto(queueEvent.Id, queueEvent.Name, queueEvent.EventDateTime);
    }

    public async Task JoinAsync(int eventId, Guid studentId, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.FindAsync([studentId], cancellationToken)
                      ?? throw new NotFoundException("Student not found.");

        if (student.GroupId == null)
            throw new ForbiddenException("You are not assigned to any group.");

        var eventExists = await db.QueueEvents.AnyAsync(qe => qe.Id == eventId, cancellationToken);
        if (!eventExists)
            throw new NotFoundException($"Queue event with ID {eventId} not found.");

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
