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
            .Include(w => w.Items).ThenInclude(i => i.Recipe)
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
            .Include(w => w.Items).ThenInclude(i => i.Recipe)
            .FirstOrDefaultAsync(w => w.Id == id);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<WorkOrderDto> CreateAsync(WorkOrderDto dto)
    {
        var orderNo = string.IsNullOrWhiteSpace(dto.OrderNo)
            ? await GenerateNextOrderNoAsync()
            : dto.OrderNo.Trim();

        // 라인 정규화 — 미전달(구버전/단일 UX)은 본체 (RecipeId, PlannedQuantity) 로 라인 1개.
        var lines = NormalizeItems(dto);

        var entity = new WorkOrder
        {
            OrderNo = orderNo,
            ProductId = dto.ProductId,
            ClientId = dto.ClientId,
            RecipeId = lines[0].RecipeId,                       // 대표 레시피 (기존 화면 호환)
            PlannedQuantity = lines.Sum(l => l.PlannedQty),     // 롤업
            Status = WorkOrderStatus.Planned,
            // 미지정/잘못된 값은 기본 Produced(총 생산 기준) — 양품 채우기는 명시적
            // opt-in (2026-08-19 정책, 초과 생산 방지)
            CompletionBasis = WorkOrderCompletionBasis.IsValid(dto.CompletionBasis)
                ? dto.CompletionBasis
                : WorkOrderCompletionBasis.Produced,
            PlannedStartAt = dto.PlannedStartAt,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };
        foreach (var l in lines)
            entity.Items.Add(new WorkOrderItem { RecipeId = l.RecipeId, PlannedQty = l.PlannedQty });

        _db.WorkOrders.Add(entity);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(entity.Id))!;
    }

    public async Task<WorkOrderDto?> UpdateAsync(int id, WorkOrderDto dto)
    {
        var entity = await _db.WorkOrders
            .Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (entity is null) return null;

        // Closed 상태는 수정 불가 (감사 추적성 보호)
        if (entity.Status == WorkOrderStatus.Closed)
            throw new InvalidOperationException("Closed WorkOrder는 수정할 수 없습니다.");

        entity.ProductId = dto.ProductId;
        entity.ClientId = dto.ClientId;
        if (WorkOrderCompletionBasis.IsValid(dto.CompletionBasis))
            entity.CompletionBasis = dto.CompletionBasis;
        entity.PlannedStartAt = dto.PlannedStartAt;
        entity.Note = dto.Note;

        // 라인 편집 규칙 (혼합 레시피 WO):
        //  - Planned: 전체 교체 (추가/삭제/수량 변경 자유)
        //  - InProgress: 기존 라인의 계획 수량 조정만 허용 (실적이 붙은 라인의 추가/삭제는
        //    수량 오염 위험 — 라인 매칭은 RecipeId 기준, 미매칭 항목은 무시)
        //  - Completed: 라인 변경 무시
        // 본체 RecipeId/PlannedQuantity 는 라인에서 파생(대표 레시피/롤업)한다.
        var lines = NormalizeItems(dto, allowEmpty: entity.Status != WorkOrderStatus.Planned);
        if (entity.Status == WorkOrderStatus.Planned && lines.Count > 0)
        {
            _db.WorkOrderItems.RemoveRange(entity.Items);
            entity.Items.Clear();
            foreach (var l in lines)
                entity.Items.Add(new WorkOrderItem { RecipeId = l.RecipeId, PlannedQty = l.PlannedQty });
        }
        else if (entity.Status == WorkOrderStatus.InProgress && lines.Count > 0)
        {
            foreach (var l in lines)
            {
                var existing = entity.Items.FirstOrDefault(i => i.RecipeId == l.RecipeId);
                if (existing != null)
                    existing.PlannedQty = l.PlannedQty;
            }
        }

        if (entity.Items.Count > 0)
        {
            entity.RecipeId = entity.Items.OrderBy(i => i.Id).First().RecipeId;
            entity.PlannedQuantity = entity.Items.Sum(i => i.PlannedQty);
        }
        else
        {
            // 라인이 아직 없는 구버전 데이터 경로 — 기존 동작 유지
            entity.RecipeId = dto.RecipeId;
            entity.PlannedQuantity = dto.PlannedQuantity;
        }

        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetByIdAsync(id);
    }

    /// <summary>
    /// DTO 라인 정규화 — 비어 있으면 본체 (RecipeId, PlannedQuantity) 로 라인 1개 구성
    /// (구버전 호환). 검증: 레시피 지정·수량 > 0·레시피 중복 금지.
    /// <paramref name="allowEmpty"/> 면 빈 목록을 그대로 반환 (라인 변경 없음 의미).
    /// </summary>
    private static List<WorkOrderItemDto> NormalizeItems(WorkOrderDto dto, bool allowEmpty = false)
    {
        var lines = dto.Items?.Where(i => i is not null).ToList() ?? new List<WorkOrderItemDto>();

        if (lines.Count == 0)
        {
            if (allowEmpty) return lines;
            lines.Add(new WorkOrderItemDto { RecipeId = dto.RecipeId, PlannedQty = dto.PlannedQuantity });
        }

        if (lines.Any(l => l.RecipeId <= 0))
            throw new ArgumentException("라인의 레시피가 지정되지 않았습니다.");
        if (lines.Any(l => l.PlannedQty <= 0))
            throw new ArgumentException("라인의 계획 수량은 1 이상이어야 합니다.");
        if (lines.Select(l => l.RecipeId).Distinct().Count() != lines.Count)
            throw new ArgumentException("같은 레시피를 두 라인에 등록할 수 없습니다.");

        return lines;
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
        CompletionBasis = w.CompletionBasis,
        PlannedStartAt = w.PlannedStartAt,
        ActualStartAt = w.ActualStartAt,
        ActualEndAt = w.ActualEndAt,
        Note = w.Note,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
        Items = w.Items
            .OrderBy(i => i.Id)
            .Select(i => new WorkOrderItemDto
            {
                Id = i.Id,
                RecipeId = i.RecipeId,
                RecipeName = i.Recipe?.Name,
                PlannedQty = i.PlannedQty,
                ProducedQty = i.ProducedQty,
                PassQty = i.PassQty,
                NgQty = i.NgQty
            })
            .ToList()
    };
}
