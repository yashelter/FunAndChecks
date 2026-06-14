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

    Task<List<SubjectDto>> GetMySubjectsAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<GroupDto> GetMyGroupAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Все студенты, чьи группы имеют доступ к предмету (для админа).</summary>
    Task<List<StudentDetailsDto>> GetStudentsBySubjectAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Поиск студентов по фамилии/имени (для админа).</summary>
    Task<List<StudentDetailsDto>> SearchStudentsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Админ задаёт цвет заливки ячейки студента (null — убрать заливку).</summary>
    Task SetColorAsync(Guid studentId, SetStudentColorRequest request, CancellationToken cancellationToken = default);

    /// <summary>События, на которые студент записан (по умолчанию — активные).</summary>
    Task<List<QueueEventDto>> GetMyQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default);

    /// <summary>События, доступные группе студента (по умолчанию — активные).</summary>
    Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default);
}
