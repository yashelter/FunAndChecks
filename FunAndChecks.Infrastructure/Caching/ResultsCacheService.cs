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
    private readonly ConcurrentDictionary<int, Lazy<SemaphoreSlim>> _locks = new();

    public SubjectResultsDto? GetResults(int subjectId) =>
        _cache.TryGetValue(subjectId, out var results) ? results : null;

    public async Task<SubjectResultsDto> GetOrAddAsync(int subjectId, Func<Task<SubjectResultsDto>> factory)
    {
        if (_cache.TryGetValue(subjectId, out var cached))
            return cached;

        var semaphore = _locks.GetOrAdd(subjectId, _ => new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(1, 1))).Value;
        await semaphore.WaitAsync();
        try
        {
            if (_cache.TryGetValue(subjectId, out cached))
                return cached;

            var results = await factory();
            _cache[subjectId] = results;
            return results;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void UpdateResults(int subjectId, SubjectResultsDto results) =>
        _cache[subjectId] = results;

    public void Invalidate(int subjectId) =>
        _cache.TryRemove(subjectId, out _);

    public void InvalidateAll() =>
        _cache.Clear();
}
