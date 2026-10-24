using System.Security.Cryptography;
using System.Text;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Files;

/// <summary>
/// HMAC-подпись ссылок на вложения. Ключ выводится из Jwt:Key с отдельной меткой,
/// так что подпись ссылки нельзя использовать как токен и наоборот.
/// </summary>
public sealed class HmacAttachmentUrlSigner : IAttachmentUrlSigner
{
    /// <summary>Ссылка живёт несколько часов — хватает на открытую вкладку, утечка ссылки быстро протухает.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(6);

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public HmacAttachmentUrlSigner(IOptions<JwtOptions> jwt, TimeProvider? time = null)
    {
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), "funandchecks:attachments:v1"u8);
        _time = time ?? TimeProvider.System;
    }

    public string CreateUrl(Guid attachmentId)
    {
        // Окно округляется вверх до часа: одна и та же ссылка в пределах часа — браузер переиспользует кэш.
        var now = _time.GetUtcNow();
        var expires = now.Add(Lifetime).ToUnixTimeSeconds();
        expires += 3600 - expires % 3600;
        return $"api/attachments/{attachmentId}/content?exp={expires}&sig={Sign(attachmentId, expires)}";
    }

    public bool Verify(Guid attachmentId, long expiresUnixSeconds, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || expiresUnixSeconds < _time.GetUtcNow().ToUnixTimeSeconds()) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(attachmentId, expiresUnixSeconds));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(Guid attachmentId, long expires)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{attachmentId:N}:{expires}"));
        return Convert.ToBase64String(hash, 0, 18).Replace('+', '-').Replace('/', '_');
    }
}
