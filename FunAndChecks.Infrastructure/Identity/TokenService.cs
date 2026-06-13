using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FunAndChecks.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FunAndChecks.Infrastructure.Identity;

public class TokenService : ITokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public TokenService(UserManager<ApplicationUser> userManager, IOptions<JwtOptions> options)
    {
        _userManager = userManager;
        _options = options.Value;

        if (string.IsNullOrEmpty(_options.Key))
            throw new InvalidOperationException("Jwt:Key is not configured.");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
    }

    public async Task<string> CreateTokenAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
                   ?? throw new InvalidOperationException($"Account {userId} not found.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.NameId, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
        };

        var roles = await _userManager.GetRolesAsync(user);
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(_options.TokenLifetimeDays),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha512Signature),
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        tokenHandler.OutboundClaimTypeMap.Clear();

        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }
}
