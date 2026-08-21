namespace BODA.VMS.Web.Client.Models;

public class HistoryFilterDto
{
    public int? ClientId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool? IsPass { get; set; }
    public string? NgCode { get; set; }
    // WO/Lot 귀속 필터 — 구형 업로드 행은 NULL 이라 필터 적용 시 제외됨 (미지정 = 전체)
    public int? WorkOrderId { get; set; }
    public int? LotId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
