using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 개별 파라미터 측정값. InspectionHistory.ToolResults JSON에 포함된 정보를
/// 정규화 테이블로 따로 저장 — SPC/통계 쿼리 성능을 위해 분리합니다.
///
/// 한 InspectionHistory(검사 1회)는 여러 ParameterMeasurement(파라미터별 측정값)를 가집니다.
/// </summary>
public class ParameterMeasurement
{
    [Key]
    public int Id { get; set; }

    public int HistoryId { get; set; }

    public int RecipeId { get; set; }

    public int ParamCode { get; set; }

    public double MeasuredValue { get; set; }

    /// <summary>"OK" | "NG"</summary>
    [Required, MaxLength(5)]
    public string Judgment { get; set; } = "OK";

    public DateTime InspectedAt { get; set; }

    // === 추적성 (InspectionHistory에서 비정규화 복사) — SPC 필터링 성능용 ===
    public int ClientId { get; set; }
    public int? WorkOrderId { get; set; }
    public int? LotId { get; set; }

    [ForeignKey(nameof(HistoryId))]
    public InspectionHistory History { get; set; } = null!;
}
