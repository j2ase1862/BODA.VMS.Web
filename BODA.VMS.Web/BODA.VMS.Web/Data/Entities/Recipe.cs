using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class Recipe
{
    [Key]
    [Column("RecipeID")]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    [Column("RecipeName")]
    public string Name { get; set; } = string.Empty;

    [Column("ClientID")]
    public int ClientId { get; set; }

    public int? RecipeIndex { get; set; }

    // Web 전용 컬럼 (VisionServer 테이블에는 없을 수 있음 — Program.cs에서 ALTER TABLE)
    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime? CreatedAt { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    // Navigation
    public ICollection<Step> Steps { get; set; } = new List<Step>();
    public ICollection<RecipeParameter> Parameters { get; set; } = new List<RecipeParameter>();
}
