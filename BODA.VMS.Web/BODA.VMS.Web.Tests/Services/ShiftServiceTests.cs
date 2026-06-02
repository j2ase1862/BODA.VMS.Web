using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

public class ShiftServiceTests
{
    [Fact]
    public async Task CreateAsync_trims_name_and_persists()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = new ShiftService(ctx.Db);

        var created = await svc.CreateAsync(new ShiftDto
        {
            Name = "  주간  ", StartHour = 8, EndHour = 17, IsActive = true
        });

        created.Name.Should().Be("주간");
        created.Id.Should().BeGreaterThan(0);
        ctx.Db.Shifts.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllAsync_default_excludes_inactive()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Shifts.AddRange(
            new Shift { Name = "Day", StartHour = 8, EndHour = 17, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Shift { Name = "OldNight", StartHour = 22, EndHour = 6, IsActive = false, CreatedAt = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var svc = new ShiftService(ctx.Db);
        (await svc.GetAllAsync()).Should().HaveCount(1);
        (await svc.GetAllAsync(includeInactive: true)).Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_throws_when_referenced_by_history()
    {
        using var ctx = new InMemorySqliteDbContext();

        // FK 부모 entity 생성 — InspectionHistory 가 Shift 를 참조해야 삭제 차단 로직 동작
        var client = new VisionClient
        {
            ClientIndex = 0, Name = "L0", IpAddress = "127.0.0.1", IsActive = true, CreatedAt = DateTime.UtcNow
        };
        ctx.Db.Clients.Add(client);
        var shift = new Shift { Name = "S", StartHour = 0, EndHour = 8, IsActive = true, CreatedAt = DateTime.UtcNow };
        ctx.Db.Shifts.Add(shift);
        await ctx.Db.SaveChangesAsync();

        ctx.Db.InspectionHistories.Add(new InspectionHistory
        {
            ClientId = client.Id,
            ShiftId = shift.Id,
            InspectedAt = DateTime.UtcNow,
            IsPass = true
        });
        await ctx.Db.SaveChangesAsync();

        var svc = new ShiftService(ctx.Db);
        // 보안/데이터 무결성: 참조 중인 Shift 삭제 차단 — 비활성화하라는 비즈니스 규칙
        var act = () => svc.DeleteAsync(shift.Id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*비활성화*");
    }

    [Fact]
    public async Task ResolveShiftIdAsync_matches_correct_shift()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Shifts.AddRange(
            new Shift { Name = "Day",   StartHour = 8,  EndHour = 17, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Shift { Name = "Night", StartHour = 22, EndHour = 6,  IsActive = true, CreatedAt = DateTime.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var svc = new ShiftService(ctx.Db);
        // Shift.Contains 는 ToLocalTime().Hour 를 사용 — DateTimeKind 명시 필수
        var localMorning = DateTime.SpecifyKind(new DateTime(2026, 6, 2, 10, 0, 0), DateTimeKind.Local);
        var dayId = await svc.ResolveShiftIdAsync(localMorning);
        dayId.Should().NotBeNull();
        ctx.Db.Shifts.Find(dayId)!.Name.Should().Be("Day");
    }
}
