namespace BODA.VMS.Web.Client.Models;

public class WorkOrderDto
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }

    public int ClientId { get; set; }
    public string? ClientName { get; set; }
    public int? ClientIndex { get; set; }

    public int RecipeId { get; set; }
    public string? RecipeName { get; set; }

    public int PlannedQuantity { get; set; }
    public int ProducedQuantity { get; set; }
    public int PassQuantity { get; set; }
    public int NgQuantity { get; set; }

    public string Status { get; set; } = "Planned";

    /// <summary>
    /// 완료 기준 — "Produced"(총 생산 수량 기준, 기본값) / "Pass"(양품 수량 기준, opt-in).
    /// Pass 면 양품이 계획 수량에 도달할 때까지 진행 (NG 만큼 자동으로 더 생산).
    /// </summary>
    public string CompletionBasis { get; set; } = "Produced";

    public DateTime? PlannedStartAt { get; set; }
    public DateTime? ActualStartAt { get; set; }
    public DateTime? ActualEndAt { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>합격률 (%) — 진행 중 표시용</summary>
    public double PassRate => ProducedQuantity > 0
        ? Math.Round((double)PassQuantity / ProducedQuantity * 100, 2)
        : 0;

    /// <summary>완료 기준에 따른 진행 수량 — Pass 기준이면 양품, Produced 기준이면 총 생산</summary>
    public int ProgressQuantity => CompletionBasis == "Pass" ? PassQuantity : ProducedQuantity;

    /// <summary>진척률 (%) — 계획 대비, 완료 기준 반영</summary>
    public double Progress => PlannedQuantity > 0
        ? Math.Round((double)ProgressQuantity / PlannedQuantity * 100, 2)
        : 0;

    /// <summary>
    /// 레시피별 계획/실적 라인 (혼합 레시피 WO). 단일 레시피 WO 는 라인 1개.
    /// 본체 수량 필드들은 라인 합계(롤업)와 일치한다.
    /// </summary>
    public List<WorkOrderItemDto> Items { get; set; } = new();
}

/// <summary>혼합 레시피 WO 의 레시피별 라인 (docs/design/wo-mixed-recipe-spec.md)</summary>
public class WorkOrderItemDto
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string? RecipeName { get; set; }
    public int PlannedQty { get; set; }
    public int ProducedQty { get; set; }
    public int PassQty { get; set; }
    public int NgQty { get; set; }
}

public class WorkOrderStatusChangeRequest
{
    public string Action { get; set; } = string.Empty; // "Start" | "Complete" | "Close"
}
