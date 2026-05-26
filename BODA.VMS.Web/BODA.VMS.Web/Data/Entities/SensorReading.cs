using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// Predictive_DefectRate_Plan §3.2-D2 / §5.2 — 환경 센서 시계열.
/// VMS 가 PLC/센서 모듈에서 읽어 Heartbeat 와 같은 주기(5초)로 POST.
/// 모든 측정값 nullable — 일부 센서만 연결된 현장에서도 사용 가능.
///
/// Audit 제외(InspectionHistory 와 동일 정책) — 대량 시계열이므로
/// AuditInterceptor 가 매 row 기록하면 비용·DB 크기 폭증.
/// </summary>
public class SensorReading
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>섭씨 온도.</summary>
    public double? TemperatureC { get; set; }

    /// <summary>상대습도(%).</summary>
    public double? HumidityPct { get; set; }

    /// <summary>진동 RMS(mm/s 등 — 현장 단위 그대로).</summary>
    public double? VibrationRms { get; set; }

    /// <summary>공압 압력(psi).</summary>
    public double? PressurePsi { get; set; }
}
