using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

/// <summary>
/// 출고 오더 관리(등록/수정/삭제) API. 글라스 피킹 조회(/api/glass/pick-list)와 달리
/// 로그인 인증을 요구한다 (관리 화면 전용).
/// </summary>
public static class OutboundEndpoints
{
    public static void MapOutboundEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/outbound-orders")
            .RequireAuthorization();

        group.MapGet("/", async (IOutboundService svc) =>
            Results.Ok(await svc.GetAllAsync()));

        group.MapGet("/{id:int}", async (int id, IOutboundService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (OutboundOrderDto dto, IOutboundService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/outbound-orders/{result.Id}", result);
            }
            catch (DuplicateOrderNoException ex)
            {
                return Results.Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create outbound order");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<OutboundOrderDto>>();

        group.MapPut("/{id:int}", async (int id, OutboundOrderDto dto, IOutboundService svc) =>
        {
            try
            {
                var r = await svc.UpdateAsync(id, dto);
                return r is null ? Results.NotFound() : Results.Ok(r);
            }
            catch (DuplicateOrderNoException ex)
            {
                return Results.Conflict(ex.Message);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<OutboundOrderDto>>();

        group.MapDelete("/{id:int}", async (int id, IOutboundService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
