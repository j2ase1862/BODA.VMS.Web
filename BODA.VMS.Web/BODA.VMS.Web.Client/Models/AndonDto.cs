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
}
