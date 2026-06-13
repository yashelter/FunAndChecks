using System.Collections.Concurrent;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Results;

namespace FunAndChecks.Infrastructure.Caching;

/// <summary>
/// Потокобезопасный in-memory кэш таблиц результатов.
/// Заполняется лениво при чтении и инвалидируется по событиям изменений —
/// без фонового пересчёта по таймеру.
/// </summary>
public class ResultsCacheService : IResultsCacheService
{
    private readonly ConcurrentDictionary<int, SubjectResultsDto> _cache = new();

    public SubjectResultsDto? GetResults(int subjectId) =>
        _cache.TryGetValue(subjectId, out var results) ? results : null;

    public void UpdateResults(int subjectId, SubjectResultsDto results) =>
        _cache[subjectId] = results;

    public void Invalidate(int subjectId) =>
        _cache.TryRemove(subjectId, out _);

    public void InvalidateAll() =>
        _cache.Clear();
}
