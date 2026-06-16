using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class WarehouseService : IWarehouseService
{
    private readonly BodaVmsDbContext _db;

    public WarehouseService(BodaVmsDbContext db)
    {
        _db = db;
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
}
