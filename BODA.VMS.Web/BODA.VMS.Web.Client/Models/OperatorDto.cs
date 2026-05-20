namespace BODA.VMS.Web.Client.Models;

public class OperatorDto
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>마스터 생성/수정 — 새 PIN을 평문으로 받음 (서버에서 해시)</summary>
public class OperatorUpsertDto
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }

    /// <summary>새 PIN. 편집 시 비워두면 기존 PIN 유지.</summary>
    public string? Pin { get; set; }

    public bool IsActive { get; set; } = true;
}

public class OperatorSessionDto
{
    public int Id { get; set; }
    public int OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? Department { get; set; }

    public int ClientId { get; set; }
    public int ClientIndex { get; set; }
    public string ClientName { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? EndReason { get; set; }

    public double DurationMinutes
    {
        get
        {
            var end = EndedAt ?? DateTime.UtcNow;
            return Math.Round((end - StartedAt).TotalMinutes, 1);
        }
    }

    public bool IsActive => !EndedAt.HasValue;
}

public class KioskLoginRequest
{
    public int ClientIndex { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
}

public class KioskLogoutRequest
{
    public int ClientIndex { get; set; }
}
