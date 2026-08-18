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

    /// <summary>
    /// 완료 기준 — Pass(양품 수량이 계획 수량 도달 시 완료, 신규 기본값) /
    /// Produced(총 생산 수량 기준, 구버전 동작). 기존 DB 행은 마이그레이션 기본값
    /// Produced 로 채워져 동작이 바뀌지 않는다 (2026-08-18, 양품 100개 채우기 요구).
    /// </summary>
    [Required, MaxLength(20)]
    public string CompletionBasis { get; set; } = WorkOrderCompletionBasis.Pass;

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

public static class WorkOrderCompletionBasis
{
    /// <summary>총 생산 수량 기준 (구버전 동작 — 기존 WO 호환)</summary>
    public const string Produced = "Produced";
    /// <summary>양품 수량 기준 (신규 WO 기본값 — NG 만큼 자동으로 더 생산)</summary>
    public const string Pass = "Pass";

    public static bool IsValid(string? value) => value is Produced or Pass;
}
