namespace BODA.VMS.Web.Client.Models;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>refresh token (raw) — 클라이언트 저장, access token 만료 시 /api/auth/refresh 에 제출.
    /// refresh 비활성(RefreshToken:ExpireDays&lt;=0) 시 빈 문자열.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>access token 만료 시각 (UTC) — 클라이언트가 사전 갱신 타이밍 판단에 사용.</summary>
    public DateTime AccessTokenExpiresAt { get; set; }
}
