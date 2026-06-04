using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// LotService — WorkOrder 와 짝지어 추적성(IATF 16949) 핵심 단위. LotNumber 채번 규칙
/// (`{YYYYMMDD}-{OrderNo}-{Sequence:D3}`), Open/Closed 상태, Closed WorkOrder 차단 검증.
/// </summary>
public class LotServiceTests
{
    private const int ClientId = 1;
    private const int RecipeId = 1;

    // VisionClient/Recipe 는 ValueGeneratedNever — Id 명시 부여 필수
    private static async Task<WorkOrder> SeedWorkOrderAsync(
        BodaVmsDbContext db, string orderNo = "WO-TEST-001", string status = WorkOrderStatus.Planned)
    {
        db.Clients.Add(new VisionClient
        {
            Id = ClientId, Name = "L0", IpAddress = "127.0.0.1", ClientIndex = 0,
            IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.Recipes.Add(new Recipe
        {
            Id = RecipeId, Name = "R0", ClientId = ClientId, CreatedAt = DateTime.UtcNow
        });
        db.Products.Add(new Product
        {
            Code = "P-001", Name = "Sample", IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var product = db.Products.First();
        var wo = new WorkOrder
        {
            OrderNo = orderNo,
            ProductId = product.Id,
            ClientId = ClientId,
            RecipeId = RecipeId,
            PlannedQuantity = 100,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo;
    }

    // ─── Create ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_generates_lotnumber_with_orderno_and_sequence()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db, "WO-CASE-7");
        var svc = new LotService(ctx.Db);

        var lot = await svc.CreateAsync(wo.Id, note: "first lot");

        var todayPrefix = $"{DateTime.Now:yyyyMMdd}-WO-CASE-7-";
        lot.LotNumber.Should().StartWith(todayPrefix);
        lot.LotNumber.Should().EndWith("001");
        lot.Sequence.Should().Be(1);
        lot.Status.Should().Be(LotStatus.Open);
        lot.Note.Should().Be("first lot");
    }

    [Fact]
    public async Task CreateAsync_increments_sequence_within_same_workorder()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db);
        var svc = new LotService(ctx.Db);

        var lot1 = await svc.CreateAsync(wo.Id, null);
        var lot2 = await svc.CreateAsync(wo.Id, null);
        var lot3 = await svc.CreateAsync(wo.Id, null);

        lot1.Sequence.Should().Be(1);
        lot2.Sequence.Should().Be(2);
        lot3.Sequence.Should().Be(3);
        lot3.LotNumber.Should().EndWith("003");
    }

    [Fact]
    public async Task CreateAsync_throws_when_workorder_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new LotService(ctx.Db);

        var act = () => svc.CreateAsync(workOrderId: 999, note: null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*");
    }

    [Fact]
    public async Task CreateAsync_blocks_when_workorder_is_closed()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db, status: WorkOrderStatus.Closed);
        var svc = new LotService(ctx.Db);

        // 추적성: Closed 오더에 추가 Lot 발생 시 감사 흐름 깨짐 → 차단
        var act = () => svc.CreateAsync(wo.Id, null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Closed*");
    }

    // ─── Read ───────────────────────────────────────────────

    [Fact]
    public async Task GetByWorkOrderAsync_returns_ordered_by_sequence_desc()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db);
        var svc = new LotService(ctx.Db);

        await svc.CreateAsync(wo.Id, null);
        await svc.CreateAsync(wo.Id, null);
        await svc.CreateAsync(wo.Id, null);

        var list = await svc.GetByWorkOrderAsync(wo.Id);
        list.Should().HaveCount(3);
        list.Select(l => l.Sequence).Should().ContainInOrder(3, 2, 1);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new LotService(ctx.Db);

        (await svc.GetByIdAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task GetByLotNumberAsync_finds_by_unique_number()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db, "WO-LOOKUP-1");
        var svc = new LotService(ctx.Db);

        var created = await svc.CreateAsync(wo.Id, null);
        var fetched = await svc.GetByLotNumberAsync(created.LotNumber);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.WorkOrderNo.Should().Be("WO-LOOKUP-1");
    }

    [Fact]
    public async Task GetActiveByWorkOrderAsync_returns_latest_open_lot()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db);
        var svc = new LotService(ctx.Db);

        var lot1 = await svc.CreateAsync(wo.Id, null);
        var lot2 = await svc.CreateAsync(wo.Id, null);
        await svc.CloseAsync(lot1.Id);

        var active = await svc.GetActiveByWorkOrderAsync(wo.Id);
        active.Should().NotBeNull();
        active!.Id.Should().Be(lot2.Id);
        active.Status.Should().Be(LotStatus.Open);
    }

    [Fact]
    public async Task GetActiveByWorkOrderAsync_returns_null_when_all_closed()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db);
        var svc = new LotService(ctx.Db);

        var lot = await svc.CreateAsync(wo.Id, null);
        await svc.CloseAsync(lot.Id);

        (await svc.GetActiveByWorkOrderAsync(wo.Id)).Should().BeNull();
    }

    // ─── Close ──────────────────────────────────────────────

    [Fact]
    public async Task CloseAsync_sets_status_and_closedat()
    {
        using var ctx = new InMemorySqliteDbContext();
        var wo = await SeedWorkOrderAsync(ctx.Db);
        var svc = new LotService(ctx.Db);

        var lot = await svc.CreateAsync(wo.Id, null);
        var before = DateTime.UtcNow;
        var closed = await svc.CloseAsync(lot.Id);

        closed.Should().NotBeNull();
        closed!.Status.Should().Be(LotStatus.Closed);
        closed.ClosedAt.Should().NotBeNull();
        closed.ClosedAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task CloseAsync_returns_null_when_lot_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new LotService(ctx.Db);

        (await svc.CloseAsync(999)).Should().BeNull();
    }
}
