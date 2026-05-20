using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 설비(Client) 상태 시계열 로그. 상태가 바뀔 때마다 1 row 추가.
/// 현재 상태는 EndedAt이 NULL인 row가 1개 — 그 시점부터 지금까지가 현재 상태입니다.
///
/// OEE 계산 기반: 기간 내 각 Status의 누적 시간을 집계.
/// </summary>
public class EquipmentStatusLog
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    /// <summary>Running / Idle / Down (SEMI E10 단순화 모델)</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = EquipmentStatus.Idle;

    public DateTime StartedAt { get; set; }

    /// <summary>NULL이면 현재 상태(진행중). 새 transition 발생 시 채워짐.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>다운타임 사유 등 메모</summary>
    [MaxLength(200)]
    public string? Reason { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;
}

public static class EquipmentStatus
{
    /// <summary>가동중 — heartbeat OK & 최근 검사 결과 수신</summary>
    public const string Running = "Running";

    /// <summary>대기중 — heartbeat OK이지만 일정 시간 검사 결과 없음</summary>
    public const string Idle = "Idle";

    /// <summary>정지 — heartbeat 끊김</summary>
    public const string Down = "Down";
}
