using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Резервное копирование БД — /api/admin/backup (только супер-админ).</summary>
public class BackupApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<BackupResultDto> CreateAsync(CancellationToken ct = default) =>
        PostAsync<BackupResultDto>("api/admin/backup", new { }, ct);

    public Task<List<BackupFileDto>> ListAsync(CancellationToken ct = default) =>
        GetAsync<List<BackupFileDto>>("api/admin/backup", ct);

    public new Task DeleteAsync(string fileName, CancellationToken ct = default) =>
        base.DeleteAsync(FileUrl(fileName), ct);

    // Keep the response and its stream alive until the browser has consumed the file.
    public Task DownloadAsync(string fileName, Func<Stream, Task> saveFile, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            using var response = await Http.GetAsync(FileUrl(fileName), HttpCompletionOption.ResponseHeadersRead, ct);
            await response.EnsureSuccessAsync(Loc);
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await saveFile(stream);
            return true;
        }, Loc, ct);

    private static string FileUrl(string fileName) => $"api/admin/backup/{Uri.EscapeDataString(fileName)}";
}
