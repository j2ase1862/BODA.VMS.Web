using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class WarehouseService : IWarehouseService
{
    private readonly BodaVmsDbContext _db;
    private readonly IHubContext<VmsHub> _hub;

    public WarehouseService(BodaVmsDbContext db, IHubContext<VmsHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<InboundLocationDto?> GetInboundLocationAsync(string barcode, string? mode = null)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        // string.Join 은 SQL 로 번역되지 않으므로 엔티티를 먼저 materialize 한 뒤 메모리에서 조립.
        var item = await _db.WarehouseItems
            .Where(i => i.Barcode == barcode && i.IsActive)
            .FirstOrDefaultAsync();

        if (item is null)
            return null;

        var parts = new[] { item.Zone, item.Rack, item.Level, item.Bin }
            .Where(s => !string.IsNullOrEmpty(s));

        return new InboundLocationDto
        {
            ItemCode     = item.Code,
            ItemName     = item.Name,
            LocationText = string.Join("-", parts),
            Coord        = new Coord3D { X = item.PosX, Y = item.PosY, Z = item.PosZ }
        };
    }

    public async Task<InboundConfirmResult?> ConfirmInboundAsync(string barcode, int qty = 1)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var code = barcode.Trim();
        var item = await _db.WarehouseItems
            .FirstOrDefaultAsync(i => i.Barcode == code && i.IsActive);
        if (item is null)
            return null;

        var step = qty <= 0 ? 1 : qty;
        var now = DateTime.UtcNow;
        item.StockQty += step;
        item.LastInboundAt = now;

        var loc = string.Join("-", new[] { item.Zone, item.Rack, item.Level, item.Bin }
            .Where(s => !string.IsNullOrEmpty(s)));

        _db.InboundReceipts.Add(new InboundReceipt
        {
            Barcode      = item.Barcode,
            ItemCode     = item.Code,
            ItemName     = item.Name,
            LocationText = loc,
            Qty          = step,
            StockAfter   = item.StockQty,
            ConfirmedAt  = now
        });
        await _db.SaveChangesAsync();

        var result = new InboundConfirmResult
        {
            Confirmed       = true,
            WarehouseItemId = item.Id,
            Barcode         = item.Barcode,
            ItemCode        = item.Code,
            ItemName        = item.Name,
            LocationText    = loc,
            Qty             = step,
            StockAfter      = item.StockQty,
            ConfirmedAt     = now,
            Message         = $"{item.Name} 입고 확정 (재고 {item.StockQty})"
        };

        // 관리 화면 실시간 반영(글라스 입고 확정 즉시) — 출고의 OutboundOrderChanged 와 대칭.
        await _hub.Clients.All.SendAsync("InboundConfirmed", result);
        return result;
    }

    // === 입고 위치 마스터 관리(등록/수정) ===

    public async Task<List<WarehouseItemDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.WarehouseItems.AsQueryable();
        if (!includeInactive)
            query = query.Where(w => w.IsActive);

        return await query
            .OrderBy(w => w.Barcode)
            .Select(w => ToDto(w))
            .ToListAsync();
    }

    public async Task<WarehouseItemDto?> GetByIdAsync(int id)
    {
        var e = await _db.WarehouseItems.FindAsync(id);
        return e is null ? null : ToDto(e);
    }

    public async Task<WarehouseItemDto> CreateAsync(WarehouseItemDto dto)
    {
        var barcode = dto.Barcode.Trim();
        if (await _db.WarehouseItems.AnyAsync(w => w.Barcode == barcode))
            throw new DuplicateBarcodeException(barcode);

        var e = new WarehouseItem
        {
            Barcode   = barcode,
            Code      = dto.Code.Trim(),
            Name      = dto.Name.Trim(),
            Zone      = Norm(dto.Zone),
            Rack      = Norm(dto.Rack),
            Level     = Norm(dto.Level),
            Bin       = Norm(dto.Bin),
            PosX      = dto.PosX,
            PosY      = dto.PosY,
            PosZ      = dto.PosZ,
            IsActive  = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        _db.WarehouseItems.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<WarehouseItemDto?> UpdateAsync(int id, WarehouseItemDto dto)
    {
        var e = await _db.WarehouseItems.FindAsync(id);
        if (e is null) return null;

        var barcode = dto.Barcode.Trim();
        if (await _db.WarehouseItems.AnyAsync(w => w.Barcode == barcode && w.Id != id))
            throw new DuplicateBarcodeException(barcode);

        e.Barcode   = barcode;
        e.Code      = dto.Code.Trim();
        e.Name      = dto.Name.Trim();
        e.Zone      = Norm(dto.Zone);
        e.Rack      = Norm(dto.Rack);
        e.Level     = Norm(dto.Level);
        e.Bin       = Norm(dto.Bin);
        e.PosX      = dto.PosX;
        e.PosY      = dto.PosY;
        e.PosZ      = dto.PosZ;
        e.IsActive  = dto.IsActive;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var e = await _db.WarehouseItems.FindAsync(id);
        if (e is null) return false;
        _db.WarehouseItems.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static WarehouseItemDto ToDto(WarehouseItem e) => new()
    {
        Id        = e.Id,
        Barcode   = e.Barcode,
        Code      = e.Code,
        Name      = e.Name,
        Zone      = e.Zone,
        Rack      = e.Rack,
        Level     = e.Level,
        Bin       = e.Bin,
        PosX          = e.PosX,
        PosY          = e.PosY,
        PosZ          = e.PosZ,
        IsActive      = e.IsActive,
        StockQty      = e.StockQty,
        LastInboundAt = e.LastInboundAt,
        CreatedAt     = e.CreatedAt,
        UpdatedAt     = e.UpdatedAt
    };
}
