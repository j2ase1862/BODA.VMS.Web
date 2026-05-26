using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// Predictive_DefectRate_Plan §4.2 — 예측 결과 영속화.
/// 윈도우 종료 후 ActualNgRate 를 backfill 하여 잔차 SPC / 드리프트 모니터링.
/// </summary>
public class PredictionLog
{
    [Key]
    public int Id { get; set; }

    public int MLModelId { get; set; }

    [ForeignKey(nameof(MLModelId))]
    public MLModel? MLModel { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient? Client { get; set; }

    public int RecipeId { get; set; }

    /// <summary>예측이 대상으로 하는 윈도우 시작(UTC).</summary>
    public DateTime WindowStart { get; set; }

    /// <summary>예측 NG율(0~1).</summary>
    public double PredictedNgRate { get; set; }

    /// <summary>
    /// 윈도우 종료 후 실제 측정 NG율(0~1). NULL 이면 아직 backfill 안 됨.
    /// 드리프트 모니터 = (Predicted - Actual) 잔차의 SPC 차트.
    /// </summary>
    public double? ActualNgRate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
