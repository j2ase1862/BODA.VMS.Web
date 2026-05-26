using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// Predictive_DefectRate_Plan §4.2 — 학습된 ML 모델 메타 레지스트리.
/// Python trainer 가 export 한 ONNX 파일을 .NET ONNX Runtime 이 in-process 로 로드.
/// IsActive 가 true 인 (Name, IsActive) 조합이 추론에 사용됨.
/// </summary>
public class MLModel
{
    [Key]
    public int Id { get; set; }

    /// <summary>모델 식별자(e.g. "defect_rate_60m"). 같은 Name 의 여러 Version 이 존재 가능.</summary>
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>버전 태그(e.g. "2026.05.26-r1").</summary>
    [Required, MaxLength(40)]
    public string Version { get; set; } = string.Empty;

    /// <summary>ONNX 파일 경로(상대/절대 — 운영 환경 기준).</summary>
    [Required, MaxLength(500)]
    public string OnnxPath { get; set; } = string.Empty;

    /// <summary>홀드아웃셋 평가 — Mean Absolute Error.</summary>
    public double Mae { get; set; }

    /// <summary>홀드아웃셋 평가 — R².</summary>
    public double R2 { get; set; }

    public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

    /// <summary>true 인 모델만 추론에 사용. 같은 Name 의 다른 row 와 상호배타.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 피처 순서·정규화 통계(JSON).
    /// 추론 시 Feature View 로 만든 행을 같은 순서·동일 정규화로 변환해야 학습-추론 정합성 보장.
    /// </summary>
    [Required]
    public string FeatureSpecJson { get; set; } = "{}";
}
