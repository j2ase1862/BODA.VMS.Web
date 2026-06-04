using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

/// <summary>
/// Predictive_DefectRate_Plan §6 Phase E — 예측 조회 엔드포인트.
/// /current 는 anonymous (VMS 가 인증 없이 폴링), /status 는 운영 진단용으로 인증 필요.
/// </summary>
public static class PredictionEndpoints
{
    public static void MapPredictionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/predictions/current/{clientIndex:int}", async (
            int clientIndex,
            IPredictionService svc,
            CancellationToken ct) =>
        {
            var resp = await svc.GetCurrentAsync(clientIndex, ct);
            return Results.Ok(resp);
        }).AllowAnonymous()
          .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        // Phase F: 기본 status 는 동기 (잔차 통계 제외) — UI 가 가벼운 헬스체크에 사용.
        app.MapGet("/api/predictions/status", (IPredictionService svc) =>
        {
            return Results.Ok(svc.GetStatus());
        }).RequireAuthorization();

        // Phase F: 잔차 통계 + 재학습 권고 시그널 포함 status — /forecast 페이지 헤더 배너용.
        app.MapGet("/api/predictions/status/full", async (
            IPredictionService svc,
            CancellationToken ct) =>
        {
            var status = await svc.GetStatusWithResidualsAsync(ct);
            return Results.Ok(status);
        }).RequireAuthorization();

        // Phase F: 잔차 SPC 차트 데이터 — 활성 모델의 직전 N시간 (기본 168 = 1주)
        app.MapGet("/api/predictions/residuals/{clientIndex:int}", async (
            int clientIndex,
            int? hours,
            IPredictionService svc,
            CancellationToken ct) =>
        {
            var resid = await svc.GetResidualsAsync(clientIndex, hours ?? 168, ct);
            return Results.Ok(resid);
        }).RequireAuthorization();

        // Blazor /forecast 카드 그리드 — 모든 active 클라이언트 예측 한 번에
        app.MapGet("/api/predictions/snapshot", async (
            IPredictionService svc,
            CancellationToken ct) =>
        {
            var snap = await svc.GetSnapshotAsync(ct);
            return Results.Ok(snap);
        }).RequireAuthorization();

        // /forecast 시계열 차트 — 직전 N시간 실측 NG율
        app.MapGet("/api/predictions/actuals/{clientIndex:int}", async (
            int clientIndex,
            int? hours,
            IPredictionService svc,
            CancellationToken ct) =>
        {
            var actuals = await svc.GetRecentActualsAsync(clientIndex, hours ?? 24, ct);
            return Results.Ok(actuals);
        }).RequireAuthorization();
    }
}
