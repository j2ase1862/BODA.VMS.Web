namespace BODA.VMS.Web.Client.Models;

/// <summary>글라스 '입고 확정'(적치) 요청.</summary>
public class InboundConfirmRequest
{
    /// <summary>스캔한 제품 바코드.</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>이번 확정 수량(글라스 PoC는 기본 1).</summary>
    public int Qty { get; set; } = 1;
}

/// <summary>
/// 입고 확정 결과 + SignalR 실시간 푸시("InboundConfirmed") 페이로드.
/// 출고의 PickConfirmResult 와 대칭.
/// </summary>
public class InboundConfirmResult
{
    public bool Confirmed { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>대상 WarehouseItem Id — 관리 화면에서 해당 행을 즉시 갱신.</summary>
    public int WarehouseItemId { get; set; }

    public string Barcode { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string LocationText { get; set; } = string.Empty;

    /// <summary>이번 확정 수량.</summary>
    public int Qty { get; set; }

    /// <summary>확정 후 누적 재고.</summary>
    public int StockAfter { get; set; }

    public DateTime ConfirmedAt { get; set; }
}
