using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace BODA.VMS.Web.Endpoints;

public static class LotEndpoints
{
    public static void MapLotEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/lots")
            .RequireAuthorization();

        group.MapGet("/{id:int}", async (int id, ILotService svc) =>
        {
            var lot = await svc.GetByIdAsync(id);
            return lot is null ? Results.NotFound() : Results.Ok(lot);
        });

        group.MapGet("/by-number/{lotNumber}", async (string lotNumber, ILotService svc) =>
        {
            var lot = await svc.GetByLotNumberAsync(lotNumber);
            return lot is null ? Results.NotFound() : Results.Ok(lot);
        }).AllowAnonymous() // VMS가 LotNumber로 조회 가능
          .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        // B1: VMS 가 WO 선택 시 활성 Lot 자동 채우기 위해 호출. 없으면 null.
        // (멀티 Lot 콤보는 아래 open-by-workorder 목록 사용 — 하위 호환용 유지)
        group.MapGet("/active-by-workorder/{workOrderId:int}", async (int workOrderId, ILotService svc) =>
        {
            var lot = await svc.GetActiveByWorkOrderAsync(workOrderId);
            return lot is null ? Results.Ok((LotDto?)null) : Results.Ok(lot);
        }).AllowAnonymous()
          .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        // 멀티 Lot 병행 운용 — VMS 콤보가 WO 의 Open Lot 전체 목록을 조회.
        group.MapGet("/open-by-workorder/{workOrderId:int}", async (int workOrderId, ILotService svc) =>
        {
            var lots = await svc.GetOpenByWorkOrderAsync(workOrderId);
            return Results.Ok(lots);
        }).AllowAnonymous()
          .AddEndpointFilter<ClientApiKeyEndpointFilter>();

        group.MapPost("/{id:int}/close", async (
            int id,
            ILotService svc,
            IHubContext<VmsPublicHub> hub,
            ILogger<Program> logger) =>
        {
            var result = await svc.CloseAsync(id);
            if (result is null) return Results.NotFound();

            // VMS 에 즉시 푸시 — 선택 중이던 Lot 이 마감되면 콤보에서 제거/재선택.
            // 실패해도 요청은 성공 (60초 폴링이 안전망 — RecipeParametersChanged 패턴).
            try
            {
                await hub.Clients.All.SendAsync("LotClosed", new
                {
                    workOrderId = result.WorkOrderId,
                    lotId = result.Id,
                    lotNumber = result.LotNumber
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[VmsPublicHub] LotClosed broadcast failed");
            }

            return Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));
    }
}
