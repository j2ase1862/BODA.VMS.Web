namespace BODA.VMS.Web.Client.Models;

public class DefectCodeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Severity { get; set; } = "Major";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>파레토 분석 결과: NG 코드별 빈도</summary>
public class ParetoEntryDto
{
    public string NgCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double CumulativePercentage { get; set; }
}

public class ParetoResultDto
{
    public int TotalNgCount { get; set; }
    public int TotalInspections { get; set; }
    public List<ParetoEntryDto> Entries { get; set; } = new();
}

/// <summary>DefectCode 자동완성용 후보. 두 풀(Param/History) 통합</summary>
public class DefectCodeCandidateDto
{
    public string Code { get; set; } = string.Empty;

    /// <summary>"Param" — RecipeParameter.ParamCode 사전 정의, "History" — InspectionHistory에서 실제 발생</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>RecipeParameter.Description (Param 소스) 또는 NULL (History only)</summary>
    public string? SuggestedDescription { get; set; }

    /// <summary>RecipeParameter.Category (Param 소스만)</summary>
    public string? SuggestedCategory { get; set; }

    /// <summary>InspectionHistory에서 발생한 횟수 (History 소스만, 최근 기간 기준)</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>이미 DefectCodes 마스터에 등록된 코드면 true</summary>
    public bool IsRegistered { get; set; }
}

/// <summary>미등록 NG 코드 (DefectCodes 페이지의 알림 섹션)</summary>
public class UnregisteredNgCodeDto
{
    public string Code { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string? SuggestedDescription { get; set; }
    public string? SuggestedCategory { get; set; }
}
