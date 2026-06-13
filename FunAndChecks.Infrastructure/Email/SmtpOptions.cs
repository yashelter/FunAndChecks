namespace FunAndChecks.Infrastructure.Email;

/// <summary>
/// Настройки SMTP-отправки. Секция "Smtp".
/// Рассчитано на собственный почтовый сервер (Postfix на том же VDS):
/// без внешних сервисов и лимитов на число писем.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Хост SMTP-сервера. Для локального Postfix — "localhost".</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Порт. 25 — локальная отправка/релей, 587 — submission со STARTTLS.</summary>
    public int Port { get; set; } = 25;

    /// <summary>Использовать STARTTLS. Для локального релея обычно false.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Логин SMTP. Пусто — без аутентификации (доверенный локальный релей).</summary>
    public string? User { get; set; }

    /// <summary>Пароль SMTP.</summary>
    public string? Password { get; set; }

    /// <summary>Адрес отправителя, например "noreply@funandchecks.ru".</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Отображаемое имя отправителя.</summary>
    public string FromName { get; set; } = "FunAndChecks";
}
