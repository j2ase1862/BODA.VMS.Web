using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 현장 작업자 마스터. 웹 사용자(User)와 분리 — 작업자는 보통 사번+PIN으로
/// 키오스크에서 빠르게 로그인하며, 웹 권한이 없습니다.
/// PIN은 BCrypt 해시로 저장.
/// </summary>
public class Operator
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string EmployeeNumber { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>BCrypt 해시. 평문 PIN은 저장하지 않음.</summary>
    [Required, MaxLength(200)]
    public string PinHash { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Department { get; set; }

    /// <summary>
    /// 작업자 등급. VMS 측 메뉴/기능 가시성 제어용 — Web 인증과 무관.
    /// "Operator" (기본) / "Lead" (반장) / "Supervisor" (관리자).
    /// </summary>
    [Required, MaxLength(20)]
    public string Role { get; set; } = OperatorRole.Operator;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public static class OperatorRole
{
    public const string Operator = "Operator";
    public const string Lead = "Lead";
    public const string Supervisor = "Supervisor";
}
