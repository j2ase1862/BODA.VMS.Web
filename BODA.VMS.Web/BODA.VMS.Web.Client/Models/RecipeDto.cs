namespace BODA.VMS.Web.Client.Models;

public class RecipeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ClientId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<StepDto> Steps { get; set; } = new();
}

public class StepDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public List<InspectionToolDto> InspectionTools { get; set; } = new();
}

public class InspectionToolDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}
