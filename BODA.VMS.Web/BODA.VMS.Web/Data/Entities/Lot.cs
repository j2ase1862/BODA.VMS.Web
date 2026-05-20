using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 로트(Lot). 한 WorkOrder는 시간/원자재 변경 등으로 여러 Lot으로 분할 생산될 수 있습니다.
/// LotNumber 채번 규칙: {YYYYMMDD}-{OrderNo}-{Sequence:D3}
///
/// 추적성 표준 요구사항(IATF 16949, ISO 9001 등) 충족을 위해 모든 검사 결과는
/// LotId를 통해 한 Lot에 귀속됩니다.
/// </summary>
public class Lot
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string LotNumber { get; set; } = string.Empty;

    public int WorkOrderId { get; set; }

    /// <summary>
    /// 같은 WorkOrder 내에서 1부터 증가하는 일련번호. LotNumber 채번에 사용됩니다.
    /// </summary>
    public int Sequence { get; set; }

    public int Quantity { get; set; }

    public int PassCount { get; set; }

    public int NgCount { get; set; }

    /// <summary>
    /// Open / Closed
    /// </summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = LotStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ClosedAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public WorkOrder WorkOrder { get; set; } = null!;
}

public static class LotStatus
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}
