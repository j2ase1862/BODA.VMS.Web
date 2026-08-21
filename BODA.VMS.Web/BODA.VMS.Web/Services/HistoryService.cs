using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class HistoryService : IHistoryService
{
    private readonly BodaVmsDbContext _db;

    public HistoryService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<HistoryDetailDto>> GetHistoryAsync(HistoryFilterDto filter)
    {
        var query = _db.InspectionHistories
            .Include(h => h.Client)
            .AsQueryable();

        if (filter.ClientId.HasValue)
            query = query.Where(h => h.ClientId == filter.ClientId.Value);
        if (filter.StartDate.HasValue)
            query = query.Where(h => h.InspectedAt >= filter.StartDate.Value);
        if (filter.EndDate.HasValue)
            query = query.Where(h => h.InspectedAt <= filter.EndDate.Value);
        if (filter.IsPass.HasValue)
            query = query.Where(h => h.IsPass == filter.IsPass.Value);
        if (!string.IsNullOrWhiteSpace(filter.NgCode))
            query = query.Where(h => h.NgCode == filter.NgCode);
        if (filter.WorkOrderId.HasValue)
            query = query.Where(h => h.WorkOrderId == filter.WorkOrderId.Value);
        if (filter.LotId.HasValue)
            query = query.Where(h => h.LotId == filter.LotId.Value);

        var totalCount = await query.CountAsync();

        var rawItems = await query
            .OrderByDescending(h => h.InspectedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(h => new
            {
                h.Id,
                h.ClientId,
                ClientName = h.Client.Name,
                h.RecipeName,
                h.IsPass,
                h.NgCode,
                h.ImagePath,
                h.InspectedAt,
                h.ToolResults,
                h.WorkOrderId,
                h.LotId,
                WorkOrderNo = h.WorkOrder != null ? h.WorkOrder.OrderNo : null,
                LotNumber = h.Lot != null ? h.Lot.LotNumber : null
            })
            .ToListAsync();

        var items = rawItems.Select(h => new HistoryDetailDto
        {
            Id = h.Id,
            ClientId = h.ClientId,
            ClientName = h.ClientName,
            RecipeName = h.RecipeName,
            IsPass = h.IsPass,
            NgCode = h.NgCode,
            ImagePath = h.ImagePath,
            InspectedAt = h.InspectedAt,
            WorkOrderId = h.WorkOrderId,
            LotId = h.LotId,
            WorkOrderNo = h.WorkOrderNo,
            LotNumber = h.LotNumber,
            ToolResults = h.ToolResults != null
                ? JsonSerializer.Deserialize<List<ToolResultItem>>(h.ToolResults)
                : null
        }).ToList();

        return new PagedResult<HistoryDetailDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<HistoryDetailDto?> GetHistoryDetailAsync(int id)
    {
        var h = await _db.InspectionHistories
            .Include(x => x.Client)
            .Include(x => x.WorkOrder)
            .Include(x => x.Lot)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (h is null) return null;

        return new HistoryDetailDto
        {
            Id = h.Id,
            ClientId = h.ClientId,
            ClientName = h.Client.Name,
            RecipeName = h.RecipeName,
            IsPass = h.IsPass,
            NgCode = h.NgCode,
            ImagePath = h.ImagePath,
            InspectedAt = h.InspectedAt,
            WorkOrderId = h.WorkOrderId,
            LotId = h.LotId,
            WorkOrderNo = h.WorkOrder?.OrderNo,
            LotNumber = h.Lot?.LotNumber,
            ToolResults = h.ToolResults != null
                ? JsonSerializer.Deserialize<List<ToolResultItem>>(h.ToolResults)
                : null
        };
    }

    public async Task<List<HistorySummaryDto>> GetDailySummaryAsync(
        int? clientId, DateTime startDate, DateTime endDate,
        int? workOrderId = null, int? lotId = null)
    {
        var query = _db.InspectionHistories.AsQueryable();

        if (clientId.HasValue)
            query = query.Where(h => h.ClientId == clientId.Value);
        if (workOrderId.HasValue)
            query = query.Where(h => h.WorkOrderId == workOrderId.Value);
        if (lotId.HasValue)
            query = query.Where(h => h.LotId == lotId.Value);

        query = query.Where(h => h.InspectedAt >= startDate && h.InspectedAt <= endDate);

        return await query
            .GroupBy(h => h.InspectedAt.Date)
            .Select(g => new HistorySummaryDto
            {
                Date = g.Key,
                TotalCount = g.Count(),
                PassCount = g.Count(h => h.IsPass),
                NgCount = g.Count(h => !h.IsPass)
            })
            .OrderBy(s => s.Date)
            .ToListAsync();
    }

    public async Task<byte[]> ExportToExcelAsync(HistoryFilterDto filter)
    {
        // Get all matching records (no paging for export)
        filter.Page = 1;
        filter.PageSize = int.MaxValue;
        var result = await GetHistoryAsync(filter);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inspection History");

        // Headers
        worksheet.Cell(1, 1).Value = "ID";
        worksheet.Cell(1, 2).Value = "Client";
        worksheet.Cell(1, 3).Value = "Recipe";
        worksheet.Cell(1, 4).Value = "Work Order";
        worksheet.Cell(1, 5).Value = "Lot";
        worksheet.Cell(1, 6).Value = "Result";
        worksheet.Cell(1, 7).Value = "NG Code";
        worksheet.Cell(1, 8).Value = "Inspected At";

        // Style header
        var headerRange = worksheet.Range(1, 1, 1, 8);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;

        // Data
        for (int i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            var row = i + 2;
            worksheet.Cell(row, 1).Value = item.Id;
            worksheet.Cell(row, 2).Value = item.ClientName;
            worksheet.Cell(row, 3).Value = item.RecipeName ?? "";
            worksheet.Cell(row, 4).Value = item.WorkOrderNo ?? "";
            worksheet.Cell(row, 5).Value = item.LotNumber ?? "";
            worksheet.Cell(row, 6).Value = item.IsPass ? "PASS" : "NG";
            worksheet.Cell(row, 7).Value = item.NgCode ?? "";
            // InspectedAt 은 UTC 저장 — 화면(ToLocalTime)과 동일하게 로컬 시각으로 출력
            worksheet.Cell(row, 8).Value = item.InspectedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
