namespace FunAndChecks.Application.Statistics;

public interface IReviewerStatisticsService
{
    /// <summary>
    /// Статистика по всем проверяющим: по всем предметам или по одному. Бросает NotFoundException, если предмета нет.
    /// </summary>
    Task<List<ReviewerStatsDto>> GetAsync(int? subjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Расширенная сводка активности всех администраторов за период и/или по предмету.
    /// </summary>
    Task<List<AdminActivityStatsDto>> GetActivityAsync(
        int? subjectId,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default);
}
