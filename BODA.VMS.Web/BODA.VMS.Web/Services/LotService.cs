using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class LotService : ILotService
{
    private readonly BodaVmsDbContext _db;

    public LotService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<LotDto>> GetByWorkOrderAsync(int workOrderId)
    {
        return await _db.Lots
            .Include(l => l.WorkOrder)
            .Where(l => l.WorkOrderId == workOrderId)
            .OrderByDescending(l => l.Sequence)
            .Select(l => ToDto(l))
            .ToListAsync();
    }

    public async Task<LotDto?> GetByIdAsync(int id)
    {
        var entity = await _db.Lots
            .Include(l => l.WorkOrder)
            .FirstOrDefaultAsync(l => l.Id == id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<LotDto?> GetByLotNumberAsync(string lotNumber)
    {
        var entity = await _db.Lots
            .Include(l => l.WorkOrder)
            .FirstOrDefaultAsync(l => l.LotNumber == lotNumber);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<LotDto?> GetActiveByWorkOrderAsync(int workOrderId)
    {
        // 가장 최근 Open Lot 1개 — Sequence DESC. 없으면 null.
        var entity = await _db.Lots
            .Include(l => l.WorkOrder)
            .Where(l => l.WorkOrderId == workOrderId && l.Status == LotStatus.Open)
            .OrderByDescending(l => l.Sequence)
            .FirstOrDefaultAsync();
        return entity is null ? null : ToDto(entity);
    }

    public async Task<LotDto> CreateAsync(int workOrderId, string? note)
    {
        var workOrder = await _db.WorkOrders.FindAsync(workOrderId)
            ?? throw new InvalidOperationException($"WorkOrder {workOrderId} not found.");

        if (workOrder.Status == WorkOrderStatus.Closed)
            throw new InvalidOperationException("Closed WorkOrder에는 Lot을 추가할 수 없습니다.");

        var nextSeq = (await _db.Lots
            .Where(l => l.WorkOrderId == workOrderId)
            .MaxAsync(l => (int?)l.Sequence) ?? 0) + 1;

        var lotNumber = $"{DateTime.Now:yyyyMMdd}-{workOrder.OrderNo}-{nextSeq:D3}";

        var entity = new Lot
        {
            LotNumber = lotNumber,
            WorkOrderId = workOrderId,
            Sequence = nextSeq,
            Status = LotStatus.Open,
            Note = note,
            CreatedAt = DateTime.UtcNow
        };

        _db.Lots.Add(entity);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<LotDto?> CloseAsync(int id)
    {
        var entity = await _db.Lots.FindAsync(id);
        if (entity is null) return null;

        entity.Status = LotStatus.Closed;
        entity.ClosedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    private static LotDto ToDto(Lot l) => new()
    {
        Id = l.Id,
        LotNumber = l.LotNumber,
        WorkOrderId = l.WorkOrderId,
        WorkOrderNo = l.WorkOrder?.OrderNo,
        Sequence = l.Sequence,
        Quantity = l.Quantity,
        PassCount = l.PassCount,
        NgCount = l.NgCount,
        Status = l.Status,
        CreatedAt = l.CreatedAt,
        ClosedAt = l.ClosedAt,
        Note = l.Note
    };
}
