using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class Recipe
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Step> Steps { get; set; } = new List<Step>();
}
