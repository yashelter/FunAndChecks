using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Subjects;

namespace FunAndChecks.Application.Students;

public interface IStudentService
{
    Task<StudentDto> GetAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<StudentDetailsDto> GetDetailsAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Профиль текущего пользователя — работает и для студента, и для админа.</summary>
    Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Обновляет редактируемые студентом поля своего профиля (GitHub, цвет).</summary>
    Task UpdateMyProfileAsync(Guid studentId, UpdateMyProfileRequest request, CancellationToken cancellationToken = default);

    Task<List<SubjectDto>> GetMySubjectsAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<GroupDto> GetMyGroupAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Все студенты, чьи группы имеют доступ к предмету (для админа).</summary>
    Task<List<StudentDetailsDto>> GetStudentsBySubjectAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Активные события, на которые студент уже записан.</summary>
    Task<List<QueueEventDto>> GetMyQueueEventsAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Активные события, доступные группе студента для записи.</summary>
    Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(Guid studentId, CancellationToken cancellationToken = default);
}
