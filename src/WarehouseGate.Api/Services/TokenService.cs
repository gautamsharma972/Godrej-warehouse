using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

public class JwtOptions
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryHours { get; set; } = 12;
}

public class TokenService
{
    private readonly JwtOptions _options;

    public TokenService(JwtOptions options)
    {
        _options = options;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(ApplicationUser user)
    {
        var expires = DateTime.UtcNow.AddHours(_options.ExpiryHours);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("displayName", user.DisplayName),
            // Read by HttpContextCurrentTenantProvider to scope every DbContext query for this
            // request. Empty for PlatformAdmin, who isn't scoped to any single organization.
            new Claim("orgId", user.OrganizationId?.ToString() ?? "")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
