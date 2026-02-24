using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Client.Models;

public class RegisterRequest
{
    [Required(ErrorMessage = "Username is required")]
    [MinLength(3, ErrorMessage = "Username must be at least 3 characters")]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(4, ErrorMessage = "Password must be at least 4 characters")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display name is required")]
    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;
}
