using System.Net.Http.Headers;
using System.Net.Http.Json;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Резервное копирование БД — /api/admin/backup (только супер-админ).</summary>
public class BackupApi(HttpClient http, IStringLocalizer<AppStrings> loc, IHttpClientFactory httpFactory) : ApiClientBase(http, loc)
{
    /// <summary>Предел загрузки бэкапа — совпадает с серверным.</summary>
    public const long MaxUploadBytes = 2L * 1024 * 1024 * 1024;

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

    /// <summary>
    /// Загружает дамп и необязательный архив вложений. Через отдельный клиент с долгим таймаутом:
    /// большой файл не успевает уйти за стандартные 100 секунд.
    /// </summary>
    public Task<BackupFileDto> UploadAsync(Stream dump, string dumpName, Stream? attachments, string? attachmentsName,
        CancellationToken ct = default) => GuardAsync(async () =>
    {
        using var form = new MultipartFormDataContent();
        using var dumpContent = new StreamContent(dump);
        dumpContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(dumpContent, "dump", dumpName);
        using var attachmentsContent = attachments is null ? null : new StreamContent(attachments);
        if (attachmentsContent is not null)
        {
            attachmentsContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(attachmentsContent, "attachments", attachmentsName ?? "attachments.zip");
        }
        var client = httpFactory.CreateClient(DependencyInjection.LongRunningApiClient);
        using var response = await client.PostAsync("api/admin/backup/upload", form, ct);
        await response.EnsureSuccessAsync(Loc);
        return (await response.Content.ReadFromJsonAsync<BackupFileDto>(ct))!;
    }, Loc, ct);

    /// <summary>Запускает восстановление из дампа; ход — через <see cref="GetRestoreStatusAsync"/>.</summary>
    public Task<RestoreStatusDto> RestoreAsync(string fileName, CancellationToken ct = default) =>
        PostAsync<RestoreStatusDto>($"{FileUrl(fileName)}/restore", new { }, ct);

    public Task<RestoreStatusDto> GetRestoreStatusAsync(CancellationToken ct = default) =>
        GetAsync<RestoreStatusDto>("api/admin/backup/restore/status", ct);

    private static string FileUrl(string fileName) => $"api/admin/backup/{Uri.EscapeDataString(fileName)}";
}
