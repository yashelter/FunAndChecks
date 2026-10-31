namespace FunAndChecks.Application.Statistics;

public interface IReviewerStatisticsService
{
    /// <summary>
    /// Статистика по всем проверяющим: по всем предметам или по одному. Бросает NotFoundException, если предмета нет.
    /// </summary>
    Task<List<ReviewerStatsDto>> GetAsync(int? subjectId, CancellationToken cancellationToken = default);
}
