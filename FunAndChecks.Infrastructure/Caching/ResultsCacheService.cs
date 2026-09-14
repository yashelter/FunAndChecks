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
    private readonly ConcurrentDictionary<int, long> _generations = new();
    private long _globalGeneration;

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

            while (true)
            {
                var subjectGeneration = _generations.GetOrAdd(subjectId, 0);
                var globalGeneration = Volatile.Read(ref _globalGeneration);
                var results = await factory();

                if (subjectGeneration == _generations.GetOrAdd(subjectId, 0) &&
                    globalGeneration == Volatile.Read(ref _globalGeneration))
                {
                    _cache[subjectId] = results;
                    return results;
                }

                if (_cache.TryGetValue(subjectId, out cached))
                    return cached;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void UpdateResults(int subjectId, SubjectResultsDto results)
    {
        _generations.AddOrUpdate(subjectId, 1, static (_, current) => current + 1);
        _cache[subjectId] = results;
    }

    public void Invalidate(int subjectId)
    {
        _generations.AddOrUpdate(subjectId, 1, static (_, current) => current + 1);
        _cache.TryRemove(subjectId, out _);
    }

    public void InvalidateAll()
    {
        Interlocked.Increment(ref _globalGeneration);
        _cache.Clear();
    }
}
