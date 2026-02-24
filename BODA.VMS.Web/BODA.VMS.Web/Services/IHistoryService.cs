using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IHistoryService
{
    Task<PagedResult<HistoryDetailDto>> GetHistoryAsync(HistoryFilterDto filter);
    Task<HistoryDetailDto?> GetHistoryDetailAsync(int id);
    Task<List<HistorySummaryDto>> GetDailySummaryAsync(int? clientId, DateTime startDate, DateTime endDate);
    Task<byte[]> ExportToExcelAsync(HistoryFilterDto filter);
}
