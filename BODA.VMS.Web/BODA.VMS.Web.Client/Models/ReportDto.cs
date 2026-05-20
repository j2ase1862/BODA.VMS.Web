namespace BODA.VMS.Web.Client.Models;

public enum ReportType
{
    Daily,
    Weekly,
    Monthly
}

public class ReportRequestDto
{
    public ReportType Type { get; set; }

    /// <summary>Daily=해당 날짜, Weekly=주의 시작 날짜(월요일), Monthly=달의 1일</summary>
    public DateTime ReferenceDate { get; set; }

    /// <summary>특정 라인만 분석. NULL이면 전체</summary>
    public int? ClientId { get; set; }
}
