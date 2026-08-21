namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// VmsPublicHub "WorkOrderUpdated" / "WorkOrderCompleted" 브로드캐스트 페이로드
/// (계약: Hubs/VmsPublicHub.cs 주석). 결과 업로드/수동 상태 변경 시 서버가 발신하며,
/// 작업지시 페이지가 행 제자리 갱신에 사용 — API 재조회 없이 수량·상태 반영.
/// </summary>
public class WorkOrderProgressDto
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public int ProducedQuantity { get; set; }
    public int PassQuantity { get; set; }
    public int NgQuantity { get; set; }
    public string Status { get; set; } = "Planned";
    public string CompletionBasis { get; set; } = "Produced";
    public bool Completed { get; set; }
    public bool UnmatchedRecipe { get; set; }
    /// <summary>수동 상태 변경(WorkOrderService) 발신분에는 없음 — 기본 false.</summary>
    public bool StaleWorkOrder { get; set; }
    public List<WorkOrderProgressItemDto> Items { get; set; } = new();
}

/// <summary>혼합 레시피 WO 의 레시피별 라인 진행 (WorkOrderItemDto 와 동일 수량 필드).</summary>
public class WorkOrderProgressItemDto
{
    public int RecipeId { get; set; }
    public int PlannedQty { get; set; }
    public int ProducedQty { get; set; }
    public int PassQty { get; set; }
    public int NgQty { get; set; }
}
