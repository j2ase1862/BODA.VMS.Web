using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 레시피-파라미터 계층 구조: (RecipeId, ParamCode) → ParamValue.
/// 같은 ParamCode(2001)라도 레시피별로 다른 값을 가질 수 있습니다.
/// </summary>
public class RecipeParameter
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 소속 레시피 (최상위 부모)
    /// </summary>
    public int RecipeId { get; set; }

    /// <summary>
    /// 고유 숫자 코드 (예: 2001, 3001). 레시피 내에서 유일합니다.
    /// </summary>
    public int ParamCode { get; set; }

    /// <summary>
    /// 실제 적용될 수치 (기준값, 공차, 임계값 등 단일 값)
    /// </summary>
    public double ParamValue { get; set; }

    /// <summary>
    /// 관리용 명칭 (예: "패턴1_기준값", "블롭_최소면적")
    /// </summary>
    [Required, MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 도구 분류: Pattern, Blob, Dimension
    /// </summary>
    [Required, MaxLength(30)]
    public string Category { get; set; } = "Dimension";

    [MaxLength(20)]
    public string? Unit { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(RecipeId))]
    public Recipe Recipe { get; set; } = null!;
}
