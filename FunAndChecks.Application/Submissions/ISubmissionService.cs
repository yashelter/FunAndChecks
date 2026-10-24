using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Submissions;

public interface ISubmissionService
{
    Task CreateAsync(Guid adminId, CreateSubmissionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет свою последнюю отметку по задаче, если она свежая (ошибочный клик).
    /// Возвращает статус, который стал текущим.
    /// </summary>
    Task<SubmissionStatus> UndoLatestAsync(Guid adminId, Guid studentId, int taskId, CancellationToken cancellationToken = default);

    /// <summary>История попыток сдачи задания студентом (старые — первыми).</summary>
    Task<List<SubmissionLogDto>> GetLogAsync(Guid adminId, Guid studentId, int taskId, CancellationToken cancellationToken = default);
}
