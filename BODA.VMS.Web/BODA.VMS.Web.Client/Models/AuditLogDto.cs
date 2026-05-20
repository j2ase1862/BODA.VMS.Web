namespace BODA.VMS.Web.Client.Models;

public class AuditLogDto
{
    public long Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Changes { get; set; }
    public DateTime Timestamp { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
}

public class AuditLogFilterDto
{
    public string? EntityName { get; set; }
    public string? Action { get; set; }
    public int? UserId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
