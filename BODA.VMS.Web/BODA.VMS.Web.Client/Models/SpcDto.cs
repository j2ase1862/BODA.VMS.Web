namespace BODA.VMS.Web.Client.Models;

public class SpcRequestDto
{
    public int RecipeId { get; set; }
    public int ParamCode { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? ClientId { get; set; }
    public int? WorkOrderId { get; set; }

    /// <summary>부분군 크기 (보통 4-5). 시간순으로 묶어 Xbar-R 계산</summary>
    public int SubgroupSize { get; set; } = 5;

    /// <summary>최대 부분군 수 (차트 가독성)</summary>
    public int MaxSubgroups { get; set; } = 25;
}

public class SpcSubgroup
{
    public int Index { get; set; }
    public DateTime StartTime { get; set; }
    public double Mean { get; set; }   // Xbar
    public double Range { get; set; }  // R
    public List<double> Values { get; set; } = new();
}

public class SpcResultDto
{
    public int RecipeId { get; set; }
    public int ParamCode { get; set; }
    public string? ParamDescription { get; set; }
    public string? Unit { get; set; }
    public double? LowerLimit { get; set; }
    public double? UpperLimit { get; set; }
    public double? TargetValue { get; set; }

    // 입력 정보
    public int SubgroupSize { get; set; }
    public int TotalMeasurements { get; set; }
    public int SubgroupCount { get; set; }

    // 통계
    public double GrandMean { get; set; }       // Xbar-bar (= 모든 측정값의 평균)
    public double MeanOfRanges { get; set; }    // R-bar
    public double StdDev { get; set; }          // 전체 σ (개별값 기준)
    public double EstimatedSigma { get; set; }  // R-bar / d2 (부분군 기반 σ)

    // 관리한계 (Xbar 관리도)
    public double UclXbar { get; set; }
    public double LclXbar { get; set; }

    // 관리한계 (R 관리도)
    public double UclR { get; set; }
    public double LclR { get; set; }

    // 공정능력
    public double? Cp { get; set; }
    public double? Cpk { get; set; }
    public double? Pp { get; set; }
    public double? Ppk { get; set; }

    public List<SpcSubgroup> Subgroups { get; set; } = new();
}
