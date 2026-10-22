namespace FunAndChecks.Application.Common.Interfaces;

/// <summary>Хранилище байтов вложений. Ключ формирует само хранилище; путь на диске наружу не выходит.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Подписанные ссылки на вложения: &lt;img src&gt; и скачивание не умеют передавать Bearer-токен,
/// поэтому доступ проверяется при выдаче ссылки, а сама ссылка ограничена по времени.
/// </summary>
public interface IAttachmentUrlSigner
{
    string CreateUrl(Guid attachmentId);
    bool Verify(Guid attachmentId, long expiresUnixSeconds, string? signature);
}
