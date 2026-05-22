namespace BODA.VMS.Web.Client.Models;

public class OperatorDto
{
    public int Id { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    /// <summary>D10: 작업자 등급 — "Operator" / "Lead" / "Supervisor". VMS 메뉴 가시성 제어용.</summary>
    public string Role { get; set; } = "Operator";
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
    /// <summary>D10: "Operator" / "Lead" / "Supervisor". 미지정 시 "Operator".</summary>
    public string Role { get; set; } = "Operator";

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
    /// <summary>D10: 활성 세션 작업자의 등급. VMS 가 메뉴 가시성 결정에 사용.</summary>
    public string Role { get; set; } = "Operator";

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
