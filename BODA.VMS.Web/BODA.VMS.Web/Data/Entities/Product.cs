using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 제품/품목 마스터. 동일 제품을 여러 라인(Client)에서 생산할 수 있으며
/// 제품마다 기본 Recipe를 매핑해두면 WorkOrder 발행 시 자동 채워집니다.
/// </summary>
public class Product
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Specification { get; set; }

    /// <summary>
    /// 기본 Recipe 매핑 (선택). WorkOrder 발행 시 기본값으로 사용됩니다.
    /// </summary>
    public int? DefaultRecipeId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(DefaultRecipeId))]
    public Recipe? DefaultRecipe { get; set; }

    public ICollection<WorkOrder> WorkOrders { get; set; } = new List<WorkOrder>();
}
