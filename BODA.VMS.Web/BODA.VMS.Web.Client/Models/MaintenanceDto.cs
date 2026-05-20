namespace BODA.VMS.Web.Client.Models;

public class MaintenanceScheduleDto
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public int? ClientIndex { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int IntervalDays { get; set; } = 7;
    public int EstimatedDurationMinutes { get; set; } = 30;

    public DateTime? LastPerformedAt { get; set; }
    public DateTime NextDueAt { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>음수면 지연(과거), 0이면 오늘, 양수면 남은 일수</summary>
    public int DueInDays => (int)Math.Floor((NextDueAt - DateTime.UtcNow).TotalDays);

    public string Status => DueInDays < 0 ? "Overdue"
        : DueInDays == 0 ? "DueToday"
        : DueInDays <= 3 ? "Soon"
        : "OnTrack";
}

public class MaintenanceScheduleUpsertDto
{
    public int? ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int IntervalDays { get; set; } = 7;
    public int EstimatedDurationMinutes { get; set; } = 30;
    public DateTime? NextDueAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class MaintenanceRecordDto
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public DateTime PerformedAt { get; set; }
    public int? ActualDurationMinutes { get; set; }
    public string? PerformedByName { get; set; }
    public string? Notes { get; set; }
    public DateTime PreviousDueAt { get; set; }
    public DateTime NewDueAt { get; set; }
}

public class PerformMaintenanceRequest
{
    public int? ClientId { get; set; }
    public int? ActualDurationMinutes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>라인별 신뢰성 통계 (MTBF/MTTR)</summary>
public class ReliabilityStatsDto
{
    public int ClientId { get; set; }
    public int ClientIndex { get; set; }
    public string ClientName { get; set; } = string.Empty;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    public int FailureCount { get; set; }
    public double TotalRunningHours { get; set; }
    public double TotalDownHours { get; set; }

    /// <summary>MTBF = Total Running Time / Number of Failures (hours)</summary>
    public double? MtbfHours { get; set; }

    /// <summary>MTTR = Total Down Time / Number of Failures (hours)</summary>
    public double? MttrHours { get; set; }

    /// <summary>Availability = MTBF / (MTBF + MTTR)</summary>
    public double? Availability { get; set; }
}

public class ReliabilityRequestDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? ClientId { get; set; }
}
