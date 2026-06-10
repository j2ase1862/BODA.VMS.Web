using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BODA.VMS.Web.Services;

public class JwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>access token 문자열만 반환 (하위 호환).</summary>
    public string GenerateToken(int userId, string username, string displayName, string role)
        => GenerateAccessToken(userId, username, displayName, role).Token;

    /// <summary>
    /// access token + 만료 시각(UTC) 반환. refresh token 흐름에서 클라이언트가
    /// AccessTokenExpiresAt 로 사전 갱신 타이밍을 판단하도록 만료 시각도 함께 노출.
    /// </summary>
    public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(
        int userId, string username, string displayName, string role)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim("DisplayName", displayName),
            new Claim(ClaimTypes.Role, role)
        };

        var expireMinutes = int.Parse(_config["Jwt:ExpireMinutes"] ?? "480");
        var expiresAt = DateTime.UtcNow.AddMinutes(expireMinutes);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
