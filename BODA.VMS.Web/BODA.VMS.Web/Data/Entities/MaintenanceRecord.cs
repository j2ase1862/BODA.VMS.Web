using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>예방 보전 실제 수행 기록. Schedule.NextDueAt을 갱신하는 트리거 역할.</summary>
public class MaintenanceRecord
{
    [Key]
    public int Id { get; set; }

    public int ScheduleId { get; set; }

    /// <summary>실제 수행 라인. Schedule이 ClientId=NULL이면 여기서 어떤 라인에 수행했는지 명시.</summary>
    public int? ClientId { get; set; }

    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

    /// <summary>실 소요 시간(분). 가동률 분석 정확도 향상.</summary>
    public int? ActualDurationMinutes { get; set; }

    public int? PerformedByUserId { get; set; }

    [MaxLength(100)]
    public string? PerformedByName { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>수행 전후 NextDueAt 비교 — 감사 추적</summary>
    public DateTime PreviousDueAt { get; set; }
    public DateTime NewDueAt { get; set; }

    [ForeignKey(nameof(ScheduleId))]
    public MaintenanceSchedule Schedule { get; set; } = null!;

    [ForeignKey(nameof(ClientId))]
    public VisionClient? Client { get; set; }
}
