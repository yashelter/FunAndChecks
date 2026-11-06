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

    /// <summary>История попыток студента по всем задачам предмета: id задачи → попытки. Задач без попыток в ответе нет.</summary>
    Task<Dictionary<int, List<SubmissionLogDto>>> GetSubjectLogsAsync(Guid adminId, Guid studentId, int subjectId, CancellationToken cancellationToken = default);
}
