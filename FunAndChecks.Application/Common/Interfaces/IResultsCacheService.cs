using FunAndChecks.Application.Results;

namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Кэш агрегированных таблиц результатов по предметам.
/// Заполняется лениво при чтении и инвалидируется при изменениях
/// (новая сдача, оценка, изменение задач/состава групп).
/// </summary>
public interface IResultsCacheService
{
    SubjectResultsDto? GetResults(int subjectId);

    Task<SubjectResultsDto> GetOrAddAsync(int subjectId, Func<Task<SubjectResultsDto>> factory);

    void UpdateResults(int subjectId, SubjectResultsDto results);

    void Invalidate(int subjectId);

    void InvalidateAll();
}
