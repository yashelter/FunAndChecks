using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Резервное копирование БД — /api/admin/backup (только супер-админ).</summary>
public class BackupApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task<BackupResultDto> CreateAsync(CancellationToken ct = default) =>
        PostAsync<BackupResultDto>("api/admin/backup", new { }, ct);
}
