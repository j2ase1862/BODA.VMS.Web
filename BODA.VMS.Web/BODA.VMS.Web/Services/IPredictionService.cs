using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

/// <summary>
/// Predictive_DefectRate_Plan §6 Phase E — 다음 1시간 NG율 예측 추론 서비스.
/// 60초 캐시 TTL (plan 명시), ONNX in-proc 추론, Active MLModel 핫스왑.
/// </summary>
public interface IPredictionService
{
    /// <summary>
    /// 주어진 clientIndex 에 대한 현재 예측을 반환.
    /// 캐시 hit 시 동일 객체 반환(60s TTL).
    /// 캐시 miss + Status="ok" 시 PredictionLog row 영속화 (드리프트 모니터/잔차 SPC 데이터).
    /// </summary>
    Task<PredictionCurrentResponse> GetCurrentAsync(int clientIndex, CancellationToken ct = default);

    /// <summary>
    /// 모든 active VisionClient 의 현재 예측을 병렬 호출하여 한 번에 반환.
    /// /forecast 카드 그리드용.
    /// </summary>
    Task<PredictionSnapshotResponse> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// 주어진 clientIndex 의 최근 N시간 실측 NG율 시계열 — v_defect_features 직접 조회.
    /// /forecast 차트용. 데이터 없으면 Points 빈 리스트.
    /// </summary>
    Task<RecentActualsResponse> GetRecentActualsAsync(int clientIndex, int hours = 24, CancellationToken ct = default);

    /// <summary>
    /// 활성 모델/세션 상태 조회 — 운영 진단용.
    /// Phase F: 최근 잔차 통계 + 재학습 권고 시그널 동봉 (동기 호출 불가하므로 별도 메서드 호출 권장).
    /// 단순 동기 호출용 — 잔차 통계는 별도 캐시.
    /// </summary>
    PredictionServiceStatus GetStatus();

    /// <summary>
    /// 활성 모델 + 최근 N일 잔차 통계 포함 status. Phase F 재학습 시그널 계산.
    /// 잔차 통계는 30s 캐시.
    /// </summary>
    Task<PredictionServiceStatus> GetStatusWithResidualsAsync(CancellationToken ct = default);

    /// <summary>
    /// 주어진 clientIndex 의 최근 N시간 잔차 시계열 — PredictionLogs (Actual 채워진 것만).
    /// Phase F /forecast SPC 차트용. 빈 리스트면 통계도 0.
    /// </summary>
    Task<ResidualsResponse> GetResidualsAsync(int clientIndex, int hours = 168, CancellationToken ct = default);
}
