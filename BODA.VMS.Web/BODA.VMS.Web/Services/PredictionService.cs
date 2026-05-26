using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BODA.VMS.Web.Services;

/// <summary>
/// Predictive_DefectRate_Plan §6 Phase E — ONNX in-proc 회귀 추론.
///
/// 모델 라이프사이클:
///   • DB MLModels WHERE IsActive=true ORDER BY TrainedAt DESC LIMIT 1 = 활성 모델.
///   • 캐시 만료 후 다음 요청에서 active 모델 변경 여부 재확인 → 변경 시 InferenceSession 핫스왑.
///   • OnnxPath 없음/파일 없음/로드 실패 → 응답 Status="no_model" + Message.
///
/// 피처 벡터:
///   • v_defect_features VIEW 의 직전 25시간을 (ClientId, RecipeName) 별로 로드(DESC).
///   • Lag1h/4h/24h 는 **clock-based** 매칭 — 행 인덱스가 아니라 HourBucket - Nh 와 일치하는 행.
///     무가동 시간으로 갭이 있으면 해당 lag = NaN (LightGBM ONNX 가 missing 학습됨).
///   • FeatureSpecJson.feature_order 순서로 float32 배열 구성.
///
/// 캐싱:
///   • IMemoryCache, key="pred:{clientIndex}", TTL 60s (plan §6 명시).
///   • Singleton 이라 한 세션을 다중 요청이 공유 — InferenceSession.Run 은 thread-safe.
///
/// 멀티 모델 지원:
///   • Name 별 활성 모델은 1개라 가정. 본 서비스는 가장 최근 활성 모델만 사용.
///   • 더 정교한 라우팅(Recipe 별 다른 모델 등)은 Phase F 이후.
/// </summary>
public sealed class PredictionService : IPredictionService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PredictionService> _logger;
    private readonly SemaphoreSlim _modelSwapLock = new(1, 1);

    // Active model state (변경 시 _modelSwapLock 하에서만)
    private InferenceSession? _session;
    private int _activeModelId;
    private string? _activeModelName;
    private string? _activeModelVersion;
    private string? _activeOnnxPath;
    private string? _activeInputName;
    private List<string>? _featureOrder;
    private DateTime? _modelLoadedAt;
    private string? _lastError;

    private const string CacheKeyPrefix = "pred:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // 25h 윈도우면 24h lag 까지 안전하게 커버 (현재 시간 + 24개 이전).
    private const int LagWindowHours = 25;
    private static readonly int[] LagHours = { 1, 4, 24 };

    public PredictionService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<PredictionService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public PredictionServiceStatus GetStatus() => new()
    {
        ModelLoaded = _session is not null,
        ModelName = _activeModelName,
        ModelVersion = _activeModelVersion,
        OnnxPath = _activeOnnxPath,
        LoadedAt = _modelLoadedAt,
        LastError = _lastError,
        FeatureCount = _featureOrder?.Count ?? 0,
        // ResidualSampleCount / ResidualMae / RetrainRecommended 는 GetStatusWithResidualsAsync 에서.
    };

    // Phase F — 잔차 통계 캐시 (전체 클라이언트 평균, 30s TTL).
    // 활성 모델 변경 시 자동 무효화는 model id 키 변경으로.
    private const string ResidualStatusCacheKey = "pred:status:residuals";
    private static readonly TimeSpan ResidualStatusTtl = TimeSpan.FromSeconds(30);
    // 최근 N일 잔차로 재학습 권고 판단
    private const int ResidualLookbackDays = 7;
    // 잔차 MAE 가 학습 시점 MAE 의 (배수) 를 넘으면 재학습 권고
    private const double RetrainMaeFactor = 2.0;
    // 최소 샘플 수 미만이면 통계가 noisy → 재학습 권고 보류
    private const int RetrainMinSamples = 24;

    public async Task<PredictionServiceStatus> GetStatusWithResidualsAsync(CancellationToken ct = default)
    {
        var status = GetStatus();

        // 활성 모델 없으면 잔차 통계도 무의미
        if (_activeModelId == 0)
            return status;

        var key = ResidualStatusCacheKey + ":" + _activeModelId;
        if (_cache.TryGetValue<ResidualStats>(key, out var cached) && cached is not null)
        {
            status.ResidualSampleCount = cached.SampleCount;
            status.ResidualMae = cached.Mae;
            status.RetrainRecommended = cached.RetrainRecommended;
            return status;
        }

        var stats = await ComputeResidualStatsAsync(ct);
        _cache.Set(key, stats, ResidualStatusTtl);
        status.ResidualSampleCount = stats.SampleCount;
        status.ResidualMae = stats.Mae;
        status.RetrainRecommended = stats.RetrainRecommended;
        return status;
    }

    private async Task<ResidualStats> ComputeResidualStatsAsync(CancellationToken ct)
    {
        if (_activeModelId == 0)
            return new ResidualStats();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        var sinceUtc = DateTime.UtcNow.AddDays(-ResidualLookbackDays);

        // 활성 모델 + 백필 완료된 row 만
        var pairs = await db.PredictionLogs
            .AsNoTracking()
            .Where(p => p.MLModelId == _activeModelId
                     && p.ActualNgRate != null
                     && p.WindowStart >= sinceUtc)
            .Select(p => new { p.PredictedNgRate, p.ActualNgRate })
            .ToListAsync(ct);

        if (pairs.Count == 0)
            return new ResidualStats();

        double sumAbs = 0;
        foreach (var pair in pairs)
            sumAbs += Math.Abs(pair.PredictedNgRate - (pair.ActualNgRate ?? 0));
        var mae = sumAbs / pairs.Count;

        // 학습 시점 MAE — 활성 MLModel 의 Mae 컬럼. RetrainMaeFactor 배 초과 + 샘플 충분 시 권고.
        var trainedMae = await db.MLModels
            .AsNoTracking()
            .Where(m => m.Id == _activeModelId)
            .Select(m => (double?)m.Mae)
            .FirstOrDefaultAsync(ct) ?? 0;

        var recommend = pairs.Count >= RetrainMinSamples
                     && trainedMae > 0
                     && mae > trainedMae * RetrainMaeFactor;

        return new ResidualStats
        {
            SampleCount = pairs.Count,
            Mae = mae,
            RetrainRecommended = recommend,
        };
    }

    private sealed class ResidualStats
    {
        public int SampleCount { get; init; }
        public double? Mae { get; init; }
        public bool RetrainRecommended { get; init; }
    }

    public async Task<ResidualsResponse> GetResidualsAsync(
        int clientIndex, int hours = 168, CancellationToken ct = default)
    {
        if (hours < 1) hours = 168;
        if (hours > 720) hours = 720;  // 30일 상한 (차트 가독성 + 성능)

        var resp = new ResidualsResponse
        {
            ClientIndex = clientIndex,
            ModelName = _activeModelName,
            ModelVersion = _activeModelVersion,
        };

        if (_activeModelId == 0) return resp;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        var client = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientIndex == clientIndex, ct);
        if (client is null) return resp;

        // 라인의 최신 RecipeName (라벨용 — PredictionLogs 의 RecipeId 와 매칭은 ID 로)
        var latestRecipe = await db.InspectionHistories
            .AsNoTracking()
            .Where(h => h.ClientId == client.Id)
            .OrderByDescending(h => h.InspectedAt)
            .Select(h => h.RecipeName)
            .FirstOrDefaultAsync(ct);
        resp.RecipeName = latestRecipe;

        var sinceUtc = DateTime.UtcNow.AddHours(-hours);

        var rows = await db.PredictionLogs
            .AsNoTracking()
            .Where(p => p.MLModelId == _activeModelId
                     && p.ClientId == client.Id
                     && p.ActualNgRate != null
                     && p.WindowStart >= sinceUtc)
            .OrderBy(p => p.WindowStart)
            .Select(p => new { p.WindowStart, p.PredictedNgRate, ActualNgRate = p.ActualNgRate!.Value })
            .ToListAsync(ct);

        if (rows.Count == 0) return resp;

        var residuals = new double[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var residual = r.PredictedNgRate - r.ActualNgRate;
            residuals[i] = residual;
            resp.Points.Add(new ResidualPoint
            {
                WindowStart = DateTime.SpecifyKind(r.WindowStart, DateTimeKind.Utc),
                Predicted = r.PredictedNgRate,
                Actual = r.ActualNgRate,
                Residual = residual,
            });
        }

        // SPC ±3σ
        var mean = residuals.Average();
        double sumSq = 0;
        foreach (var d in residuals) sumSq += (d - mean) * (d - mean);
        var std = rows.Count > 1 ? Math.Sqrt(sumSq / (rows.Count - 1)) : 0;
        resp.ResidualMean = mean;
        resp.ResidualStdDev = std;
        resp.Ucl = mean + 3 * std;
        resp.Lcl = mean - 3 * std;
        return resp;
    }

    public async Task<PredictionCurrentResponse> GetCurrentAsync(int clientIndex, CancellationToken ct = default)
    {
        var key = CacheKeyPrefix + clientIndex;
        if (_cache.TryGetValue<PredictionCurrentResponse>(key, out var cached) && cached is not null)
            return cached;

        var resp = await ComputeAsync(clientIndex, ct);
        _cache.Set(key, resp, CacheTtl);

        // Phase F 잔차 SPC 준비 — cache miss + ok 일 때만 영속화.
        // 동일 (Model, Client, WindowStart) 중복은 사전 체크로 회피.
        if (resp.Status == "ok" && resp.WindowStart.HasValue && _activeModelId != 0)
        {
            _ = Task.Run(() => PersistPredictionLogAsync(resp, ct), ct);
        }
        return resp;
    }

    public async Task<PredictionSnapshotResponse> GetSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = new PredictionSnapshotResponse
        {
            ServerUtc = DateTime.UtcNow,
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        var clientIndexes = await db.Clients
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.ClientIndex)
            .Select(c => c.ClientIndex)
            .ToListAsync(ct);

        // 병렬 호출 — 각 GetCurrentAsync 는 캐시 hit 면 1us, miss 라도 독립적
        var tasks = clientIndexes.Select(idx => GetCurrentAsync(idx, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        snapshot.Items.AddRange(results);
        return snapshot;
    }

    public async Task<RecentActualsResponse> GetRecentActualsAsync(
        int clientIndex, int hours = 24, CancellationToken ct = default)
    {
        if (hours < 1) hours = 24;
        if (hours > 168) hours = 168;  // 1주 상한 — 차트 가독성/성능

        var resp = new RecentActualsResponse { ClientIndex = clientIndex };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        var client = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientIndex == clientIndex, ct);
        if (client is null) return resp;

        var latestRecipe = await db.InspectionHistories
            .AsNoTracking()
            .Where(h => h.ClientId == client.Id)
            .OrderByDescending(h => h.InspectedAt)
            .Select(h => h.RecipeName)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(latestRecipe)) return resp;
        resp.RecipeName = latestRecipe;

        var sinceUtc = DateTime.UtcNow.AddHours(-hours);

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT HourBucket, NgRate, InspectionCount
            FROM v_defect_features
            WHERE ClientId = $cid AND RecipeName = $rn AND HourBucket >= $since
            ORDER BY HourBucket ASC";
        AddParam(cmd, "$cid", client.Id);
        AddParam(cmd, "$rn", latestRecipe);
        AddParam(cmd, "$since", sinceUtc.ToString("yyyy-MM-ddTHH:00:00", CultureInfo.InvariantCulture));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var hbStr = reader.GetString(0);
            DateTime.TryParseExact(hbStr, "yyyy-MM-ddTHH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var hb);
            resp.Points.Add(new ActualHourPoint
            {
                HourBucket = hb,
                NgRate = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
                InspectionCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            });
        }
        return resp;
    }

    private async Task PersistPredictionLogAsync(PredictionCurrentResponse resp, CancellationToken ct)
    {
        if (!resp.WindowStart.HasValue || resp.PredictedNgRate is null || _activeModelId == 0)
            return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

            var client = await db.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClientIndex == resp.ClientIndex, ct);
            if (client is null) return;

            // RecipeId 매핑 — RecipeName 으로 조회
            var recipeId = await db.Recipes
                .AsNoTracking()
                .Where(r => r.Name == resp.RecipeName)
                .Select(r => (int?)r.Id)
                .FirstOrDefaultAsync(ct) ?? 0;

            var windowStartUtc = DateTime.SpecifyKind(resp.WindowStart.Value, DateTimeKind.Utc);

            // 동일 (MLModelId, ClientId, RecipeId, WindowStart) 중복 방지
            var exists = await db.PredictionLogs
                .AsNoTracking()
                .AnyAsync(p =>
                    p.MLModelId == _activeModelId &&
                    p.ClientId == client.Id &&
                    p.RecipeId == recipeId &&
                    p.WindowStart == windowStartUtc, ct);
            if (exists) return;

            db.PredictionLogs.Add(new Data.Entities.PredictionLog
            {
                MLModelId = _activeModelId,
                ClientId = client.Id,
                RecipeId = recipeId,
                WindowStart = windowStartUtc,
                PredictedNgRate = resp.PredictedNgRate.Value,
                ActualNgRate = null,  // Phase F backfill 작업이 채움
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // 영속화 실패는 추론 자체를 막아선 안 됨 — 경고만 로깅.
            _logger.LogWarning(ex, "[Prediction] PredictionLog persist failed (client={ClientIndex})",
                resp.ClientIndex);
        }
    }

    private async Task<PredictionCurrentResponse> ComputeAsync(int clientIndex, CancellationToken ct)
    {
        var resp = new PredictionCurrentResponse
        {
            ClientIndex = clientIndex,
            ServerUtc = DateTime.UtcNow,
            Status = "no_model",
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();

        if (!await EnsureModelLoadedAsync(db, ct))
        {
            resp.Message = _lastError ?? "No active MLModel registered.";
            return resp;
        }

        resp.ModelName = _activeModelName;
        resp.ModelVersion = _activeModelVersion;

        // ClientIndex → ClientId
        var client = await db.Clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientIndex == clientIndex, ct);
        if (client is null)
        {
            resp.Status = "no_data";
            resp.Message = $"Client with index {clientIndex} not found.";
            return resp;
        }

        // 최신 RecipeName — 가장 최근 InspectionHistory 의 레시피
        var latestRecipe = await db.InspectionHistories
            .AsNoTracking()
            .Where(h => h.ClientId == client.Id)
            .OrderByDescending(h => h.InspectedAt)
            .Select(h => h.RecipeName)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(latestRecipe))
        {
            resp.Status = "no_data";
            resp.Message = "No inspection history for this client yet.";
            return resp;
        }
        resp.RecipeName = latestRecipe;

        // v_defect_features 직전 25h 로드
        var sinceUtc = DateTime.UtcNow.AddHours(-LagWindowHours - 1);
        var rows = await FetchFeatureRowsAsync(db, client.Id, latestRecipe, sinceUtc, ct);
        if (rows.Count == 0)
        {
            resp.Status = "no_data";
            resp.Message = "No aggregated feature rows yet — VIEW empty for this client/recipe.";
            return resp;
        }

        var current = rows[0]; // 최신 (DESC)
        // WindowStart 의미 통일 (Plan §4.2): "예측이 대상으로 하는 윈도우의 시작" = 입력 시점 + horizon.
        // 베이스라인은 horizon=1h. PredictionLog.WindowStart 와 동일 의미여야 backfill 매칭이 단순 동등이 됨.
        resp.WindowStart = current.HourBucket.AddHours(1);
        resp.InspectionCountThisHour = current.InspectionCount;

        // clock-based lag matching
        var lagRows = LagHours.ToDictionary(
            h => h,
            h => rows.FirstOrDefault(r => r.HourBucket == current.HourBucket.AddHours(-h)));

        var vector = BuildFeatureVector(current, lagRows, _featureOrder!);

        try
        {
            var raw = RunInference(vector);
            resp.PredictedNgRate = Math.Clamp(raw, 0.0, 1.0);
            resp.Status = "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Prediction] Inference failed for client {ClientIndex}", clientIndex);
            resp.Status = "error";
            resp.Message = $"Inference failed: {ex.Message}";
        }

        return resp;
    }

    private async Task<bool> EnsureModelLoadedAsync(BodaVmsDbContext db, CancellationToken ct)
    {
        var active = await db.MLModels
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderByDescending(m => m.TrainedAt)
            .FirstOrDefaultAsync(ct);

        if (active is null)
        {
            await UnloadIfAnyAsync();
            _lastError = "No active MLModel row (IsActive=true) found.";
            return false;
        }

        // 이미 같은 모델 → 재사용
        if (_session is not null && _activeModelId == active.Id)
            return true;

        await _modelSwapLock.WaitAsync(ct);
        try
        {
            // double-check after lock
            if (_session is not null && _activeModelId == active.Id)
                return true;

            _session?.Dispose();
            _session = null;

            if (!File.Exists(active.OnnxPath))
            {
                _lastError = $"ONNX file not found: {active.OnnxPath}";
                _activeModelId = 0;
                return false;
            }

            try
            {
                // SessionOptions: Microsoft.AspNetCore.Builder.SessionOptions 와 동명이라 완전 경로 명시.
                var sessionOptions = new Microsoft.ML.OnnxRuntime.SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                };
                _session = new InferenceSession(active.OnnxPath, sessionOptions);
                _activeInputName = _session.InputNames.FirstOrDefault() ?? "input";

                if (!TryParseFeatureOrder(active.FeatureSpecJson, out var order, out var parseErr))
                {
                    _lastError = parseErr;
                    _session.Dispose();
                    _session = null;
                    return false;
                }
                _featureOrder = order;

                _activeModelId = active.Id;
                _activeModelName = active.Name;
                _activeModelVersion = active.Version;
                _activeOnnxPath = active.OnnxPath;
                _modelLoadedAt = DateTime.UtcNow;
                _lastError = null;

                _logger.LogInformation(
                    "[Prediction] Loaded model {Name} v{Version} from {Path} ({Features} features, input={Input})",
                    active.Name, active.Version, active.OnnxPath, _featureOrder.Count, _activeInputName);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = $"InferenceSession load failed: {ex.Message}";
                _logger.LogError(ex, "[Prediction] Model load failed for {Path}", active.OnnxPath);
                _session?.Dispose();
                _session = null;
                _activeModelId = 0;
                return false;
            }
        }
        finally
        {
            _modelSwapLock.Release();
        }
    }

    private static bool TryParseFeatureOrder(string featureSpecJson, out List<string> order, out string? error)
    {
        order = new List<string>();
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(featureSpecJson);
            if (!doc.RootElement.TryGetProperty("feature_order", out var fo) || fo.ValueKind != JsonValueKind.Array)
            {
                error = "FeatureSpecJson missing 'feature_order' array.";
                return false;
            }
            foreach (var el in fo.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s)) order.Add(s);
            }
            if (order.Count == 0)
            {
                error = "FeatureSpecJson 'feature_order' is empty.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"FeatureSpecJson parse failed: {ex.Message}";
            return false;
        }
    }

    private Task UnloadIfAnyAsync()
    {
        if (_session is null) return Task.CompletedTask;
        _session.Dispose();
        _session = null;
        _activeModelId = 0;
        _activeModelName = null;
        _activeModelVersion = null;
        _activeOnnxPath = null;
        _activeInputName = null;
        _featureOrder = null;
        _modelLoadedAt = null;
        return Task.CompletedTask;
    }

    private static float[] BuildFeatureVector(
        DefectFeatureRow current,
        Dictionary<int, DefectFeatureRow?> lagRows,
        List<string> featureOrder)
    {
        var v = new float[featureOrder.Count];
        for (int i = 0; i < featureOrder.Count; i++)
        {
            v[i] = ResolveFeatureValue(featureOrder[i], current, lagRows);
        }
        return v;
    }

    private static float ResolveFeatureValue(
        string featureName,
        DefectFeatureRow current,
        Dictionary<int, DefectFeatureRow?> lagRows)
    {
        foreach (var lag in LagHours)
        {
            var prefix = $"Lag{lag}h_";
            if (featureName.StartsWith(prefix, StringComparison.Ordinal))
            {
                var col = featureName[prefix.Length..];
                var row = lagRows.TryGetValue(lag, out var r) ? r : null;
                return row is null ? float.NaN : ToFloat(row.GetColumn(col));
            }
        }
        return ToFloat(current.GetColumn(featureName));
    }

    private static float ToFloat(object? value)
    {
        if (value is null) return float.NaN;
        try { return (float)Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return float.NaN; }
    }

    private double RunInference(float[] features)
    {
        if (_session is null || _activeInputName is null)
            throw new InvalidOperationException("Session not loaded.");

        var tensor = new DenseTensor<float>(features, new[] { 1, features.Length });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(_activeInputName, tensor)
        };
        using var outputs = _session.Run(inputs);

        // 첫 출력의 첫 원소 = 회귀 예측값. LightGBM ONNX 회귀의 표준 형태.
        var first = outputs.First();
        return first.Value switch
        {
            Tensor<float> tf  => tf.First(),
            Tensor<double> td => td.First(),
            _ => throw new InvalidOperationException(
                $"Unexpected ONNX output type: {first.Value?.GetType().Name}")
        };
    }

    private async Task<List<DefectFeatureRow>> FetchFeatureRowsAsync(
        BodaVmsDbContext db, int clientId, string recipeName, DateTime sinceUtc, CancellationToken ct)
    {
        var rows = new List<DefectFeatureRow>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);

        using var cmd = conn.CreateCommand();
        // 컬럼 순서는 reader 에서 이름으로 매핑하므로 자유. LIMIT 는 25h 윈도우 보호.
        cmd.CommandText = @"
            SELECT *
            FROM v_defect_features
            WHERE ClientId = $cid AND RecipeName = $rn AND HourBucket >= $since
            ORDER BY HourBucket DESC
            LIMIT 50";
        AddParam(cmd, "$cid", clientId);
        AddParam(cmd, "$rn", recipeName);
        // VIEW 의 HourBucket 은 'yyyy-MM-ddTHH:00:00' TEXT — 같은 포맷으로 비교 필수
        AddParam(cmd, "$since", sinceUtc.ToString("yyyy-MM-ddTHH:00:00", CultureInfo.InvariantCulture));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(DefectFeatureRow.From(reader));
        }
        return rows;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _modelSwapLock.Dispose();
    }
}

