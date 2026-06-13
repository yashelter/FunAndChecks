using System.Security.Claims;

namespace FunAndChecks.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? throw new InvalidOperationException("Token does not contain a user id claim.");
        return Guid.Parse(id);
    }
}
