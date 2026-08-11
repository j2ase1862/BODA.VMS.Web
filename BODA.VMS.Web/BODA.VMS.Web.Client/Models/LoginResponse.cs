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

    /// <summary>임시 비밀번호 상태 — true 면 클라이언트가 /change-password 로 유도 (2026-08-11).</summary>
    public bool MustChangePassword { get; set; }
}
