using System.Security.Cryptography;
using System.Text;
using FunAndChecks.Application.Backups;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Backup;

/// <summary>
/// HMAC-подпись ссылок на скачивание бэкапа. Ключ выводится из Jwt:Key со своей меткой: подпись нельзя
/// использовать ни как токен, ни как ссылку на вложение, и наоборот.
/// </summary>
public sealed class HmacBackupDownloadLinks : IBackupDownloadLinks
{
    /// <summary>Ссылка нужна только чтобы браузер начал скачивание сразу после выдачи.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public HmacBackupDownloadLinks(IOptions<JwtOptions> jwt, TimeProvider? time = null)
    {
        _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), "funandchecks:backup-download:v1"u8);
        _time = time ?? TimeProvider.System;
    }

    public string CreateUrl(string fileName)
    {
        var expires = _time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        return $"api/admin/backup/download/{Uri.EscapeDataString(fileName)}?exp={expires}&sig={Sign(fileName, expires)}";
    }

    public bool Verify(string fileName, long expiresUnixSeconds, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || expiresUnixSeconds < _time.GetUtcNow().ToUnixTimeSeconds()) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(fileName, expiresUnixSeconds));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private string Sign(string fileName, long expires)
    {
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{fileName}:{expires}"));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
