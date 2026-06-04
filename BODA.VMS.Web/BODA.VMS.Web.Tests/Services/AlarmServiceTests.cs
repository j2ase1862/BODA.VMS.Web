using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// AlarmService — ISA-18.2 알람 생명주기 (New → Ack → Resolved) 검증.
/// Acknowledge 멱등성, Resolve 시 Ack 자동 채움(skip-ack 흐름), AcknowledgeAll
/// 일괄 처리, Summary 카운트, 필터/페이징. SignalR broadcast 는 NoopHubContext 로 삼킴.
/// </summary>
public class AlarmServiceTests
{
    private static AlarmService BuildService(BodaVmsDbContext db)
        => new(db, new NoopHubContext<VmsHub>());

    private static AlarmEvent MakeEvent(
        string title = "Sample",
        string type = AlarmEventType.NG,
        string severity = AlarmSeverity.Major,
        int? clientId = null,
        DateTime? occurredAt = null)
        => new()
        {
            Title = title,
            AlarmType = type,
            Severity = severity,
            ClientId = clientId,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
            Message = "test"
        };

    // ─── Create ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_persists_and_returns_dto_in_New_state()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var dto = await svc.CreateAsync(MakeEvent(title: "Lens Dirty"));

        dto.Title.Should().Be("Lens Dirty");
        dto.State.Should().Be("New");
        ctx.Db.AlarmEvents.Should().ContainSingle();
    }

    // ─── Acknowledge ────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_sets_ack_fields_and_transitions_to_Ack_state()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var created = await svc.CreateAsync(MakeEvent());
        var ack = await svc.AcknowledgeAsync(created.Id, userId: 42, userName: "Alice");

        ack.Should().NotBeNull();
        ack!.AcknowledgedAt.Should().NotBeNull();
        ack.AcknowledgedBy.Should().Be(42);
        ack.AcknowledgedByName.Should().Be("Alice");
        ack.State.Should().Be("Ack");
    }

    [Fact]
    public async Task AcknowledgeAsync_is_idempotent_does_not_overwrite_original_ack()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var created = await svc.CreateAsync(MakeEvent());
        var first = await svc.AcknowledgeAsync(created.Id, 42, "Alice");
        await Task.Delay(10);
        var second = await svc.AcknowledgeAsync(created.Id, 99, "Bob");

        // 두 번째 호출은 새 user 로 덮어쓰지 않고 기존 ack 정보 그대로 반환
        second.Should().NotBeNull();
        second!.AcknowledgedBy.Should().Be(42);
        second.AcknowledgedByName.Should().Be("Alice");
        second.AcknowledgedAt.Should().Be(first!.AcknowledgedAt);
    }

    [Fact]
    public async Task AcknowledgeAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        (await svc.AcknowledgeAsync(999, 1, "x")).Should().BeNull();
    }

    // ─── Resolve ────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_after_ack_keeps_original_ack_and_adds_resolution()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var created = await svc.CreateAsync(MakeEvent());
        await svc.AcknowledgeAsync(created.Id, 42, "Alice");
        var resolved = await svc.ResolveAsync(created.Id, 99, "Bob", "재조정 완료");

        resolved.Should().NotBeNull();
        resolved!.State.Should().Be("Resolved");
        resolved.AcknowledgedBy.Should().Be(42); // 기존 ack 그대로
        resolved.ResolvedBy.Should().Be(99);
        resolved.ResolvedByName.Should().Be("Bob");
        resolved.Resolution.Should().Be("재조정 완료");
    }

    [Fact]
    public async Task ResolveAsync_without_prior_ack_auto_fills_ack_fields()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var created = await svc.CreateAsync(MakeEvent());
        // 직접 Resolve — Ack 단계 건너뜀
        var resolved = await svc.ResolveAsync(created.Id, 99, "Bob", "즉시 처리");

        resolved!.State.Should().Be("Resolved");
        // skip-ack 흐름: Ack 정보도 같은 user 로 자동 채움
        resolved.AcknowledgedBy.Should().Be(99);
        resolved.AcknowledgedByName.Should().Be("Bob");
        resolved.AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        (await svc.ResolveAsync(999, 1, "x", "y")).Should().BeNull();
    }

    // ─── AcknowledgeAll ────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAllAsync_processes_only_new_alarms_and_returns_count()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var a1 = await svc.CreateAsync(MakeEvent("A1"));
        var a2 = await svc.CreateAsync(MakeEvent("A2"));
        var a3 = await svc.CreateAsync(MakeEvent("A3"));
        await svc.AcknowledgeAsync(a2.Id, 1, "u"); // 이미 ack
        await svc.ResolveAsync(a3.Id, 1, "u", "done"); // 이미 resolved

        var count = await svc.AcknowledgeAllAsync(userId: 50, userName: "Manager");

        count.Should().Be(1);
        var refreshed1 = await svc.GetByIdAsync(a1.Id);
        refreshed1!.State.Should().Be("Ack");
        refreshed1.AcknowledgedByName.Should().Be("Manager");
    }

    [Fact]
    public async Task AcknowledgeAllAsync_returns_zero_when_nothing_new()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        (await svc.AcknowledgeAllAsync(1, "x")).Should().Be(0);
    }

    // ─── Summary ───────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_counts_new_ack_critical_correctly()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var n1 = await svc.CreateAsync(MakeEvent("N1", severity: AlarmSeverity.Major));
        var n2 = await svc.CreateAsync(MakeEvent("N2", severity: AlarmSeverity.Critical));
        var ack1 = await svc.CreateAsync(MakeEvent("A1", severity: AlarmSeverity.Critical));
        await svc.AcknowledgeAsync(ack1.Id, 1, "u");
        var resolved = await svc.CreateAsync(MakeEvent("R1", severity: AlarmSeverity.Critical));
        await svc.ResolveAsync(resolved.Id, 1, "u", "done");

        var summary = await svc.GetSummaryAsync();

        summary.NewCount.Should().Be(2);          // n1, n2
        summary.AckCount.Should().Be(1);          // ack1
        // critical & not resolved = n2 (New Critical) + ack1 (Ack Critical) → 2
        summary.CriticalCount.Should().Be(2);
    }

    // ─── Filter ────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_state_filter_New_returns_only_new()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var n1 = await svc.CreateAsync(MakeEvent("N1"));
        var ack1 = await svc.CreateAsync(MakeEvent("A1"));
        await svc.AcknowledgeAsync(ack1.Id, 1, "u");

        var page = await svc.GetAsync(new AlarmFilterDto { State = "New" });
        page.Items.Should().ContainSingle().Which.Title.Should().Be("N1");
        page.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_state_filter_Ack_excludes_resolved()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var ack = await svc.CreateAsync(MakeEvent("A1"));
        await svc.AcknowledgeAsync(ack.Id, 1, "u");
        var resolved = await svc.CreateAsync(MakeEvent("R1"));
        await svc.ResolveAsync(resolved.Id, 1, "u", "done");

        var page = await svc.GetAsync(new AlarmFilterDto { State = "Ack" });
        page.Items.Should().ContainSingle().Which.Title.Should().Be("A1");
    }

    [Fact]
    public async Task GetAsync_filter_by_type_and_severity()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        await svc.CreateAsync(MakeEvent("NG-1", type: AlarmEventType.NG, severity: AlarmSeverity.Critical));
        await svc.CreateAsync(MakeEvent("NG-2", type: AlarmEventType.NG, severity: AlarmSeverity.Minor));
        await svc.CreateAsync(MakeEvent("OFF-1", type: AlarmEventType.Offline, severity: AlarmSeverity.Critical));

        var page = await svc.GetAsync(new AlarmFilterDto
        {
            AlarmType = AlarmEventType.NG,
            Severity = AlarmSeverity.Critical
        });
        page.Items.Should().ContainSingle().Which.Title.Should().Be("NG-1");
    }

    [Fact]
    public async Task GetAsync_pagination_orders_desc_and_returns_total()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);

        var baseTime = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            await svc.CreateAsync(MakeEvent($"A{i}", occurredAt: baseTime.AddMinutes(i)));
        }

        var page = await svc.GetAsync(new AlarmFilterDto { Page = 1, PageSize = 2 });
        page.TotalCount.Should().Be(5);
        page.Items.Should().HaveCount(2);
        // OccurredAt DESC — 최신 2 개
        page.Items.Select(a => a.Title).Should().ContainInOrder("A4", "A3");

        var page2 = await svc.GetAsync(new AlarmFilterDto { Page = 2, PageSize = 2 });
        page2.Items.Select(a => a.Title).Should().ContainInOrder("A2", "A1");
    }

    // ─── GetById ───────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_returns_null_when_missing()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = BuildService(ctx.Db);
        (await svc.GetByIdAsync(999)).Should().BeNull();
    }
}
