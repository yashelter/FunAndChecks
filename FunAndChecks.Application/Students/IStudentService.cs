using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Queues;
using FunAndChecks.Application.Subjects;

namespace FunAndChecks.Application.Students;

public interface IStudentService
{
    Task<StudentDto> GetAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<StudentDetailsDto> GetDetailsAsync(Guid adminId, Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Профиль текущего пользователя — работает и для студента, и для админа.</summary>
    Task<MeDto> GetMeAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<SubjectDto>> GetMySubjectsAsync(Guid studentId, CancellationToken cancellationToken = default);

    Task<GroupDto> GetMyGroupAsync(Guid studentId, CancellationToken cancellationToken = default);

    /// <summary>Все студенты, чьи группы имеют доступ к предмету (для админа).</summary>
    Task<List<StudentDetailsDto>> GetStudentsBySubjectAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Поиск студентов по фамилии/имени (для админа).</summary>
    Task<List<StudentDetailsDto>> SearchStudentsAsync(Guid adminId, string query, CancellationToken cancellationToken = default);

    /// <summary>Админ задаёт цвет заливки ячейки студента (null — убрать заливку).</summary>
    Task SetColorAsync(Guid adminId, Guid studentId, SetStudentColorRequest request, CancellationToken cancellationToken = default);

    /// <summary>События, на которые студент записан (по умолчанию — активные).</summary>
    Task<List<QueueEventDto>> GetMyQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default);

    /// <summary>События, доступные группе студента (по умолчанию — активные).</summary>
    Task<List<QueueEventDto>> GetAvailableQueueEventsAsync(Guid studentId, bool includePast = false, CancellationToken cancellationToken = default);

    /// <summary>Редактирование учётки и профиля студента (ФИО, группа, email, пароль).</summary>
    Task UpdateStudentAccountAsync(Guid adminId, Guid studentId, UpdateStudentAccountRequest request, CancellationToken cancellationToken = default);

    Task SetPreferredCultureAsync(Guid userId, string culture, CancellationToken cancellationToken = default);
}
