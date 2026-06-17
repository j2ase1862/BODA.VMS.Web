using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class GlassEndpoints
{
    public static void MapGlassEndpoints(this WebApplication app)
    {
        // 스마트 글라스(핸즈프리) 호출 — 키오스크 선례와 동일 인증 모델.
        // Anonymous + X-API-Key 호환 필터: 기본 호환 모드(Required=false)에서는 헤더 없이 통과,
        // enforcement 전환 시 머신/키오스크와 일관되게 키 요구 (설계문서 §6, GS baseline §5.1/§11.3).
        var group = app.MapGroup("/api/glass")
            .AllowAnonymous()
            .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        // GET /api/glass/inbound-location?barcode=...&mode=입고
        group.MapGet("/inbound-location", async (
            string barcode, string? mode, IWarehouseService svc) =>
        {
            var loc = await svc.GetInboundLocationAsync(barcode, mode);
            return loc is null
                ? Results.NotFound(new { message = "등록되지 않은 바코드" })
                : Results.Ok(loc);
        });

        // GET /api/glass/pick-list?orderNo=...  — 출고 주문 기반 피킹 목록(읽기 가이드)
        group.MapGet("/pick-list", async (string orderNo, IOutboundService svc) =>
        {
            var pick = await svc.GetPickListAsync(orderNo);
            return pick is null
                ? Results.NotFound(new { message = "등록되지 않은 주문번호" })
                : Results.Ok(pick);
        });

        // POST /api/glass/pick-confirm  — 스캔 검증 피킹 확정(2차)
        group.MapPost("/pick-confirm", async (PickConfirmRequest req, IOutboundService svc) =>
        {
            var result = await svc.ConfirmPickAsync(req.OrderNo, req.Barcode, req.Qty);
            return result is null
                ? Results.NotFound(new { message = "등록되지 않은 주문번호" })
                : Results.Ok(result);
        });

        // POST /api/glass/ship-confirm  — 출하 확정(2차)
        group.MapPost("/ship-confirm", async (PickConfirmRequest req, IOutboundService svc) =>
        {
            var pick = await svc.ConfirmShipAsync(req.OrderNo);
            return pick is null
                ? Results.NotFound(new { message = "등록되지 않은 주문번호" })
                : Results.Ok(pick);
        });
    }
}
