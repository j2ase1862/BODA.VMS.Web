namespace BODA.VMS.Web.Client.Models;

public class AlarmEventDto
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public int? ClientIndex { get; set; }
    public string AlarmType { get; set; } = "NG";
    public string Severity { get; set; } = "Major";
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedBy { get; set; }
    public string? AcknowledgedByName { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
    public string? ResolvedByName { get; set; }
    public string? Resolution { get; set; }
    public int? RelatedHistoryId { get; set; }

    /// <summary>New (미확인) / Ack (확인됨) / Resolved (해제됨)</summary>
    public string State => ResolvedAt.HasValue
        ? "Resolved"
        : (AcknowledgedAt.HasValue ? "Ack" : "New");
}

public class AlarmFilterDto
{
    public int? ClientId { get; set; }
    public string? AlarmType { get; set; }
    public string? Severity { get; set; }
    public string? State { get; set; } // "New" | "Ack" | "Resolved"
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class AlarmActionRequest
{
    /// <summary>"Acknowledge" | "Resolve"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Resolve일 때만 필수</summary>
    public string? Resolution { get; set; }
}

public class AlarmSummaryDto
{
    public int NewCount { get; set; }
    public int AckCount { get; set; }
    public int CriticalCount { get; set; }
}
