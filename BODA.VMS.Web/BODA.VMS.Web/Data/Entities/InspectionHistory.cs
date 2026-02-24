using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class InspectionHistory
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    [MaxLength(100)]
    public string? RecipeName { get; set; }

    public bool IsPass { get; set; }

    [MaxLength(50)]
    public string? NgCode { get; set; }

    /// <summary>
    /// JSON-serialized tool measurement results (up to 7 tools).
    /// </summary>
    public string? ToolResults { get; set; }

    /// <summary>
    /// Relative path to the NG image file.
    /// </summary>
    [MaxLength(500)]
    public string? ImagePath { get; set; }

    public DateTime InspectedAt { get; set; } = DateTime.UtcNow;
}
