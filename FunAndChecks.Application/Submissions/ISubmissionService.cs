namespace FunAndChecks.Application.Submissions;

public interface ISubmissionService
{
    Task CreateAsync(Guid adminId, CreateSubmissionRequest request, CancellationToken cancellationToken = default);

    /// <summary>История попыток сдачи задания студентом (старые — первыми).</summary>
    Task<List<SubmissionLogDto>> GetLogAsync(Guid studentId, int taskId, CancellationToken cancellationToken = default);
}
