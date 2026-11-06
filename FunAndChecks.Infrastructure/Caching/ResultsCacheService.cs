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
    /// <summary>
    /// Верхняя граница перестроений одного предмета за вызов: при непрерывном шторме
    /// инвалидаций лучше отдать свежепостроенный (возможно, чуть устаревший) результат,
    /// чем строить бесконечно.
    /// </summary>
    private const int MaxRebuildAttempts = 5;

    /// <summary>
    /// Число замков построения. Замки разделены по предметам полосами, а не созданы на каждый ID:
    /// запросы к произвольным (в том числе несуществующим) ID не должны увеличивать память.
    /// </summary>
    private const int LockStripes = 64;

    private readonly ConcurrentDictionary<int, SubjectResultsDto> _cache = new();
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockStripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    // Запись появляется только при инвалидации, то есть при реальном изменении данных предмета.
    // Отсутствие записи равнозначно поколению 0.
    private readonly ConcurrentDictionary<int, long> _generations = new();

    public SubjectResultsDto? GetResults(int subjectId) =>
        _cache.TryGetValue(subjectId, out var results) ? results : null;

    public async Task<SubjectResultsDto> GetOrAddAsync(int subjectId, Func<Task<SubjectResultsDto>> factory)
    {
        if (_cache.TryGetValue(subjectId, out var cached))
            return cached;

        var semaphore = _locks[(int)((uint)subjectId % LockStripes)];
        await semaphore.WaitAsync();
        try
        {
            if (_cache.TryGetValue(subjectId, out cached))
                return cached;

            for (var attempt = 1; ; attempt++)
            {
                var subjectGeneration = Generation(subjectId);
                var results = await factory();

                if (subjectGeneration == Generation(subjectId))
                {
                    _cache[subjectId] = results;
                    // Invalidate мог пройти между проверкой и публикацией: он сначала увеличивает поколение,
                    // затем удаляет запись. Если поколение уже другое, снимаем именно свою (устаревшую) запись.
                    if (subjectGeneration != Generation(subjectId))
                        _cache.TryRemove(new KeyValuePair<int, SubjectResultsDto>(subjectId, results));
                    return results;
                }

                if (_cache.TryGetValue(subjectId, out cached))
                    return cached;

                if (attempt >= MaxRebuildAttempts)
                {
                    // Не публикуем в кэш: последнее построение могло устареть
                    // во время шторма инвалидаций — просто отдаём его вызывающему.
                    return results;
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Invalidate(int subjectId)
    {
        _generations.AddOrUpdate(subjectId, 1, static (_, current) => current + 1);
        _cache.TryRemove(subjectId, out _);
    }

    public void Clear()
    {
        foreach (var subjectId in _cache.Keys.Concat(_generations.Keys).Distinct().ToList())
            Invalidate(subjectId);
    }

    private long Generation(int subjectId) => _generations.TryGetValue(subjectId, out var generation) ? generation : 0;
}
