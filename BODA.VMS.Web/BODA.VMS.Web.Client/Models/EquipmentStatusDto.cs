namespace BODA.VMS.Web.Client.Models;

public class EquipmentStatusLogDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string? ClientName { get; set; }
    public string Status { get; set; } = "Idle";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public double DurationSeconds { get; set; }
    public string? Reason { get; set; }
}

public class OeeRequestDto
{
    public int? ClientId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class OeeClientResultDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public int ClientIndex { get; set; }

    public double PeriodSeconds { get; set; }
    public double RunningSeconds { get; set; }
    public double IdleSeconds { get; set; }
    public double DownSeconds { get; set; }

    public int TotalInspections { get; set; }
    public int PassCount { get; set; }
    public int NgCount { get; set; }

    /// <summary>가용성 = (Running + Idle) / Period — 설비가 사용 가능했던 비율</summary>
    public double Availability { get; set; }

    /// <summary>성능 = Running / (Running + Idle) — 가용 시간 중 실제 가동 비율</summary>
    public double Performance { get; set; }

    /// <summary>품질 = Pass / Total</summary>
    public double Quality { get; set; }

    /// <summary>OEE = A × P × Q</summary>
    public double Oee { get; set; }
}

public class OeeResultDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<OeeClientResultDto> Clients { get; set; } = new();
}
