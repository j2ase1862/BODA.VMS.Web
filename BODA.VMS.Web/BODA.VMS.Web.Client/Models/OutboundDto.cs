using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Client.Models;

// =====================================================================
//  글라스 피킹(읽기 가이드) 응답 DTO — /api/glass/pick-list
// =====================================================================

/// <summary>글라스가 받는 피킹 목록. 주문 1건 + 집어야 할 라인들 + 완료 후 출고 목적지.</summary>
public class PickListDto
{
    public string OrderNo { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? ShipTo { get; set; }

    /// <summary>피킹 완료 후 안내할 출고 목적지(도크/레인).</summary>
    public string? Destination { get; set; }

    public string Status { get; set; } = "Pending";
    public List<PickLineDto> Lines { get; set; } = new();
}

/// <summary>피킹 한 줄 — "어디서 무엇을 몇 개".</summary>
public class PickLineDto
{
    public int Seq { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Qty { get; set; }
    public int PickedQty { get; set; }

    /// <summary>피킹 위치 — WarehouseItem(Zone-Rack-Level-Bin)에서 조회. 미등록이면 빈 문자열.</summary>
    public string LocationText { get; set; } = string.Empty;
}

// =====================================================================
//  글라스 피킹 확정 / 출하 확정 (2차) — /api/glass/pick-confirm, /ship-confirm
// =====================================================================

/// <summary>스캔 검증 피킹 확정 요청 — 스캔 1회 = qty 1 기본.</summary>
public class PickConfirmRequest
{
    public string OrderNo { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public int Qty { get; set; } = 1;
}

/// <summary>피킹 확정 결과 — 글라스가 피드백 + 상태 갱신에 사용.</summary>
public class PickConfirmResult
{
    /// <summary>스캔한 바코드가 이 주문의 미완료 라인과 일치해 수량이 증가했는가.</summary>
    public bool Matched { get; set; }

    /// <summary>이미 전량 피킹된 라인을 또 스캔했는가(일치하나 증가 없음).</summary>
    public bool AlreadyComplete { get; set; }

    /// <summary>모든 라인 피킹 완료.</summary>
    public bool AllPicked { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>갱신된 피킹 목록(pickedQty 반영).</summary>
    public PickListDto PickList { get; set; } = new();
}

// =====================================================================
//  관리(등록/수정) DTO — /api/outbound-orders
// =====================================================================

public class OutboundOrderDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "주문번호는 필수입니다.")]
    [MaxLength(64)]
    public string OrderNo { get; set; } = string.Empty;

    [MaxLength(100)] public string? CustomerName { get; set; }
    [MaxLength(200)] public string? ShipTo { get; set; }
    [MaxLength(100)] public string? Destination { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public List<OutboundOrderLineDto> Lines { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>목록 표시용 — 총 라인 수(읽기 전용).</summary>
    public int LineCount => Lines.Count;
}

public class OutboundOrderLineDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "바코드는 필수입니다.")]
    [MaxLength(64)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(50)]  public string ItemCode { get; set; } = string.Empty;
    [MaxLength(200)] public string ItemName { get; set; } = string.Empty;

    public int Qty { get; set; } = 1;
    public int PickedQty { get; set; }
}
