using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 생산 오더 (Work Order). 어떤 제품을 어느 라인에서 어떤 레시피로 몇 개 생산할지 정의.
/// 검사 결과는 OrderId/LotId를 통해 이 오더에 귀속됩니다.
///
/// 상태 전이: Planned → InProgress → Completed → Closed
/// </summary>
public class WorkOrder
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 오더 번호. 사람이 읽는 식별자 (예: WO-20260519-001).
    /// </summary>
    [Required, MaxLength(50)]
    public string OrderNo { get; set; } = string.Empty;

    public int ProductId { get; set; }

    /// <summary>
    /// 작업 라인(Vision Client).
    /// </summary>
    public int ClientId { get; set; }

    /// <summary>
    /// 사용할 레시피.
    /// </summary>
    public int RecipeId { get; set; }

    public int PlannedQuantity { get; set; }

    public int ProducedQuantity { get; set; }

    public int PassQuantity { get; set; }

    public int NgQuantity { get; set; }

    /// <summary>
    /// Planned / InProgress / Completed / Closed
    /// </summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = WorkOrderStatus.Planned;

    public DateTime? PlannedStartAt { get; set; }

    public DateTime? ActualStartAt { get; set; }

    public DateTime? ActualEndAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    [ForeignKey(nameof(RecipeId))]
    public Recipe Recipe { get; set; } = null!;

    public ICollection<Lot> Lots { get; set; } = new List<Lot>();
}

public static class WorkOrderStatus
{
    public const string Planned = "Planned";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Closed = "Closed";
}
