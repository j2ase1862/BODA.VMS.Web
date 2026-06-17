using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class OutboundService : IOutboundService
{
    private readonly BodaVmsDbContext _db;

    public OutboundService(BodaVmsDbContext db)
    {
        _db = db;
    }

    // === 글라스 피킹 (읽기 가이드) ===

    public async Task<PickListDto?> GetPickListAsync(string orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo))
            return null;

        var key = orderNo.Trim();
        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OrderNo == key);
        if (order is null)
            return null;

        var lines = await _db.OutboundOrderLines
            .Where(l => l.OrderId == order.Id)
            .OrderBy(l => l.Seq).ThenBy(l => l.Id)
            .ToListAsync();

        // 피킹 위치는 WarehouseItem(바코드 매칭)에서 — 배치 조회로 N+1 회피.
        var barcodes = lines.Select(l => l.Barcode).Distinct().ToList();
        var locByBarcode = (await _db.WarehouseItems
                .Where(w => barcodes.Contains(w.Barcode) && w.IsActive)
                .ToListAsync())
            .GroupBy(w => w.Barcode)
            .ToDictionary(g => g.Key, g => LocationOf(g.First()), StringComparer.Ordinal);

        return new PickListDto
        {
            OrderNo      = order.OrderNo,
            CustomerName = order.CustomerName,
            ShipTo       = order.ShipTo,
            Destination  = order.Destination,
            Status       = order.Status,
            Lines = lines.Select(l => new PickLineDto
            {
                Seq          = l.Seq,
                Barcode      = l.Barcode,
                ItemCode     = l.ItemCode,
                ItemName     = l.ItemName,
                Qty          = l.Qty,
                PickedQty    = l.PickedQty,
                LocationText = locByBarcode.GetValueOrDefault(l.Barcode, "")
            }).ToList()
        };
    }

    public async Task<PickConfirmResult?> ConfirmPickAsync(string orderNo, string barcode, int qty = 1)
    {
        if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(barcode))
            return null;

        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo.Trim());
        if (order is null) return null;

        var code = barcode.Trim();
        var step = qty <= 0 ? 1 : qty;

        var lines = await _db.OutboundOrderLines
            .Where(l => l.OrderId == order.Id)
            .OrderBy(l => l.Seq).ThenBy(l => l.Id)
            .ToListAsync();

        var matching = lines.Where(l => l.Barcode == code).ToList();
        var result = new PickConfirmResult();

        if (matching.Count == 0)
        {
            result.Matched = false;
            result.Message = "이 주문에 없는 품목입니다";
        }
        else
        {
            // 미완료(부족) 라인 우선 — 같은 바코드가 여러 줄이면 순서대로.
            var target = matching.FirstOrDefault(l => l.PickedQty < l.Qty);
            if (target is null)
            {
                result.AlreadyComplete = true;
                result.Message = "이미 전량 피킹된 품목입니다";
            }
            else
            {
                var remaining = target.Qty - target.PickedQty;
                target.PickedQty += Math.Min(step, remaining);
                order.Status = "Picking";
                order.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                result.Matched = true;
                result.Message = target.PickedQty >= target.Qty
                    ? $"{target.ItemName} 완료 ({target.PickedQty}/{target.Qty})"
                    : $"{target.ItemName} {target.PickedQty}/{target.Qty}";
            }
        }

        // 갱신된 상태 재조회
        result.PickList = (await GetPickListAsync(order.OrderNo))!;
        result.AllPicked = result.PickList.Lines.Count > 0
            && result.PickList.Lines.All(l => l.PickedQty >= l.Qty);
        return result;
    }

    public async Task<PickListDto?> ConfirmShipAsync(string orderNo)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) return null;

        var order = await _db.OutboundOrders.FirstOrDefaultAsync(o => o.OrderNo == orderNo.Trim());
        if (order is null) return null;

        order.Status = "Done";
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetPickListAsync(order.OrderNo);
    }

    // === 관리 ===

    public async Task<List<OutboundOrderDto>> GetAllAsync()
    {
        var orders = await _db.OutboundOrders.OrderByDescending(o => o.Id).ToListAsync();
        var ids = orders.Select(o => o.Id).ToList();
        var lines = await _db.OutboundOrderLines
            .Where(l => ids.Contains(l.OrderId))
            .ToListAsync();
        var linesByOrder = lines.GroupBy(l => l.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Seq).ThenBy(l => l.Id).ToList());

        return orders.Select(o => ToDto(o, linesByOrder.GetValueOrDefault(o.Id) ?? new())).ToList();
    }

    public async Task<OutboundOrderDto?> GetByIdAsync(int id)
    {
        var order = await _db.OutboundOrders.FindAsync(id);
        if (order is null) return null;
        var lines = await _db.OutboundOrderLines
            .Where(l => l.OrderId == id)
            .OrderBy(l => l.Seq).ThenBy(l => l.Id)
            .ToListAsync();
        return ToDto(order, lines);
    }

    public async Task<OutboundOrderDto> CreateAsync(OutboundOrderDto dto)
    {
        var orderNo = dto.OrderNo.Trim();
        if (await _db.OutboundOrders.AnyAsync(o => o.OrderNo == orderNo))
            throw new DuplicateOrderNoException(orderNo);

        var order = new OutboundOrder
        {
            OrderNo      = orderNo,
            CustomerName = Norm(dto.CustomerName),
            ShipTo       = Norm(dto.ShipTo),
            Destination  = Norm(dto.Destination),
            Status       = string.IsNullOrWhiteSpace(dto.Status) ? "Pending" : dto.Status,
            CreatedAt    = DateTime.UtcNow
        };
        _db.OutboundOrders.Add(order);
        await _db.SaveChangesAsync();

        await ReplaceLinesAsync(order.Id, dto.Lines);
        return (await GetByIdAsync(order.Id))!;
    }

    public async Task<OutboundOrderDto?> UpdateAsync(int id, OutboundOrderDto dto)
    {
        var order = await _db.OutboundOrders.FindAsync(id);
        if (order is null) return null;

        var orderNo = dto.OrderNo.Trim();
        if (await _db.OutboundOrders.AnyAsync(o => o.OrderNo == orderNo && o.Id != id))
            throw new DuplicateOrderNoException(orderNo);

        order.OrderNo      = orderNo;
        order.CustomerName = Norm(dto.CustomerName);
        order.ShipTo       = Norm(dto.ShipTo);
        order.Destination  = Norm(dto.Destination);
        order.Status       = string.IsNullOrWhiteSpace(dto.Status) ? "Pending" : dto.Status;
        order.UpdatedAt    = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await ReplaceLinesAsync(id, dto.Lines);
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var order = await _db.OutboundOrders.FindAsync(id);
        if (order is null) return false;

        var lines = await _db.OutboundOrderLines.Where(l => l.OrderId == id).ToListAsync();
        _db.OutboundOrderLines.RemoveRange(lines);
        _db.OutboundOrders.Remove(order);
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>라인 전체 교체. 품목코드/명이 비어 있으면 WarehouseItem에서 자동 채움.</summary>
    private async Task ReplaceLinesAsync(int orderId, List<OutboundOrderLineDto> dtos)
    {
        var existing = await _db.OutboundOrderLines.Where(l => l.OrderId == orderId).ToListAsync();
        if (existing.Count > 0) _db.OutboundOrderLines.RemoveRange(existing);

        var barcodes = dtos.Select(d => d.Barcode.Trim()).Where(b => b.Length > 0).Distinct().ToList();
        var whByBarcode = (await _db.WarehouseItems
                .Where(w => barcodes.Contains(w.Barcode))
                .ToListAsync())
            .GroupBy(w => w.Barcode)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var seq = 1;
        foreach (var d in dtos)
        {
            var barcode = d.Barcode.Trim();
            if (barcode.Length == 0) continue;

            var code = d.ItemCode?.Trim() ?? "";
            var name = d.ItemName?.Trim() ?? "";
            if ((code.Length == 0 || name.Length == 0) && whByBarcode.TryGetValue(barcode, out var wh))
            {
                if (code.Length == 0) code = wh.Code;
                if (name.Length == 0) name = wh.Name;
            }

            _db.OutboundOrderLines.Add(new OutboundOrderLine
            {
                OrderId   = orderId,
                Barcode   = barcode,
                ItemCode  = code,
                ItemName  = name,
                Qty       = d.Qty <= 0 ? 1 : d.Qty,
                PickedQty = d.PickedQty,
                Seq       = seq++
            });
        }
        await _db.SaveChangesAsync();
    }

    private static string LocationOf(WarehouseItem w) =>
        string.Join("-", new[] { w.Zone, w.Rack, w.Level, w.Bin }.Where(s => !string.IsNullOrEmpty(s)));

    private static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static OutboundOrderDto ToDto(OutboundOrder o, List<OutboundOrderLine> lines) => new()
    {
        Id           = o.Id,
        OrderNo      = o.OrderNo,
        CustomerName = o.CustomerName,
        ShipTo       = o.ShipTo,
        Destination  = o.Destination,
        Status       = o.Status,
        CreatedAt    = o.CreatedAt,
        UpdatedAt    = o.UpdatedAt,
        Lines = lines.Select(l => new OutboundOrderLineDto
        {
            Id = l.Id, Barcode = l.Barcode, ItemCode = l.ItemCode,
            ItemName = l.ItemName, Qty = l.Qty, PickedQty = l.PickedQty
        }).ToList()
    };
}
