using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 불량 코드 마스터. InspectionHistory.NgCode가 이 테이블의 Code를 참조하면
/// 파레토 분석/품질 리포트에서 의미 있는 분류가 가능합니다.
///
/// Severity: Critical(치명) / Major(중대) / Minor(경미)
/// </summary>
public class DefectCode
{
    [Key]
    public int Id { get; set; }

    /// <summary>InspectionHistory.NgCode와 매칭되는 코드 (예: "2001", "BL-001")</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    /// <summary>분류 (예: Pattern, Blob, Dimension, Surface, Mechanical)</summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>심각도: Critical / Major / Minor</summary>
    [Required, MaxLength(20)]
    public string Severity { get; set; } = DefectSeverity.Major;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}

public static class DefectSeverity
{
    public const string Critical = "Critical";
    public const string Major = "Major";
    public const string Minor = "Minor";
}
