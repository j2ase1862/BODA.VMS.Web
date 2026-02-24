using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class VisionClient
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [Column("Index")]
    public int ClientIndex { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastSeenAt { get; set; }

    [MaxLength(100)]
    public string? LastHeartbeatIp { get; set; }

    [MaxLength(100)]
    public string? HostName { get; set; }

    [MaxLength(50)]
    public string? SwName { get; set; }

    // Navigation
    public ICollection<Recipe> Recipes { get; set; } = new List<Recipe>();
    public ICollection<InspectionHistory> InspectionHistories { get; set; } = new List<InspectionHistory>();
}
