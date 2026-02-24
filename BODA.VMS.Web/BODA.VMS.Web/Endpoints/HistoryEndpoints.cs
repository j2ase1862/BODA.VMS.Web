using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/history")
            .RequireAuthorization();

        group.MapGet("/", async (
            int? clientId,
            DateTime? startDate,
            DateTime? endDate,
            bool? isPass,
            string? ngCode,
            int page,
            int pageSize,
            IHistoryService historyService) =>
        {
            var filter = new HistoryFilterDto
            {
                ClientId = clientId,
                StartDate = startDate,
                EndDate = endDate,
                IsPass = isPass,
                NgCode = ngCode,
                Page = page > 0 ? page : 1,
                PageSize = pageSize > 0 ? pageSize : 50
            };

            var result = await historyService.GetHistoryAsync(filter);
            return Results.Ok(result);
        });

        group.MapGet("/{id:int}", async (int id, IHistoryService historyService) =>
        {
            var detail = await historyService.GetHistoryDetailAsync(id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapGet("/summary", async (
            int? clientId,
            DateTime startDate,
            DateTime endDate,
            IHistoryService historyService) =>
        {
            var summary = await historyService.GetDailySummaryAsync(clientId, startDate, endDate);
            return Results.Ok(summary);
        });

        group.MapGet("/export", async (
            int? clientId,
            DateTime? startDate,
            DateTime? endDate,
            bool? isPass,
            string? ngCode,
            IHistoryService historyService) =>
        {
            var filter = new HistoryFilterDto
            {
                ClientId = clientId,
                StartDate = startDate,
                EndDate = endDate,
                IsPass = isPass,
                NgCode = ngCode
            };

            var bytes = await historyService.ExportToExcelAsync(filter);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"InspectionHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        });
    }
}
