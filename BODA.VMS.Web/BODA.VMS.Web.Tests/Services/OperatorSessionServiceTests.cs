using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// OperatorSessionService — 라인별 활성 세션이 정확히 1개 유지 + 강제 교대시 적절한 EndReason
/// (Auto / ShiftChange) 부여 + 비활성 작업자 세션 시작 차단 검증. SignalR broadcast 는
/// NoopHubContext 로 삼킴 — REST 응답 본체 로직만 검증.
/// </summary>
public class OperatorSessionServiceTests
{
    private const int ClientA = 1;
    private const int ClientB = 2;
    private const int ClientIndexA = 0;
    private const int ClientIndexB = 1;

    private static OperatorSessionService BuildService(BodaVmsDbContext db)
        => new(db, new NoopHubContext<VmsHub>());

    private static async Task<(int op1, int op2)> SeedClientsAndOperatorsAsync(BodaVmsDbContext db)
    {
        // VisionClient ValueGeneratedNever — Id 명시
        db.Clients.AddRange(
            new VisionClient { Id = ClientA, Name = "L0", IpAddress = "127.0.0.1", ClientIndex = ClientIndexA, IsActive = true, CreatedAt = DateTime.UtcNow },
            new VisionClient { Id = ClientB, Name = "L1", IpAddress = "127.0.0.2", ClientIndex = ClientIndexB, IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        db.Operators.AddRange(
            new Operator { EmployeeNumber = "E001", Name = "Alice", PinHash = "x", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Operator { EmployeeNumber = "E002", Name = "Bob",   PinHash = "x", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var ops = db.Operators.OrderBy(o => o.Id).ToList();
        return (ops[0].Id, ops[1].Id);
    }

    // ─── StartSession ──────────────────────────────────────

    [Fact]
    public async Task StartSessionAsync_creates_first_session_with_no_endreason()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        var dto = await svc.StartSessionAsync(ClientA, op1);

        dto.OperatorId.Should().Be(op1);
        dto.ClientId.Should().Be(ClientA);
        dto.IsActive.Should().BeTrue();
        dto.EndReason.Should().BeNull();
        ctx.Db.OperatorSessions.Should().ContainSingle();
    }

    [Fact]
    public async Task StartSessionAsync_different_operator_closes_prev_with_Auto_reason()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, op2) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        await svc.StartSessionAsync(ClientA, op2);

        var sessions = ctx.Db.OperatorSessions.OrderBy(s => s.StartedAt).ToList();
        sessions.Should().HaveCount(2);
        sessions[0].EndedAt.Should().NotBeNull();
        sessions[0].EndReason.Should().Be(SessionEndReason.Auto);
        sessions[1].EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task StartSessionAsync_same_operator_re_login_closes_prev_with_ShiftChange()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        await svc.StartSessionAsync(ClientA, op1);

        var sessions = ctx.Db.OperatorSessions.OrderBy(s => s.StartedAt).ToList();
        sessions.Should().HaveCount(2);
        sessions[0].EndReason.Should().Be(SessionEndReason.ShiftChange);
    }

    [Fact]
    public async Task StartSessionAsync_does_not_disturb_sessions_on_other_clients()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, op2) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        await svc.StartSessionAsync(ClientB, op2);

        // 라인별로 활성 세션 각각 유지
        var active = ctx.Db.OperatorSessions.Where(s => s.EndedAt == null).ToList();
        active.Should().HaveCount(2);
        active.Select(s => s.ClientId).Should().BeEquivalentTo(new[] { ClientA, ClientB });
    }

    [Fact]
    public async Task StartSessionAsync_throws_when_client_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        var act = () => svc.StartSessionAsync(999, op1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Client*999*");
    }

    [Fact]
    public async Task StartSessionAsync_throws_when_operator_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        var act = () => svc.StartSessionAsync(ClientA, 999);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Operator*999*");
    }

    [Fact]
    public async Task StartSessionAsync_blocks_inactive_operator()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        // 비활성화
        var inactive = ctx.Db.Operators.Find(op1)!;
        inactive.IsActive = false;
        await ctx.Db.SaveChangesAsync();

        var svc = BuildService(ctx.Db);
        var act = () => svc.StartSessionAsync(ClientA, op1);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*비활성*");
    }

    // ─── EndSession ────────────────────────────────────────

    [Fact]
    public async Task EndSessionAsync_closes_active_session_with_reason()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        var ended = await svc.EndSessionAsync(ClientA, SessionEndReason.Logout);

        ended.Should().NotBeNull();
        ended!.EndedAt.Should().NotBeNull();
        ended.EndReason.Should().Be(SessionEndReason.Logout);
        ended.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task EndSessionAsync_returns_null_when_no_active_session()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        (await svc.EndSessionAsync(ClientA, SessionEndReason.Logout)).Should().BeNull();
    }

    // ─── GetCurrent / ResolveCurrentOperatorId ──────────────

    [Fact]
    public async Task GetCurrentByClientIdAsync_returns_active_session_or_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        (await svc.GetCurrentByClientIdAsync(ClientA)).Should().BeNull();

        await svc.StartSessionAsync(ClientA, op1);
        var current = await svc.GetCurrentByClientIdAsync(ClientA);
        current.Should().NotBeNull();
        current!.OperatorId.Should().Be(op1);

        await svc.EndSessionAsync(ClientA, SessionEndReason.Logout);
        (await svc.GetCurrentByClientIdAsync(ClientA)).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentByClientIndexAsync_resolves_via_index_lookup()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);

        var byIndex = await svc.GetCurrentByClientIndexAsync(ClientIndexA);
        byIndex.Should().NotBeNull();
        byIndex!.ClientId.Should().Be(ClientA);

        (await svc.GetCurrentByClientIndexAsync(999)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveCurrentOperatorIdAsync_returns_active_operator_or_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, _) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        (await svc.ResolveCurrentOperatorIdAsync(ClientA)).Should().BeNull();

        await svc.StartSessionAsync(ClientA, op1);
        (await svc.ResolveCurrentOperatorIdAsync(ClientA)).Should().Be(op1);
    }

    // ─── GetHistory ────────────────────────────────────────

    [Fact]
    public async Task GetHistoryAsync_filters_by_client_operator_and_date()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, op2) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        await svc.StartSessionAsync(ClientA, op2);  // op1 세션 자동 Auto 종료
        await svc.StartSessionAsync(ClientB, op1);

        var byClientA = await svc.GetHistoryAsync(ClientA, null, null, null);
        byClientA.Should().HaveCount(2);

        var byOp1 = await svc.GetHistoryAsync(null, op1, null, null);
        byOp1.Should().HaveCount(2);

        // StartedAt 미래 → 빈 결과
        var futureOnly = await svc.GetHistoryAsync(null, null, DateTime.UtcNow.AddDays(1), null);
        futureOnly.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_orders_desc_and_respects_limit()
    {
        using var ctx = new InMemorySqliteDbContext();
        var (op1, op2) = await SeedClientsAndOperatorsAsync(ctx.Db);
        var svc = BuildService(ctx.Db);

        await svc.StartSessionAsync(ClientA, op1);
        await svc.StartSessionAsync(ClientA, op2);
        await svc.StartSessionAsync(ClientA, op1);

        var top2 = await svc.GetHistoryAsync(null, null, null, null, limit: 2);
        top2.Should().HaveCount(2);
        top2.Should().BeInDescendingOrder(s => s.StartedAt);
    }
}
