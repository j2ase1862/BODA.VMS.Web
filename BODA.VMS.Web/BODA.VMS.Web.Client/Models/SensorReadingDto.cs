namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// Predictive_DefectRate_Plan §5.2 — VMS → Web 환경 센서 단건 업로드.
/// Heartbeat 와 같은 주기(5초)로 PLC/센서 모듈에서 읽어 송신.
/// 모든 측정값 nullable — 일부 센서만 연결된 현장에서도 호출 가능.
/// </summary>
public class SensorReadingRequest
{
    public int ClientIndex { get; set; }

    /// <summary>VMS 시점의 UTC 측정 시각. 미지정 시 서버 수신 시각 사용.</summary>
    public DateTime? Timestamp { get; set; }

    public double? TemperatureC { get; set; }
    public double? HumidityPct { get; set; }
    public double? VibrationRms { get; set; }
    public double? PressurePsi { get; set; }
}
