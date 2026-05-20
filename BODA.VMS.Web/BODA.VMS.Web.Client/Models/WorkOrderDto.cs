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

    /// <summary>진척률 (%) — 계획 대비</summary>
    public double Progress => PlannedQuantity > 0
        ? Math.Round((double)ProducedQuantity / PlannedQuantity * 100, 2)
        : 0;
}

public class WorkOrderStatusChangeRequest
{
    public string Action { get; set; } = string.Empty; // "Start" | "Complete" | "Close"
}
