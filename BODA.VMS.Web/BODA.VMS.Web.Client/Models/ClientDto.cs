using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Client.Models;

public class ClientDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Client name is required")]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "IP address is required")]
    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [Range(0, 99, ErrorMessage = "Client index must be 0-99")]
    public int ClientIndex { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public string? LastHeartbeatIp { get; set; }

    public string? HostName { get; set; }

    public string? SwName { get; set; }

    public bool IsAlive { get; set; }
}
