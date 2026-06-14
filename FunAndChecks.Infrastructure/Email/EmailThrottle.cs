using System.Collections.Concurrent;
using FunAndChecks.Application.Common.Interfaces;

namespace FunAndChecks.Infrastructure.Email;

/// <summary>
/// In-memory троттлинг: не чаще одного письма в минуту на адрес. Singleton.
/// </summary>
public class EmailThrottle : IEmailThrottle
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string email, out TimeSpan retryAfter)
    {
        var now = DateTime.UtcNow;
        var key = email.Trim().ToLowerInvariant();

        while (true)
        {
            if (_lastSent.TryGetValue(key, out var last))
            {
                var elapsed = now - last;
                if (elapsed < Window)
                {
                    retryAfter = Window - elapsed;
                    return false;
                }

                if (_lastSent.TryUpdate(key, now, last))
                {
                    retryAfter = TimeSpan.Zero;
                    return true;
                }
            }
            else if (_lastSent.TryAdd(key, now))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }
    }
}
