namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>
/// Ограничивает частоту отправки писем на один адрес (не чаще одного письма в минуту).
/// </summary>
public interface IEmailThrottle
{
    /// <summary>
    /// Пытается «занять» окно отправки для адреса. true — можно слать; false — рано,
    /// <paramref name="retryAfter"/> — сколько ждать.
    /// </summary>
    bool TryAcquire(string email, out TimeSpan retryAfter);
}
