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
    Task DeleteAsync(Guid adminId, int subjectId, CancellationToken cancellationToken = default);

    Task<List<TaskDto>> GetTasksAsync(int subjectId, CancellationToken cancellationToken = default);

    /// <summary>Задания предмета со статусом последней сдачи конкретного студента.</summary>
    Task<List<TaskWithStatusDto>> GetTasksWithStatusAsync(int subjectId, Guid studentId, CancellationToken cancellationToken = default);
    Task<TaskDto> CreateTaskAsync(Guid adminId, int subjectId, CreateTaskRequest request, CancellationToken cancellationToken = default);
    Task<TaskDto> UpdateTaskAsync(Guid adminId, int taskId, UpdateTaskRequest request, CancellationToken cancellationToken = default);
    Task DeleteTaskAsync(Guid adminId, int taskId, CancellationToken cancellationToken = default);

    /// <summary>Группы заданий предмета (в порядке отображения).</summary>
    Task<List<TaskGroupDto>> GetTaskGroupsAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<TaskGroupDto> CreateTaskGroupAsync(Guid adminId, int subjectId, CreateTaskGroupRequest request, CancellationToken cancellationToken = default);
    Task<TaskGroupDto> UpdateTaskGroupAsync(Guid adminId, int taskGroupId, UpdateTaskGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteTaskGroupAsync(Guid adminId, int taskGroupId, CancellationToken cancellationToken = default);
}
