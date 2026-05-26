using System.Data;
using System.Data.Common;
using System.Globalization;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

/// <summary>
/// Predictive_DefectRate_Plan §6 Phase F — PredictionLog.ActualNgRate 자동 백필.
///
/// 동작:
///   • 5분 주기로 ActualNgRate IS NULL 인 row 들 중 WindowStart 가 1시간 이상 지난 것을 조회.
///   • 같은 (ClientId, RecipeName, HourBucket=WindowStart) 의 v_defect_features.NgRate 로 UPDATE.
///   • 7일 이상 지난 row 는 영영 실측을 못 받았다고 가정 — backfill 시도하지 않음(영구 NULL).
///     (현장 무가동/레시피 변경 등으로 그 윈도우의 집계가 영영 비는 경우.)
///
/// 운영 안정:
///   • 시작 시 1분 지연 — Web 부팅 + 스키마 마이그레이션 완료 대기.
///   • 한 tick 당 최대 500 row 처리 — 너무 많은 row 가 한 번에 백필되는 것 방지(예외 시 다음 tick 재시도).
///   • DB 쓰기 실패는 다음 tick 에 재시도 — 운영 DB 잠금/network 일시 장애 무해.
///
/// 잔차 SPC / 재학습 트리거의 기초 데이터 — 본 서비스가 채우는 ActualNgRate 가 누적되어야
/// PredictionService.GetStatus() 가 RetrainRecommended 를 판단할 수 있음.
/// </summary>
public sealed class PredictionLogBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredictionLogBackfillService> _logger;

    private static readonly TimeSpan StartDelay   = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    // WindowStart + ReadyDelay 가 지난 row 만 백필 — 그 시점의 v_defect_features 가 안정적으로 집계됨.
    private static readonly TimeSpan ReadyDelay   = TimeSpan.FromMinutes(60);
    // 7일 이상 지난 row 는 영영 NULL — backfill 시도 안 함.
    private static readonly TimeSpan StaleAfter   = TimeSpan.FromDays(7);
    private const int BatchSize = 500;

    public PredictionLogBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<PredictionLogBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updated = await BackfillOnceAsync(stoppingToken);
                if (updated > 0)
                    _logger.LogInformation("[Backfill] Updated {Count} PredictionLogs with actual NG rate", updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Backfill] tick failed — will retry next interval");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task<int> BackfillOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        var now = DateTime.UtcNow;
        var readyBefore = now - ReadyDelay;   // WindowStart 가 이 시각 이전이면 backfill 시도
        var staleBefore = now - StaleAfter;   // WindowStart 가 이 시각 이전이면 포기

        // RecipeId=0 (PersistPredictionLogAsync 의 폴백) 은 v_defect_features 와 join 불가 — skip
        var rows = await db.PredictionLogs
            .Where(p => p.ActualNgRate == null
                     && p.RecipeId > 0
                     && p.WindowStart <= readyBefore
                     && p.WindowStart >= staleBefore)
            .OrderBy(p => p.WindowStart)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (rows.Count == 0) return 0;

        // RecipeId → RecipeName 일괄 조회
        var recipeIds = rows.Select(r => r.RecipeId).Distinct().ToList();
        var recipeNames = await db.Recipes
            .Where(r => recipeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        int updated = 0;
        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            if (!recipeNames.TryGetValue(row.RecipeId, out var recipeName) || string.IsNullOrEmpty(recipeName))
                continue;

            var actual = await LookupActualAsync(conn, row.ClientId, recipeName, row.WindowStart, ct);
            if (actual.HasValue)
            {
                row.ActualNgRate = actual.Value;
                updated++;
            }
        }

        if (updated > 0)
            await db.SaveChangesAsync(ct);
        return updated;
    }

    private static async Task<double?> LookupActualAsync(
        DbConnection conn, int clientId, string recipeName, DateTime windowStartUtc, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT NgRate FROM v_defect_features
            WHERE ClientId = $cid AND RecipeName = $rn AND HourBucket = $hb
            LIMIT 1";
        AddParam(cmd, "$cid", clientId);
        AddParam(cmd, "$rn", recipeName);
        AddParam(cmd, "$hb",
            windowStartUtc.ToString("yyyy-MM-ddTHH:00:00", CultureInfo.InvariantCulture));

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result == DBNull.Value) return null;
        try { return Convert.ToDouble(result, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
