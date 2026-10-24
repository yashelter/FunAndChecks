using System.Text.RegularExpressions;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Files;

public class AttachmentStorageOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Каталог с файлами вложений (в compose — том attachments-data).</summary>
    public string RootDirectory { get; set; } = "../attachments";

    /// <summary>Через сколько часов непривязанная загрузка считается брошенной.</summary>
    public int OrphanLifetimeHours { get; set; } = 24;

    public int CleanupIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Файлы на локальном диске: ключ вида yyyy/MM/{guid}. Ключ проверяется по шаблону,
/// поэтому выйти за пределы корня через «..» или абсолютный путь нельзя.
/// </summary>
public partial class LocalDiskFileStorage(IOptions<AttachmentStorageOptions> options) : IFileStorage
{
    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();

    private string Root => Path.GetFullPath(options.Value.RootDirectory);

    public async Task<string> SaveAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await content.CopyToAsync(file, cancellationToken);
            File.Move(temporary, path);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            Stream stream = new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            return Task.FromResult(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new NotFoundException("The file is no longer available.", "attachment.not_found");
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new InvalidOperationException("Invalid storage key.");
        return Path.Combine(Root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
