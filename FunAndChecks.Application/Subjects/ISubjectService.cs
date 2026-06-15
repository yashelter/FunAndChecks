using FunAndChecks.Application.Tasks;

namespace FunAndChecks.Application.Subjects;

public interface ISubjectService
{
    Task<List<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Предметы, видимые админу: без запрещённых супер-админом и без скрытых самим админом (архив).</summary>
    Task<List<SubjectDto>> GetVisibleForAdminAsync(Guid adminId, CancellationToken cancellationToken = default);

    Task<SubjectDto> GetAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<SubjectDto> CreateAsync(CreateSubjectRequest request, CancellationToken cancellationToken = default);
    Task<SubjectDto> UpdateAsync(Guid adminId, int subjectId, UpdateSubjectRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int subjectId, CancellationToken cancellationToken = default);

    Task<List<TaskDto>> GetTasksAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Задания предмета со статусом последней сдачи конкретного студента.</summary>
    Task<List<TaskWithStatusDto>> GetTasksWithStatusAsync(int subjectId, Guid studentId, CancellationToken cancellationToken = default);
    Task<TaskDto> CreateTaskAsync(Guid adminId, int subjectId, CreateTaskRequest request, CancellationToken cancellationToken = default);
    Task<TaskDto> UpdateTaskAsync(Guid adminId, int taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default);
    Task DeleteTaskAsync(int taskId, CancellationToken cancellationToken = default);
}
