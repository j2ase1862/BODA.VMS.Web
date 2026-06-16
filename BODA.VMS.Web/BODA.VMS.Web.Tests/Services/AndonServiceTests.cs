using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// AndonService.GetSnapshotAsync — 라인별 현재 상태/KPI 조립 검증.
/// 핵심 회귀: 한 ClientId 에 open(EndedAt=null) EquipmentStatusLog 가 2개 이상 남아도
/// (과거 버전/크로스 프로세스 race 흔적) ToDictionary "same key" 예외로 죽지 않고
/// 가장 최근 행을 현재 상태로 골라야 한다.
/// </summary>
public class AndonServiceTests
{
    /// <summary>GetSnapshotAsync 는 _prediction.GetSnapshotAsync() 만 호출하고 예외를 삼킨다.</summary>
    private sealed class ThrowingPredictionService : IPredictionService
    {
        public Task<PredictionSnapshotResponse> GetSnapshotAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("predictive infra down — 안돈은 폴백해야 함");

        public Task<PredictionCurrentResponse> GetCurrentAsync(int clientIndex, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<RecentActualsResponse> GetRecentActualsAsync(int clientIndex, int hours = 24, CancellationToken ct = default)
            => throw new NotImplementedException();
        public PredictionServiceStatus GetStatus() => throw new NotImplementedException();
        public Task<PredictionServiceStatus> GetStatusWithResidualsAsync(CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ResidualsResponse> GetResidualsAsync(int clientIndex, int hours = 168, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static async Task SeedClientAsync(BodaVmsDbContext db, int id, int index)
    {
        db.Clients.Add(new VisionClient
        {
            Id = id, Name = $"Line{index}", IpAddress = $"127.0.0.{index + 1}",
            ClientIndex = index, IsActive = true, CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSnapshotAsync_with_duplicate_open_status_rows_does_not_throw_and_picks_latest()
    {
        using var ctx = new InMemorySqliteDbContext();
        await SeedClientAsync(ctx.Db, id: 2, index: 2);

        var t = DateTime.UtcNow.AddHours(-2);
        // ClientId=2 에 EndedAt=null 인 open 행이 2개 — 데이터 무결성 위반 상황 재현.
        ctx.Db.EquipmentStatusLogs.AddRange(
            new EquipmentStatusLog { ClientId = 2, Status = EquipmentStatus.Idle, StartedAt = t },
            new EquipmentStatusLog { ClientId = 2, Status = EquipmentStatus.Running, StartedAt = t.AddHours(1) }
        );
        await ctx.Db.SaveChangesAsync();

        var svc = new AndonService(ctx.Db, new ThrowingPredictionService());

        var act = async () => await svc.GetSnapshotAsync();

        var snap = (await act.Should().NotThrowAsync()).Subject;
        var line = snap.Lines.Should().ContainSingle().Subject;
        // 가장 최근(StartedAt 큰) open 행 = Running 이 현재 상태여야 한다.
        line.Status.Should().Be(EquipmentStatus.Running);
        snap.PredictiveInsights.Should().BeEmpty(); // 예측 인프라 장애 → 폴백
    }
}
