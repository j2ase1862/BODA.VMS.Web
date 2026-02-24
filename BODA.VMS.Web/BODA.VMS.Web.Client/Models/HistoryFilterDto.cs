namespace BODA.VMS.Web.Client.Models;

public class HistoryFilterDto
{
    public int? ClientId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? IsPass { get; set; }
    public string? NgCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
