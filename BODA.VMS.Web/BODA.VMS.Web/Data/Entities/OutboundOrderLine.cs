using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 출고 오더 라인 — 한 오더에서 집어야 할 품목 1건.
/// 피킹 위치는 여기 저장하지 않고 <see cref="WarehouseItem"/>(Barcode 매칭)에서 조회한다
/// (입고 위치 마스터 재사용).
/// </summary>
public class OutboundOrderLine
{
    [Key]
    public int Id { get; set; }

    /// <summary>FK → <see cref="OutboundOrder"/>.Id</summary>
    public int OrderId { get; set; }

    [Required, MaxLength(64)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(50)]  public string ItemCode { get; set; } = string.Empty;
    [MaxLength(200)] public string ItemName { get; set; } = string.Empty;

    /// <summary>지시 수량.</summary>
    public int Qty { get; set; }

    /// <summary>피킹 완료 수량 (1차 읽기 가이드에선 미사용, 2차 스캔검증용).</summary>
    public int PickedQty { get; set; }

    /// <summary>피킹 안내 순서.</summary>
    public int Seq { get; set; }
}
