using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// EquipmentStatusService — SEMI E10 상태 시계열(Running/Idle/Down) 검증.
/// 같은 상태 무시(불필요한 row 폭발 방지) + 이전 open 행 EndedAt 닫기 +
/// OEE/Availability 누적 시간 집계 기반 시간 윈도우 조회 정확성.
/// </summary>
public class EquipmentStatusServiceTests
{
    private const int ClientId = 1;

    private static async Task SeedClientAsync(BodaVmsDbContext db, int id = ClientId, int index = 0)
    {
        db.Clients.Add(new VisionClient
        {
            Id = id, Name = $"L{index}", IpAddress = $"127.0.0.{index + 1}",
            ClientIndex = index, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ─── RecordTransition ──────────────────────────────────

    [Fact]
    public async Task RecordTransitionAsync_first_record_creates_open_row()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);

        var rows = await ctx.Db.EquipmentStatusLogs.ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Status.Should().Be(EquipmentStatus.Running);
        rows[0].EndedAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordTransitionAsync_same_status_noop_keeps_single_row()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);

        // 동일 상태 폭발 방지 — heartbeat 가 매 초 동일 상태 호출해도 row 1 개
        ctx.Db.EquipmentStatusLogs.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordTransitionAsync_status_change_closes_previous_and_opens_new()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Down, "케이블 단선");

        var rows = await ctx.Db.EquipmentStatusLogs.OrderBy(e => e.StartedAt).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].Status.Should().Be(EquipmentStatus.Running);
        rows[0].EndedAt.Should().NotBeNull();
        rows[1].Status.Should().Be(EquipmentStatus.Down);
        rows[1].EndedAt.Should().BeNull();
        rows[1].Reason.Should().Be("케이블 단선");
    }

    [Fact]
    public async Task RecordTransitionAsync_multiple_transitions_chain_correctly()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Idle);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Down);
        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);

        var rows = await ctx.Db.EquipmentStatusLogs.OrderBy(e => e.StartedAt).ToListAsync();
        rows.Should().HaveCount(4);
        rows.Take(3).Should().AllSatisfy(r => r.EndedAt.Should().NotBeNull());
        rows.Last().EndedAt.Should().BeNull();
        rows.Select(r => r.Status).Should().ContainInOrder(
            EquipmentStatus.Idle,
            EquipmentStatus.Running,
            EquipmentStatus.Down,
            EquipmentStatus.Running);
    }

    [Fact]
    public async Task RecordTransitionAsync_isolates_per_client()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db, id: 1, index: 0);
        await SeedClientAsync(ctx.Db, id: 2, index: 1);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(1, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(2, EquipmentStatus.Down);
        await svc.RecordTransitionAsync(1, EquipmentStatus.Idle);

        var line1 = await ctx.Db.EquipmentStatusLogs.Where(e => e.ClientId == 1).ToListAsync();
        var line2 = await ctx.Db.EquipmentStatusLogs.Where(e => e.ClientId == 2).ToListAsync();
        line1.Should().HaveCount(2);
        line2.Should().HaveCount(1);
        line2[0].EndedAt.Should().BeNull();
    }

    // ─── GetCurrent ────────────────────────────────────────

    [Fact]
    public async Task GetCurrentAsync_returns_open_row_with_duration()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        // 과거 시작점 명시 → DurationSeconds > 0 보장
        ctx.Db.EquipmentStatusLogs.Add(new EquipmentStatusLog
        {
            ClientId = ClientId,
            Status = EquipmentStatus.Running,
            StartedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await ctx.Db.SaveChangesAsync();

        var current = await svc.GetCurrentAsync(ClientId);
        current.Should().NotBeNull();
        current!.Status.Should().Be(EquipmentStatus.Running);
        current.EndedAt.Should().BeNull();
        current.DurationSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetCurrentAsync_returns_null_when_no_logs()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        (await svc.GetCurrentAsync(ClientId)).Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_returns_null_when_all_closed()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(ClientId, EquipmentStatus.Running);
        // 모든 row 강제로 EndedAt 부여
        foreach (var r in ctx.Db.EquipmentStatusLogs)
            r.EndedAt = DateTime.UtcNow;
        await ctx.Db.SaveChangesAsync();

        (await svc.GetCurrentAsync(ClientId)).Should().BeNull();
    }

    // ─── GetLogs (시간 윈도우) ────────────────────────────

    [Fact]
    public async Task GetLogsAsync_returns_overlapping_logs_in_ascending_order()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db);

        // 3 개 row: 윈도우 [10:00, 11:00] 와 겹치는 케이스 매트릭스
        var t = new DateTime(2026, 6, 4, 9, 0, 0, DateTimeKind.Utc);
        ctx.Db.EquipmentStatusLogs.AddRange(
            // 윈도우 이전에 끝남 — 제외
            new EquipmentStatusLog { ClientId = ClientId, Status = "Idle", StartedAt = t, EndedAt = t.AddMinutes(30) },
            // 윈도우 시작 직전 시작 → 윈도우 진입 — 포함
            new EquipmentStatusLog { ClientId = ClientId, Status = "Running", StartedAt = t.AddMinutes(45), EndedAt = t.AddMinutes(75) },
            // 윈도우 중간 — 포함
            new EquipmentStatusLog { ClientId = ClientId, Status = "Down", StartedAt = t.AddMinutes(75), EndedAt = t.AddMinutes(90) },
            // 윈도우 종료 후 시작 — 제외
            new EquipmentStatusLog { ClientId = ClientId, Status = "Idle", StartedAt = t.AddMinutes(150) }
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new EquipmentStatusService(ctx.Db);
        var window = await svc.GetLogsAsync(ClientId,
            start: t.AddMinutes(60),  // 10:00
            end:   t.AddMinutes(120)); // 11:00

        window.Should().HaveCount(2);
        window.Select(l => l.Status).Should().ContainInOrder("Running", "Down");
    }

    [Fact]
    public async Task GetLogsAsync_filters_by_client()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db, id: 1, index: 0);
        await SeedClientAsync(ctx.Db, id: 2, index: 1);
        var svc = new EquipmentStatusService(ctx.Db);

        await svc.RecordTransitionAsync(1, EquipmentStatus.Running);
        await svc.RecordTransitionAsync(2, EquipmentStatus.Down);

        var t0 = DateTime.UtcNow.AddMinutes(-1);
        var t1 = DateTime.UtcNow.AddMinutes(1);
        (await svc.GetLogsAsync(1, t0, t1)).Should().ContainSingle()
            .Which.Status.Should().Be(EquipmentStatus.Running);
        (await svc.GetLogsAsync(2, t0, t1)).Should().ContainSingle()
            .Which.Status.Should().Be(EquipmentStatus.Down);
    }
}
