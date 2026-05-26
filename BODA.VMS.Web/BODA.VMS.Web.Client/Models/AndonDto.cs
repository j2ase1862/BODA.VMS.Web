namespace BODA.VMS.Web.Client.Models;

/// <summary>Andon Board용 라인 1개의 통합 스냅샷</summary>
public class AndonLineDto
{
    public int ClientId { get; set; }
    public int ClientIndex { get; set; }
    public string ClientName { get; set; } = string.Empty;

    /// <summary>Running / Idle / Down — EquipmentStatusLog 기반 현재 상태</summary>
    public string Status { get; set; } = "Down";

    public DateTime? StatusSince { get; set; }
    public DateTime? LastSeenAt { get; set; }

    // === 오늘 (00:00 ~ now) 생산 카운트 ===
    public int TodayTotal { get; set; }
    public int TodayPass { get; set; }
    public int TodayNg { get; set; }
    public double TodayNgRate => TodayTotal > 0
        ? Math.Round((double)TodayNg / TodayTotal * 100, 2)
        : 0;

    // === 현재 활성 WorkOrder (InProgress) ===
    public string? ActiveWorkOrderNo { get; set; }
    public string? ActiveProductCode { get; set; }
    public int? ActiveWorkOrderProgress { get; set; } // 0-100

    // === 미확인 알람 수 ===
    public int OpenAlarmCount { get; set; }
    public int CriticalAlarmCount { get; set; }
}

public class AndonSnapshotDto
{
    public DateTime AsOf { get; set; }
    public List<AndonLineDto> Lines { get; set; } = new();

    // 전체 KPI
    public int TotalToday { get; set; }
    public int PassToday { get; set; }
    public int NgToday { get; set; }
    public double NgRateToday => TotalToday > 0
        ? Math.Round((double)NgToday / TotalToday * 100, 2)
        : 0;

    public int RunningLines { get; set; }
    public int IdleLines { get; set; }
    public int DownLines { get; set; }

    // 최근 미해제 Critical/Major 알람 (최대 10건)
    public List<AlarmEventDto> ActiveAlarms { get; set; } = new();

    // === Predictive_DefectRate_Plan §6 Phase E + §8 Open Q #5 ===
    // 알람 피로 방지를 위해 별도 "Predictive Insight" 채널 — 알람 생성 없이 권고만.
    // 임계 초과(≥5%)한 라인만 포함 — 정상 라인은 카드에 표시하지 않음(노이즈 회피).
    public List<PredictiveInsightDto> PredictiveInsights { get; set; } = new();
}

/// <summary>
/// 다음 1시간 NG율 예측이 임계를 초과한 라인의 사전 경보.
/// Plan §8 Open Q #5 정책: "초기에는 'Predictive Insight' 별도 채널 권장"
/// → AlarmEvents 와 분리된 비-알람 권고 채널.
/// </summary>
public class PredictiveInsightDto
{
    public int ClientId { get; set; }
    public int ClientIndex { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? RecipeName { get; set; }

    /// <summary>예측 NG율(0~1).</summary>
    public double PredictedNgRate { get; set; }

    /// <summary>"high" (≥10%) / "medium" (5~10%). low(<5%)는 리스트에 포함되지 않음.</summary>
    public string Severity { get; set; } = "medium";

    public DateTime? WindowStart { get; set; }
    public string? ModelName { get; set; }
    public string? ModelVersion { get; set; }
}
