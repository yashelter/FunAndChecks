using Frontend.Shared.Models;

namespace Frontend.Shared.Api;

/// <summary>Резервное копирование БД — /api/admin/backup (только супер-админ).</summary>
public class BackupApi(HttpClient http) : ApiClientBase(http)
{
    public Task<BackupResultDto> CreateAsync(CancellationToken ct = default) =>
        PostAsync<BackupResultDto>("api/admin/backup", new { }, ct);
}
