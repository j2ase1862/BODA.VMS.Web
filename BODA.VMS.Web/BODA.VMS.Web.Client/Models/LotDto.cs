namespace BODA.VMS.Web.Client.Models;

public class LotDto
{
    public int Id { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public int WorkOrderId { get; set; }
    public string? WorkOrderNo { get; set; }
    public int Sequence { get; set; }
    public int Quantity { get; set; }
    public int PassCount { get; set; }
    public int NgCount { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Note { get; set; }

    public double NgRate => Quantity > 0
        ? Math.Round((double)NgCount / Quantity * 100, 2)
        : 0;
}

public class CreateLotRequest
{
    public string? Note { get; set; }
}
