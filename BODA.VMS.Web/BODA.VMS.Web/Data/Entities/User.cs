using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Role { get; set; } = "Pending"; // Admin, User, Guest, Pending

    public bool IsApproved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAt { get; set; }

    /// <summary>연속 로그인 실패 횟수 — 성공 또는 잠금 시 0 으로 리셋.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>이 시각 전까지 로그인 차단 (UTC). null 이면 잠금 없음.</summary>
    public DateTime? LockoutUntil { get; set; }

    /// <summary>
    /// 다음 로그인 시 비밀번호 변경 강제 (임시 비밀번호 발급 시 true — 2026-08-11).
    /// 변경 완료(/api/auth/change-password) 시 false 로 해제.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
