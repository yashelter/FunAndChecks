using System;
using Microsoft.Extensions.Caching.Memory;
using FunAndChecks.Application.Common.Interfaces;

namespace FunAndChecks.Infrastructure.Email;

/// <summary>
/// In-memory троттлинг: не чаще одного письма в минуту на адрес. Singleton.
/// </summary>
public class EmailThrottle : IEmailThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly IMemoryCache _cache;
    private readonly object _lock = new();

    public EmailThrottle(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryAcquire(string email, out TimeSpan retryAfter)
    {
        var key = email.Trim().ToLowerInvariant();

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out DateTime lastSent))
            {
                var elapsed = DateTime.UtcNow - lastSent;
                if (elapsed < Window)
                {
                    retryAfter = Window - elapsed;
                    return false;
                }
            }

            _cache.Set(key, DateTime.UtcNow, Window);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }
}
