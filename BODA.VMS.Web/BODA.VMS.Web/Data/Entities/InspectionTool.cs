using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class InspectionTool
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ToolType { get; set; } = string.Empty; // e.g., "PatMax", "Blob", "Caliper"

    public int OrderIndex { get; set; }

    public int StepId { get; set; }

    [ForeignKey(nameof(StepId))]
    public Step Step { get; set; } = null!;
}
