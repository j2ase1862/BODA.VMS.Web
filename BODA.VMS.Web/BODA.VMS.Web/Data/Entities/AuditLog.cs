using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 마스터/설정 데이터 변경 감사 로그. EF SaveChanges 인터셉터가 자동 생성.
/// 21 CFR Part 11 / EU GMP Annex 11 준수의 핵심 — 이 테이블 자체는 INSERT 외 변경 금지.
/// </summary>
public class AuditLog
{
    [Key]
    public long Id { get; set; }

    /// <summary>변경된 엔티티 타입 이름 (예: "RecipeParameter")</summary>
    [Required, MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;

    /// <summary>엔티티 PK (복합키는 JSON 직렬화)</summary>
    [MaxLength(50)]
    public string? EntityId { get; set; }

    /// <summary>Create / Update / Delete / LoginSuccess / LoginFailed / Lockout</summary>
    [Required, MaxLength(20)]
    public string Action { get; set; } = string.Empty;

    /// <summary>변경된 속성들의 JSON: {"PropName": {"old": ..., "new": ...}}</summary>
    public string? Changes { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int? UserId { get; set; }

    [MaxLength(100)]
    public string? UserName { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }
}

public static class AuditAction
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";

    // 인증 이벤트 (EntityName="Auth" 웹 로그인 / "KioskAuth" 키오스크 PIN)
    public const string LoginSuccess = "LoginSuccess";
    public const string LoginFailed = "LoginFailed";
    public const string Lockout = "Lockout";
}