/// <summary>
/// v_defect_features 한 행의 약타입 컨테이너. VIEW 컬럼이 추가돼도 reader 에서 자동 흡수.
/// </summary>
internal sealed class DefectFeatureRow
{
    public int ClientId { get; init; }
    public string RecipeName { get; init; } = "";
    public DateTime HourBucket { get; init; }
    public int InspectionCount { get; init; }

    private readonly Dictionary<string, object?> _columns = new(StringComparer.OrdinalIgnoreCase);

    public object? GetColumn(string name) =>
        _columns.TryGetValue(name, out var v) ? v : null;

    public static DefectFeatureRow From(DbDataReader r)
    {
        var cols = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++)
            cols[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);

        var hourBucketStr = cols.GetValueOrDefault("HourBucket")?.ToString() ?? "";
        DateTime.TryParseExact(
            hourBucketStr,
            "yyyy-MM-ddTHH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var hourBucket);

        var row = new DefectFeatureRow
        {
            ClientId = Convert.ToInt32(cols.GetValueOrDefault("ClientId") ?? 0),
            RecipeName = cols.GetValueOrDefault("RecipeName")?.ToString() ?? "",
            HourBucket = hourBucket,
            InspectionCount = Convert.ToInt32(cols.GetValueOrDefault("InspectionCount") ?? 0),
        };
        foreach (var kv in cols)
            row._columns[kv.Key] = kv.Value;
        return row;
    }
}
