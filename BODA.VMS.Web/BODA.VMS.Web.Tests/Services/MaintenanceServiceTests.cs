using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// MaintenanceService — SEMI E10 (RAM) 기반 예방보전(PM) 일정/수행 검증.
/// PerformAsync 가 NextDueAt 을 IntervalDays 만큼 자동 진행시키는 핵심 로직
/// (가동률/Availability 계산의 입력) 정확성 보장.
/// </summary>
public class MaintenanceServiceTests
{
    private static MaintenanceScheduleUpsertDto MakeUpsert(
        string name = "Lens 청소",
        int intervalDays = 7,
        int? clientId = null,
        DateTime? nextDueAt = null,
        bool isActive = true)
        => new()
        {
            Name = name,
            IntervalDays = intervalDays,
            ClientId = clientId,
            NextDueAt = nextDueAt,
            IsActive = isActive,
            EstimatedDurationMinutes = 30
        };

    // ─── CreateSchedule ─────────────────────────────────────

    [Fact]
    public async Task CreateScheduleAsync_defaults_nextdue_to_now_plus_interval()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var before = DateTime.UtcNow;
        var created = await svc.CreateScheduleAsync(MakeUpsert(intervalDays: 30));

        created.Name.Should().Be("Lens 청소");
        created.IntervalDays.Should().Be(30);
        created.NextDueAt.Should().BeOnOrAfter(before.AddDays(30).AddSeconds(-2));
        created.NextDueAt.Should().BeOnOrBefore(DateTime.UtcNow.AddDays(30).AddSeconds(2));
    }

    [Fact]
    public async Task CreateScheduleAsync_uses_explicit_nextdueat_when_provided()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var explicitDue = new DateTime(2026, 12, 31, 9, 0, 0, DateTimeKind.Utc);
        var created = await svc.CreateScheduleAsync(MakeUpsert(nextDueAt: explicitDue));

        created.NextDueAt.Should().Be(explicitDue);
    }

    [Fact]
    public async Task CreateScheduleAsync_trims_name()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var created = await svc.CreateScheduleAsync(MakeUpsert(name: "  PM-1  "));
        created.Name.Should().Be("PM-1");
    }

    [Fact]
    public async Task CreateScheduleAsync_throws_when_interval_zero_or_negative()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var act0 = () => svc.CreateScheduleAsync(MakeUpsert(intervalDays: 0));
        var actNeg = () => svc.CreateScheduleAsync(MakeUpsert(intervalDays: -1));

        await act0.Should().ThrowAsync<ArgumentException>();
        await actNeg.Should().ThrowAsync<ArgumentException>();
    }

    // ─── GetSchedules / Filter ──────────────────────────────

    [Fact]
    public async Task GetSchedulesAsync_default_excludes_inactive()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        await svc.CreateScheduleAsync(MakeUpsert(name: "Active"));
        await svc.CreateScheduleAsync(MakeUpsert(name: "Inactive", isActive: false));

        (await svc.GetSchedulesAsync()).Should().ContainSingle()
            .Which.Name.Should().Be("Active");
        (await svc.GetSchedulesAsync(includeInactive: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSchedulesAsync_orders_by_nextdue_ascending()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        await svc.CreateScheduleAsync(MakeUpsert(name: "Later", nextDueAt: DateTime.UtcNow.AddDays(30)));
        await svc.CreateScheduleAsync(MakeUpsert(name: "Soon", nextDueAt: DateTime.UtcNow.AddDays(1)));
        await svc.CreateScheduleAsync(MakeUpsert(name: "Mid", nextDueAt: DateTime.UtcNow.AddDays(7)));

        var list = await svc.GetSchedulesAsync();
        list.Select(s => s.Name).Should().ContainInOrder("Soon", "Mid", "Later");
    }

    [Fact]
    public async Task GetSchedulesAsync_clientid_filter_includes_global_schedules()
    {
        using var ctx = new InMemorySqliteDbContext();
        // VisionClient ValueGeneratedNever — Id 명시
        ctx.Db.Clients.AddRange(
            new VisionClient { Id = 5, Name = "Line5", IpAddress = "10.0.0.5", ClientIndex = 5, IsActive = true, CreatedAt = DateTime.UtcNow },
            new VisionClient { Id = 6, Name = "Line6", IpAddress = "10.0.0.6", ClientIndex = 6, IsActive = true, CreatedAt = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var svc = new MaintenanceService(ctx.Db);
        await svc.CreateScheduleAsync(MakeUpsert(name: "Line5-PM", clientId: 5));
        await svc.CreateScheduleAsync(MakeUpsert(name: "All-Lines-PM", clientId: null));
        await svc.CreateScheduleAsync(MakeUpsert(name: "Line6-PM", clientId: 6));

        // ClientId=5 필터: Line5-PM 과 NULL(공통) 모두 포함, Line6-PM 은 제외 —
        // 운영에서 라인 작업자가 자기 라인 + 공통을 함께 봄
        var list = await svc.GetSchedulesAsync(clientId: 5);
        list.Select(s => s.Name).Should().BeEquivalentTo(new[] { "Line5-PM", "All-Lines-PM" });
    }

    // ─── Update ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateScheduleAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        (await svc.UpdateScheduleAsync(999, MakeUpsert())).Should().BeNull();
    }

    [Fact]
    public async Task UpdateScheduleAsync_throws_when_interval_invalid()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        var created = await svc.CreateScheduleAsync(MakeUpsert());

        var act = () => svc.UpdateScheduleAsync(created.Id, MakeUpsert(intervalDays: 0));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task UpdateScheduleAsync_preserves_nextdue_when_dto_nextdue_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var originalDue = DateTime.UtcNow.AddDays(45);
        var created = await svc.CreateScheduleAsync(MakeUpsert(nextDueAt: originalDue));

        var dto = MakeUpsert(name: "Renamed");
        dto.NextDueAt = null;
        var updated = await svc.UpdateScheduleAsync(created.Id, dto);

        updated!.Name.Should().Be("Renamed");
        updated.NextDueAt.Should().BeCloseTo(originalDue, TimeSpan.FromSeconds(1));
    }

    // ─── Delete ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteScheduleAsync_returns_false_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        (await svc.DeleteScheduleAsync(999)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteScheduleAsync_blocks_when_records_exist()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var schedule = await svc.CreateScheduleAsync(MakeUpsert());
        await svc.PerformAsync(schedule.Id, new PerformMaintenanceRequest(), userId: 1, userName: "tester");

        // 추적성: 수행 기록이 있는 일정은 삭제 금지 (감사 추적 보존)
        var act = () => svc.DeleteScheduleAsync(schedule.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*비활성화*");
    }

    [Fact]
    public async Task DeleteScheduleAsync_succeeds_when_no_records()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var schedule = await svc.CreateScheduleAsync(MakeUpsert());
        (await svc.DeleteScheduleAsync(schedule.Id)).Should().BeTrue();
        ctx.Db.MaintenanceSchedules.Should().BeEmpty();
    }

    // ─── Perform ────────────────────────────────────────────

    [Fact]
    public async Task PerformAsync_advances_nextdue_by_interval_and_creates_record()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        var schedule = await svc.CreateScheduleAsync(MakeUpsert(intervalDays: 7));

        var record = await svc.PerformAsync(schedule.Id,
            new PerformMaintenanceRequest { ActualDurationMinutes = 25, Notes = "OK" },
            userId: 42, userName: "Operator A");

        record.Should().NotBeNull();
        record.PerformedByName.Should().Be("Operator A");
        record.ActualDurationMinutes.Should().Be(25);
        record.Notes.Should().Be("OK");

        // 일정 본체의 NextDueAt 이 PerformedAt + IntervalDays 로 진행돼야 함
        record.NewDueAt.Should().BeCloseTo(record.PerformedAt.AddDays(7), TimeSpan.FromSeconds(2));

        var refreshed = await svc.GetScheduleByIdAsync(schedule.Id);
        refreshed!.NextDueAt.Should().BeCloseTo(record.NewDueAt, TimeSpan.FromSeconds(1));
        refreshed.LastPerformedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformAsync_throws_when_schedule_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);

        var act = () => svc.PerformAsync(999, new PerformMaintenanceRequest(), null, null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999*");
    }

    // ─── GetDueSchedules ────────────────────────────────────

    [Fact]
    public async Task GetDueSchedulesAsync_returns_only_active_within_window()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new MaintenanceService(ctx.Db);
        await svc.CreateScheduleAsync(MakeUpsert(name: "DueToday", nextDueAt: DateTime.UtcNow));
        await svc.CreateScheduleAsync(MakeUpsert(name: "DueIn5",  nextDueAt: DateTime.UtcNow.AddDays(5)));
        await svc.CreateScheduleAsync(MakeUpsert(name: "DueIn30", nextDueAt: DateTime.UtcNow.AddDays(30)));
        await svc.CreateScheduleAsync(MakeUpsert(name: "Inactive", nextDueAt: DateTime.UtcNow, isActive: false));

        var due = await svc.GetDueSchedulesAsync(withinDays: 7);
        due.Select(s => s.Name).Should().BeEquivalentTo(new[] { "DueToday", "DueIn5" });
    }
}
