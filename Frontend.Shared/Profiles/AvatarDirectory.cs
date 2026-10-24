namespace Frontend.Shared.Profiles;

/// <summary>
/// Аватары для списков. Компоненты спрашивают по одному, а запрос к API уходит пачкой:
/// очередь в 50 человек — один GET вместо пятидесяти.
/// </summary>
public sealed class AvatarDirectory : IDisposable
{
    private const int BatchSize = 100;
    private readonly ProfileApi _api;
    private readonly Dictionary<Guid, Task<MediaDto?>> _known = [];
    private readonly Dictionary<Guid, TaskCompletionSource<MediaDto?>> _waiting = [];
    private bool _flushScheduled;

    public AvatarDirectory(ProfileApi api)
    {
        _api = api;
        _api.ProfileChanged += Clear;
    }

    public Task<MediaDto?> GetAsync(Guid userId)
    {
        if (_known.TryGetValue(userId, out var known)) return known;
        var waiter = new TaskCompletionSource<MediaDto?>();
        _waiting[userId] = waiter;
        _known[userId] = waiter.Task;
        if (!_flushScheduled)
        {
            _flushScheduled = true;
            _ = FlushAsync();
        }
        return waiter.Task;
    }

    private async Task FlushAsync()
    {
        // Даём отрисоваться всему списку, чтобы собрать его в один запрос.
        await Task.Delay(25);
        _flushScheduled = false;
        var batch = _waiting.ToList();
        _waiting.Clear();
        foreach (var chunk in batch.Chunk(BatchSize))
        {
            try
            {
                var found = await _api.AvatarsAsync(chunk.Select(c => c.Key).ToList());
                foreach (var (id, waiter) in chunk) waiter.TrySetResult(found.GetValueOrDefault(id));
            }
            catch (Exception)
            {
                // Аватар — украшение: без него показываем инициалы и спросим снова позже.
                foreach (var (id, waiter) in chunk)
                {
                    _known.Remove(id);
                    waiter.TrySetResult(null);
                }
            }
        }
    }

    private void Clear() => _known.Clear();

    public void Dispose() => _api.ProfileChanged -= Clear;
}
