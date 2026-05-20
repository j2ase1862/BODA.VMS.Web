using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class InspectionHistory
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    [MaxLength(100)]
    public string? RecipeName { get; set; }

    public bool IsPass { get; set; }

    [MaxLength(50)]
    public string? NgCode { get; set; }

    /// <summary>
    /// JSON-serialized tool measurement results (up to 7 tools).
    /// </summary>
    public string? ToolResults { get; set; }

    /// <summary>
    /// Relative path to the NG image file.
    /// </summary>
    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public DateTime InspectedAt { get; set; } = DateTime.UtcNow;

    // === 추적성(Traceability) 컬럼 — Phase 1 (4M 중 Material/Method/Man 식별) ===

    /// <summary>
    /// 검사 결과가 귀속되는 생산 오더. NULL이면 오더와 무관한 단발성 검사(점검 등).
    /// </summary>
    public int? WorkOrderId { get; set; }

    /// <summary>
    /// 검사 결과가 귀속되는 로트. NULL이면 로트 미할당.
    /// </summary>
    public int? LotId { get; set; }

    /// <summary>
    /// 작업자 식별자(Users.Id). VMS 단말에 로그인한 작업자.
    /// </summary>
    public int? OperatorId { get; set; }

    /// <summary>
    /// 개별 제품 시리얼/바코드 (예: SN-20260519-00123).
    /// </summary>
    [MaxLength(80)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// 교대조 식별자. InspectedAt 기준으로 자동 매칭.
    /// </summary>
    public int? ShiftId { get; set; }

    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public WorkOrder? WorkOrder { get; set; }

    [ForeignKey(nameof(LotId))]
    public Lot? Lot { get; set; }

    [ForeignKey(nameof(OperatorId))]
    public User? Operator { get; set; }
}
