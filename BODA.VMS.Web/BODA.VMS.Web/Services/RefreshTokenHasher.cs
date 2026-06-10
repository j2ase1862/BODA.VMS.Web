using System.Security.Cryptography;
using System.Text;

namespace BODA.VMS.Web.Services;

/// <summary>
/// refresh token 생성/해시. raw 토큰은 256-bit 고엔트로피 난수라 패스워드와 달리
/// salt/BCrypt 없이 SHA-256 단방향 해시로 충분 (사전 공격 불가). DB 에는 해시만 저장.
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>URL-safe 256-bit 난수 토큰 (base64url, 패딩 제거).</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>raw 토큰의 SHA-256 base64 해시 — DB 조회 키.</summary>
    public static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(bytes);
    }
}
