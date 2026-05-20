using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 예방 보전(PM) 일정 마스터. 설비(Client)별 주기적 점검 계획.
/// ClientId가 NULL이면 모든 라인에 공통 적용되는 일정.
/// SEMI E10 (RAM 표준) — Scheduled Down 시간 계획 기반.
/// </summary>
public class MaintenanceSchedule
{
    [Key]
    public int Id { get; set; }

    /// <summary>NULL이면 전체 라인 공통</summary>
    public int? ClientId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>주기 (일 단위). 1=Daily, 7=Weekly, 30=Monthly, 90=Quarterly, 365=Yearly</summary>
    public int IntervalDays { get; set; } = 7;

    /// <summary>예상 소요 시간(분). 가동률 계산에 참고.</summary>
    public int EstimatedDurationMinutes { get; set; } = 30;

    /// <summary>마지막 수행 시각 (NULL=한 번도 수행 안 됨)</summary>
    public DateTime? LastPerformedAt { get; set; }

    /// <summary>다음 수행 예정. (LastPerformedAt ?? CreatedAt) + IntervalDays — 자동 계산이지만 수동 조정도 가능</summary>
    public DateTime NextDueAt { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient? Client { get; set; }

    public ICollection<MaintenanceRecord> Records { get; set; } = new List<MaintenanceRecord>();
}
