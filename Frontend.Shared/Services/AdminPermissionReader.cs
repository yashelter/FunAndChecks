using Frontend.Shared.Api;
using Frontend.Shared.Models;

namespace Frontend.Shared.Services;

public sealed class AdminPermissionReader(MeApi meApi)
{
    public async Task<AdminAccessDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await meApi.GetMyAccessAsync(cancellationToken);
        }
        catch (ApiException)
        {
            return null;
        }
    }
}
