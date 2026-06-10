using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// JWT refresh token — 짧은 수명의 access token 을 재로그인 없이 재발급받는 장기 자격.
/// raw 토큰은 클라이언트에만 1 회 전달되고 DB 에는 SHA-256 해시만 저장 (탈취 시 재사용 방지).
/// 로테이션: /api/auth/refresh 시 기존 토큰을 폐기하고 새 토큰을 발급 — 재사용 탐지 기반.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>raw 토큰의 SHA-256 base64 해시 (원문 미저장).</summary>
    [Required, MaxLength(100)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>폐기 시각 — null 이면 유효. 로그아웃 / 로테이션 / 탈취 대응 시 설정.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>로테이션으로 이 토큰을 대체한 새 토큰의 해시 (감사 추적용).</summary>
    [MaxLength(100)]
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>폐기되지 않았고 아직 만료 전이면 유효.</summary>
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
