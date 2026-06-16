using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// WarehouseService — 스마트 글라스 입고 위치 조회(PoC).
/// 바코드로 활성 품목 1건을 찾아 위치 문자열(Zone-Rack-Level-Bin) + 좌표로 매핑.
/// </summary>
public class WarehouseServiceTests
{
    private static async Task SeedAsync(BodaVmsDbContext db, params WarehouseItem[] items)
    {
        db.WarehouseItems.AddRange(items);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetInboundLocationAsync_existing_barcode_returns_location_and_coord()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedAsync(ctx.Db, new WarehouseItem
        {
            Barcode = "8801234567890", Code = "P-1001", Name = "알루미늄 브라켓 A",
            Zone = "A", Rack = "01", Level = "2", Bin = "07",
            PosX = 1.0, PosY = 2.0, PosZ = 0.5, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        var svc = new WarehouseService(ctx.Db);

        var loc = await svc.GetInboundLocationAsync("8801234567890", "입고");

        loc.Should().NotBeNull();
        loc!.ItemCode.Should().Be("P-1001");
        loc.ItemName.Should().Be("알루미늄 브라켓 A");
        loc.LocationText.Should().Be("A-01-2-07");
        loc.Coord.Should().NotBeNull();
        loc.Coord!.X.Should().Be(1.0);
        loc.Coord.Y.Should().Be(2.0);
        loc.Coord.Z.Should().Be(0.5);
    }

    [Fact]
    public async Task GetInboundLocationAsync_unknown_barcode_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new WarehouseService(ctx.Db);

        var loc = await svc.GetInboundLocationAsync("0000000000000");

        loc.Should().BeNull();
    }

    [Fact]
    public async Task GetInboundLocationAsync_inactive_item_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedAsync(ctx.Db, new WarehouseItem
        {
            Barcode = "8801234567891", Code = "P-1002", Name = "비활성 품목",
            Zone = "B", Rack = "02", Level = "1", Bin = "03",
            IsActive = false, CreatedAt = DateTime.UtcNow
        });
        var svc = new WarehouseService(ctx.Db);

        var loc = await svc.GetInboundLocationAsync("8801234567891");

        loc.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetInboundLocationAsync_blank_barcode_returns_null(string? barcode)
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new WarehouseService(ctx.Db);

        var loc = await svc.GetInboundLocationAsync(barcode!);

        loc.Should().BeNull();
    }

    [Fact]
    public async Task GetInboundLocationAsync_partial_location_fields_join_skips_blanks()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedAsync(ctx.Db, new WarehouseItem
        {
            Barcode = "8801234567892", Code = "P-1003", Name = "위치 일부만",
            Zone = "C", Rack = null, Level = "", Bin = "04",
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        var svc = new WarehouseService(ctx.Db);

        var loc = await svc.GetInboundLocationAsync("8801234567892");

        // null/빈 칸은 건너뛰고 조인 → "C-04"
        loc!.LocationText.Should().Be("C-04");
    }
}
