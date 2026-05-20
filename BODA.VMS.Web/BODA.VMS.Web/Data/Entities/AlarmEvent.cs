using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 알람 이벤트 영구 저장. 발생 → Acknowledge → Resolve 3단계 워크플로.
/// ISA-18.2 (Alarm Management Lifecycle) 기반.
/// </summary>
public class AlarmEvent
{
    [Key]
    public int Id { get; set; }

    /// <summary>NULL이면 시스템 알람 (특정 Client 무관)</summary>
    public int? ClientId { get; set; }

    /// <summary>NG / Offline / HighNgRate / Other</summary>
    [Required, MaxLength(30)]
    public string AlarmType { get; set; } = AlarmEventType.NG;

    /// <summary>Critical / Major / Minor / Info</summary>
    [Required, MaxLength(20)]
    public string Severity { get; set; } = AlarmSeverity.Major;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Message { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // === Acknowledge (확인) ===
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedBy { get; set; }
    [MaxLength(100)]
    public string? AcknowledgedByName { get; set; }

    // === Resolve (해제 + 조치 내용) ===
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
    [MaxLength(100)]
    public string? ResolvedByName { get; set; }
    [MaxLength(1000)]
    public string? Resolution { get; set; }

    // === Cross-reference (선택) ===
    public int? RelatedHistoryId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient? Client { get; set; }
}

public static class AlarmEventType
{
    public const string NG = "NG";
    public const string Offline = "Offline";
    public const string HighNgRate = "HighNgRate";
    public const string Other = "Other";
}

public static class AlarmSeverity
{
    public const string Critical = "Critical";
    public const string Major = "Major";
    public const string Minor = "Minor";
    public const string Info = "Info";
}
