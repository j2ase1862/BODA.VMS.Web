using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class WorkOrderService : IWorkOrderService
{
    private readonly BodaVmsDbContext _db;

    public WorkOrderService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<WorkOrderDto>> GetAllAsync(string? status = null, int? clientId = null)
    {
        var query = _db.WorkOrders
            .Include(w => w.Product)
            .Include(w => w.Client)
            .Include(w => w.Recipe)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(w => w.Status == status);

        if (clientId.HasValue)
            query = query.Where(w => w.ClientId == clientId.Value);

        return await query
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => ToDto(w))
            .ToListAsync();
    }

    public async Task<WorkOrderDto?> GetByIdAsync(int id)
    {
        var entity = await _db.WorkOrders
            .Include(w => w.Product)
            .Include(w => w.Client)
            .Include(w => w.Recipe)
            .FirstOrDefaultAsync(w => w.Id == id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<WorkOrderDto> CreateAsync(WorkOrderDto dto)
    {
        var orderNo = string.IsNullOrWhiteSpace(dto.OrderNo)
            ? await GenerateNextOrderNoAsync()
            : dto.OrderNo.Trim();

        var entity = new WorkOrder
        {
            OrderNo = orderNo,
            ProductId = dto.ProductId,
            ClientId = dto.ClientId,
            RecipeId = dto.RecipeId,
            PlannedQuantity = dto.PlannedQuantity,
            Status = WorkOrderStatus.Planned,
            PlannedStartAt = dto.PlannedStartAt,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };

        _db.WorkOrders.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<WorkOrderDto?> UpdateAsync(int id, WorkOrderDto dto)
    {
        var entity = await _db.WorkOrders.FindAsync(id);
        if (entity is null) return null;

        // Closed 상태는 수정 불가 (감사 추적성 보호)
        if (entity.Status == WorkOrderStatus.Closed)
            throw new InvalidOperationException("Closed WorkOrder는 수정할 수 없습니다.");

        entity.ProductId = dto.ProductId;
        entity.ClientId = dto.ClientId;
        entity.RecipeId = dto.RecipeId;
        entity.PlannedQuantity = dto.PlannedQuantity;
        entity.PlannedStartAt = dto.PlannedStartAt;
        entity.Note = dto.Note;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.WorkOrders
            .Include(w => w.Lots)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (entity is null) return false;

        // Planned 상태에서만 삭제 허용 (이미 생산 시작된 오더는 Closed로만 종료)
        if (entity.Status != WorkOrderStatus.Planned)
            throw new InvalidOperationException(
                "이미 시작된 WorkOrder는 삭제할 수 없습니다. 대신 Close 처리하세요.");

        if (entity.Lots.Any())
            throw new InvalidOperationException("Lot이 있는 WorkOrder는 삭제할 수 없습니다.");

        _db.WorkOrders.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<WorkOrderDto?> ChangeStatusAsync(int id, string action)
    {
        var entity = await _db.WorkOrders.FindAsync(id);
        if (entity is null) return null;

        switch (action)
        {
            case "Start":
                if (entity.Status != WorkOrderStatus.Planned)
                    throw new InvalidOperationException($"Planned 상태에서만 시작 가능합니다. (현재: {entity.Status})");
                entity.Status = WorkOrderStatus.InProgress;
                entity.ActualStartAt = DateTime.UtcNow;
                break;

            case "Complete":
                if (entity.Status != WorkOrderStatus.InProgress)
                    throw new InvalidOperationException($"InProgress 상태에서만 완료 가능합니다. (현재: {entity.Status})");
                entity.Status = WorkOrderStatus.Completed;
                entity.ActualEndAt = DateTime.UtcNow;
                break;

            case "Close":
                if (entity.Status != WorkOrderStatus.Completed)
                    throw new InvalidOperationException($"Completed 상태에서만 마감 가능합니다. (현재: {entity.Status})");
                entity.Status = WorkOrderStatus.Closed;
                break;

            default:
                throw new ArgumentException($"알 수 없는 액션: {action}");
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    public async Task<string> GenerateNextOrderNoAsync()
    {
        var today = DateTime.Now.ToString("yyyyMMdd");
        var prefix = $"WO-{today}-";

        var maxSeq = await _db.WorkOrders
            .Where(w => w.OrderNo.StartsWith(prefix))
            .Select(w => w.OrderNo.Substring(prefix.Length))
            .ToListAsync();

        var nextSeq = maxSeq
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"{prefix}{nextSeq:D3}";
    }

    private static WorkOrderDto ToDto(WorkOrder w) => new()
    {
        Id = w.Id,
        OrderNo = w.OrderNo,
        ProductId = w.ProductId,
        ProductCode = w.Product?.Code,
        ProductName = w.Product?.Name,
        ClientId = w.ClientId,
        ClientName = w.Client?.Name,
        ClientIndex = w.Client?.ClientIndex,
        RecipeId = w.RecipeId,
        RecipeName = w.Recipe?.Name,
        PlannedQuantity = w.PlannedQuantity,
        ProducedQuantity = w.ProducedQuantity,
        PassQuantity = w.PassQuantity,
        NgQuantity = w.NgQuantity,
        Status = w.Status,
        PlannedStartAt = w.PlannedStartAt,
        ActualStartAt = w.ActualStartAt,
        ActualEndAt = w.ActualEndAt,
        Note = w.Note,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };
}
