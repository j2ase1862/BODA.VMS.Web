using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// WorkOrderService 상태 머신(Planned → InProgress → Completed → Closed) + 채번/삭제 규칙 검증.
/// 운영 데이터 추적성/감사성 핵심 — 잘못된 전이가 통과하면 IATF/ISO 추적성 무너짐.
/// </summary>
public class WorkOrderServiceTests
{
    // VisionClient/Recipe 는 BodaVmsDbContext 가 ValueGeneratedNever — VisionServer 가 ID 관리.
    // 테스트에서는 Id 명시 부여 필수 (EF 가 0 으로 두 엔티티 동시 추적 시 충돌).
    private static async Task<(int productId, int clientId, int recipeId)> SeedFkParentsAsync(
        BodaVmsDbContext db, int clientId = 1, int recipeId = 1, int clientIndex = 0)
    {
        var client = new VisionClient
        {
            Id = clientId,
            Name = $"L{clientIndex}", IpAddress = $"127.0.0.{clientIndex + 1}", ClientIndex = clientIndex,
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var recipe = new Recipe
        {
            Id = recipeId,
            Name = $"R{clientIndex}", ClientId = client.Id, CreatedAt = DateTime.UtcNow
        };
        db.Recipes.Add(recipe);

        var product = new Product
        {
            Code = $"P-{clientIndex:D3}", Name = $"Sample{clientIndex}",
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);

        await db.SaveChangesAsync();
        return (product.Id, client.Id, recipe.Id);
    }

    private static WorkOrderDto MakeDto(int productId, int clientId, int recipeId, string? orderNo = null)
        => new()
        {
            OrderNo = orderNo ?? string.Empty,
            ProductId = productId,
            ClientId = clientId,
            RecipeId = recipeId,
            PlannedQuantity = 100
        };

    // ─── Create ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_generates_orderno_when_blank()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);

        var svc = new WorkOrderService(ctx.Db);
        var created = await svc.CreateAsync(MakeDto(pid, cid, rid));

        var expectedPrefix = $"WO-{DateTime.Now:yyyyMMdd}-";
        created.OrderNo.Should().StartWith(expectedPrefix);
        created.OrderNo.Should().EndWith("001");
        created.Status.Should().Be(WorkOrderStatus.Planned);
        created.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CreateAsync_trims_explicit_orderno()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);

        var svc = new WorkOrderService(ctx.Db);
        var created = await svc.CreateAsync(MakeDto(pid, cid, rid, "  WO-CUSTOM-1  "));

