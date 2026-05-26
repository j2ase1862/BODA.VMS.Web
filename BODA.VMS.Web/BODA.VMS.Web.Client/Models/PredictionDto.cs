namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// Predictive_DefectRate_Plan §6 Phase E — VMS/Blazor 가 폴링하는 단발 예측 응답.
/// 모델 미등록·데이터 부족·추론 실패 시에도 200 OK 로 응답 — Status 필드로 구분.
/// </summary>
public class PredictionCurrentResponse
{
    public int ClientIndex { get; set; }
    public string? RecipeName { get; set; }

    /// <summary>예측이 대상으로 한 윈도우의 시작 시각(UTC, hour-aligned). 데이터 없으면 null.</summary>
    public DateTime? WindowStart { get; set; }

    /// <summary>예측 NG율(0~1). null = 예측 산출 실패.</summary>
    public double? PredictedNgRate { get; set; }

    public string? ModelName { get; set; }
    public string? ModelVersion { get; set; }

    /// <summary>현재 hour bucket 의 실제 검사 건수 — 적으면 예측 신뢰도 낮음(소비자가 표시 판단).</summary>
    public int InspectionCountThisHour { get; set; }

    /// <summary>"ok" | "no_model" | "no_data" | "error"</summary>
    public string Status { get; set; } = "";

    public string? Message { get; set; }

    public DateTime ServerUtc { get; set; }
}

/// <summary>
/// /api/predictions/status — 운영자가 활성 모델 상태를 점검할 때 사용.
/// Phase F: 잔차 통계 기반 재학습 권고 시그널 포함.
/// </summary>
public class PredictionServiceStatus
{
    public bool ModelLoaded { get; set; }
    public string? ModelName { get; set; }
    public string? ModelVersion { get; set; }
    public string? OnnxPath { get; set; }
    public DateTime? LoadedAt { get; set; }
    public string? LastError { get; set; }
    public int FeatureCount { get; set; }

    // Phase F — 잔차/재학습 시그널 (활성 모델의 최근 N일 잔차 통계)
    /// <summary>잔차 통계가 산출된 샘플 수(ActualNgRate 채워진 PredictionLog 개수).</summary>
    public int ResidualSampleCount { get; set; }
    /// <summary>평균 |잔차|. 베이스라인 학습 시점 MAE 대비 악화 여부 판단 기준.</summary>
    public double? ResidualMae { get; set; }
    /// <summary>true 면 잔차가 학습 시점 MAE 의 2배를 초과 → 운영자에게 재학습 권고.</summary>
    public bool RetrainRecommended { get; set; }
}

/// <summary>
/// /api/predictions/snapshot — Blazor /forecast 카드 그리드용 한 번 호출 응답.
/// 모든 활성 VisionClient 의 현재 예측을 한 번에 반환.
/// </summary>
public class PredictionSnapshotResponse
{
    public DateTime ServerUtc { get; set; }
    public List<PredictionCurrentResponse> Items { get; set; } = new();
}

/// <summary>
/// /api/predictions/actuals/{clientIndex} — v_defect_features 의 최근 N시간 실측 NG율.
/// /forecast 페이지 시계열 차트의 데이터 소스.
/// </summary>
public class RecentActualsResponse
{
    public int ClientIndex { get; set; }
    public string? RecipeName { get; set; }
    public List<ActualHourPoint> Points { get; set; } = new();
}

public class ActualHourPoint
{
    public DateTime HourBucket { get; set; }
    public double NgRate { get; set; }
    public int InspectionCount { get; set; }
}

/// <summary>
/// /api/predictions/residuals/{clientIndex} — Phase F 잔차 SPC 차트 데이터.
/// PredictionLogs 중 ActualNgRate 가 backfill 된 row 만 포함.
/// </summary>
public class ResidualsResponse
{
    public int ClientIndex { get; set; }
    public string? RecipeName { get; set; }
    public string? ModelName { get; set; }
    public string? ModelVersion { get; set; }

    public List<ResidualPoint> Points { get; set; } = new();

    /// <summary>잔차 평균(centerline).</summary>
    public double ResidualMean { get; set; }
    /// <summary>잔차 표준편차.</summary>
    public double ResidualStdDev { get; set; }
    /// <summary>+3σ Upper Control Limit.</summary>
    public double Ucl { get; set; }
    /// <summary>-3σ Lower Control Limit.</summary>
    public double Lcl { get; set; }
}

public class ResidualPoint
{
    public DateTime WindowStart { get; set; }
    public double Predicted { get; set; }
    public double Actual { get; set; }
    /// <summary>Predicted - Actual. 양수 = 과대예측, 음수 = 과소예측(NG 놓침).</summary>
    public double Residual { get; set; }
}
