namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// 레시피-파라미터 계층 구조: (RecipeId, ParamCode) → ParamValue
/// </summary>
public class RecipeParameterDto
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int ParamCode { get; set; }
    public double ParamValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Dimension";
    public string? Unit { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// VMS가 검사 결과를 업로드할 때 사용하는 개별 결과
/// </summary>
public class ParameterResultDto
{
    public int ParamCode { get; set; }
    public double MeasuredValue { get; set; }
    public string Judgment { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// VMS → Web 결과 업로드 요청
/// </summary>
public class ParameterResultUploadRequest
{
    public int ClientIndex { get; set; }
    public int RecipeId { get; set; }
    public List<ParameterResultDto> Results { get; set; } = new();
}

/// <summary>
/// 프리셋 그룹: 도구 유형별 관련 파라미터를 일괄 생성하는 템플릿.
/// Code와 Value는 포함하지 않음 — 사용자가 직접 입력.
/// </summary>
public class ParameterPresetGroup
{
    public string GroupName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<ParameterPresetEntry> Entries { get; set; } = new();

    public static readonly List<ParameterPresetGroup> PresetGroups = new()
    {
        new()
        {
            GroupName = "Pattern Tool", Category = "Pattern",
            Entries = new()
            {
                new() { DescriptionTemplate = "Score Threshold",   Unit = "score" },
                new() { DescriptionTemplate = "Angle Lower Limit", Unit = "deg" },
                new() { DescriptionTemplate = "Angle Upper Limit", Unit = "deg" },
                new() { DescriptionTemplate = "Scale Lower Limit", Unit = "ratio" },
                new() { DescriptionTemplate = "Scale Upper Limit", Unit = "ratio" },
                new() { DescriptionTemplate = "Position X Limit",  Unit = "mm" },
                new() { DescriptionTemplate = "Position Y Limit",  Unit = "mm" },
            }
        },
        new()
        {
            GroupName = "Blob Tool", Category = "Blob",
            Entries = new()
            {
                new() { DescriptionTemplate = "Threshold",         Unit = "level" },
                new() { DescriptionTemplate = "Expected Count",    Unit = "ea" },
                new() { DescriptionTemplate = "Min Area",          Unit = "px" },
                new() { DescriptionTemplate = "Max Area",          Unit = "px" },
                new() { DescriptionTemplate = "Max Defect Size",   Unit = "px" },
                new() { DescriptionTemplate = "Total Area Min",    Unit = "px" },
                new() { DescriptionTemplate = "Total Area Max",    Unit = "px" },
            }
        },
        new()
        {
            GroupName = "Dimension Tool", Category = "Dimension",
            Entries = new()
            {
                new() { DescriptionTemplate = "Reference Value",   Unit = "mm" },
                new() { DescriptionTemplate = "Lower Tolerance",   Unit = "mm" },
                new() { DescriptionTemplate = "Upper Tolerance",   Unit = "mm" },
            }
        }
    };

    public static string GetCategoryDescription(string category) => category switch
    {
        "Pattern"   => "패턴 매칭 도구: Score, Angle, Scale, Position 등의 개별 임계값 파라미터",
        "Blob"      => "블롭 분석 도구: Threshold, Count, Area 등의 개별 판정 파라미터",
        "Dimension" => "치수 측정 도구: 기준값, 공차 등의 개별 수치 파라미터",
        _ => ""
    };
}

public class ParameterPresetEntry
{
    public string DescriptionTemplate { get; set; } = string.Empty;
    public string? Unit { get; set; }
}
