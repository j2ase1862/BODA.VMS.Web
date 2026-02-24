namespace BODA.VMS.Web.Client.Models;

public class HistorySummaryDto
{
    public DateTime Date { get; set; }
    public int TotalCount { get; set; }
    public int PassCount { get; set; }
    public int NgCount { get; set; }
    public double NgRate => TotalCount > 0 ? (double)NgCount / TotalCount * 100 : 0;
}

public class HistoryDetailDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? RecipeName { get; set; }
    public bool IsPass { get; set; }
    public string? NgCode { get; set; }
    public List<ToolResultItem>? ToolResults { get; set; }
    public string? ImagePath { get; set; }
    public DateTime InspectedAt { get; set; }
}

public class ToolResultItem
{
    public string ToolName { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public bool IsPass { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
