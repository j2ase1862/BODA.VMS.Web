using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class Step
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public int RecipeId { get; set; }

    [ForeignKey(nameof(RecipeId))]
    public Recipe Recipe { get; set; } = null!;

    // Navigation
    public ICollection<InspectionTool> InspectionTools { get; set; } = new List<InspectionTool>();
}