        created.OrderNo.Should().Be("WO-CUSTOM-1");
    }

    [Fact]
    public async Task GenerateNextOrderNoAsync_increments_sequence()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);

        var svc = new WorkOrderService(ctx.Db);
        var first = await svc.CreateAsync(MakeDto(pid, cid, rid));
        var second = await svc.CreateAsync(MakeDto(pid, cid, rid));
        var third = await svc.CreateAsync(MakeDto(pid, cid, rid));

        first.OrderNo.Should().EndWith("001");
        second.OrderNo.Should().EndWith("002");
        third.OrderNo.Should().EndWith("003");
    }

    // ─── Read / Filter ───────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_filters_by_status()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        await svc.CreateAsync(MakeDto(pid, cid, rid));
        var w2 = await svc.CreateAsync(MakeDto(pid, cid, rid));
        await svc.ChangeStatusAsync(w2.Id, "Start");

        var planned = await svc.GetAllAsync(status: WorkOrderStatus.Planned);
        var inProgress = await svc.GetAllAsync(status: WorkOrderStatus.InProgress);

        planned.Should().ContainSingle();
        inProgress.Should().ContainSingle().Which.Id.Should().Be(w2.Id);
    }

    [Fact]
    public async Task GetAllAsync_filters_by_clientid()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid1, rid) = await SeedFkParentsAsync(ctx.Db);

        var client2 = new VisionClient
        {
            Id = 2, Name = "L1", IpAddress = "127.0.0.2", ClientIndex = 1,
            IsActive = true, CreatedAt = DateTime.UtcNow
        };
        ctx.Db.Clients.Add(client2);
        await ctx.Db.SaveChangesAsync();
        var recipe2 = new Recipe { Id = 2, Name = "R1", ClientId = client2.Id, CreatedAt = DateTime.UtcNow };
        ctx.Db.Recipes.Add(recipe2);
        await ctx.Db.SaveChangesAsync();

        var svc = new WorkOrderService(ctx.Db);
        await svc.CreateAsync(MakeDto(pid, cid1, rid));
        await svc.CreateAsync(MakeDto(pid, client2.Id, recipe2.Id));

        var line0 = await svc.GetAllAsync(clientId: cid1);
        line0.Should().ContainSingle();
        line0[0].ClientId.Should().Be(cid1);
    }

    // ─── Update ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new WorkOrderService(ctx.Db);

        var result = await svc.UpdateAsync(999, new WorkOrderDto());
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_blocks_closed_workorder_for_audit_integrity()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        await svc.ChangeStatusAsync(w.Id, "Start");
        await svc.ChangeStatusAsync(w.Id, "Complete");
        await svc.ChangeStatusAsync(w.Id, "Close");

        // 감사 추적성: Closed 상태 수정 차단
        var act = () => svc.UpdateAsync(w.Id, MakeDto(pid, cid, rid));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Closed*");
    }

    // ─── Delete ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_returns_false_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new WorkOrderService(ctx.Db);

        (await svc.DeleteAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_succeeds_for_planned_without_lots()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        (await svc.DeleteAsync(w.Id)).Should().BeTrue();
        ctx.Db.WorkOrders.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_blocks_inprogress_workorder()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        await svc.ChangeStatusAsync(w.Id, "Start");

        // InProgress 는 Close 절차로만 종료 — 직접 삭제 금지
        var act = () => svc.DeleteAsync(w.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Close*");
    }

    [Fact]
    public async Task DeleteAsync_blocks_planned_workorder_with_lots()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);
        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));

        ctx.Db.Lots.Add(new Lot
        {
            LotNumber = "L-1", WorkOrderId = w.Id, Sequence = 1,
            Status = LotStatus.Open, CreatedAt = DateTime.UtcNow
        });
        await ctx.Db.SaveChangesAsync();

        var act = () => svc.DeleteAsync(w.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Lot*");
    }

    // ─── Status transitions ─────────────────────────────────

    [Fact]
    public async Task ChangeStatusAsync_Start_advances_planned_and_sets_actual_start()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        var before = DateTime.UtcNow;
        var started = await svc.ChangeStatusAsync(w.Id, "Start");

        started!.Status.Should().Be(WorkOrderStatus.InProgress);
        started.ActualStartAt.Should().NotBeNull();
        started.ActualStartAt!.Value.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task ChangeStatusAsync_Complete_advances_inprogress_and_sets_actual_end()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        await svc.ChangeStatusAsync(w.Id, "Start");
        var completed = await svc.ChangeStatusAsync(w.Id, "Complete");

        completed!.Status.Should().Be(WorkOrderStatus.Completed);
        completed.ActualEndAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ChangeStatusAsync_Close_advances_completed_only()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        await svc.ChangeStatusAsync(w.Id, "Start");
        await svc.ChangeStatusAsync(w.Id, "Complete");
        var closed = await svc.ChangeStatusAsync(w.Id, "Close");

        closed!.Status.Should().Be(WorkOrderStatus.Closed);
    }

    [Theory]
    [InlineData("Start", WorkOrderStatus.InProgress)]
    [InlineData("Complete", WorkOrderStatus.Planned)]
    [InlineData("Close", WorkOrderStatus.InProgress)]
    public async Task ChangeStatusAsync_blocks_invalid_source_state(string action, string startingStatus)
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));

        // 시작 상태로 강제 진행
        if (startingStatus == WorkOrderStatus.InProgress)
        {
            await svc.ChangeStatusAsync(w.Id, "Start");
        }
        else if (startingStatus == WorkOrderStatus.Completed)
        {
            await svc.ChangeStatusAsync(w.Id, "Start");
            await svc.ChangeStatusAsync(w.Id, "Complete");
        }

        var act = () => svc.ChangeStatusAsync(w.Id, action);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangeStatusAsync_unknown_action_throws_argument_exception()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (pid, cid, rid) = await SeedFkParentsAsync(ctx.Db);
        var svc = new WorkOrderService(ctx.Db);

        var w = await svc.CreateAsync(MakeDto(pid, cid, rid));
        var act = () => svc.ChangeStatusAsync(w.Id, "Reopen");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ChangeStatusAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new WorkOrderService(ctx.Db);

        var result = await svc.ChangeStatusAsync(999, "Start");
        result.Should().BeNull();
    }
}
