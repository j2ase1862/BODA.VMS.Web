using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Role { get; set; } = "Pending"; // Admin, User, Guest, Pending

    public bool IsApproved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ApprovedAt { get; set; }
}
