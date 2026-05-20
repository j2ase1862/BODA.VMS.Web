namespace BODA.VMS.Web.Client.Models;

public class ShiftDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int StartHour { get; set; }
    public int EndHour { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public int DurationHours => StartHour < EndHour
        ? EndHour - StartHour
        : 24 - StartHour + EndHour;

    public string TimeRange => $"{StartHour:D2}:00 - {EndHour:D2}:00" + (StartHour > EndHour ? " (overnight)" : "");
}

public class ShiftReportEntryDto
{
    public DateTime Date { get; set; }
    public int ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int PassCount { get; set; }
    public int NgCount { get; set; }
    public double NgRate => TotalCount > 0 ? Math.Round((double)NgCount / TotalCount * 100, 2) : 0;
}

public class ShiftReportRequestDto
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? ClientId { get; set; }
}
