using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

/// <summary>
/// 입고 위치 마스터 관리(등록/수정/삭제) API. 글라스 조회(/api/glass)와 달리
/// 로그인 인증을 요구한다 (관리 화면 전용).
/// </summary>
public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/warehouse-items")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeInactive, IWarehouseService svc) =>
        {
            var items = await svc.GetAllAsync(includeInactive == true);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, IWarehouseService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (WarehouseItemDto dto, IWarehouseService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/warehouse-items/{result.Id}", result);
            }
            catch (DuplicateBarcodeException ex)
            {
                return Results.Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create warehouse item");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<WarehouseItemDto>>();

        group.MapPut("/{id:int}", async (int id, WarehouseItemDto dto, IWarehouseService svc) =>
        {
            try
            {
                var r = await svc.UpdateAsync(id, dto);
                return r is null ? Results.NotFound() : Results.Ok(r);
            }
            catch (DuplicateBarcodeException ex)
            {
                return Results.Conflict(ex.Message);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<WarehouseItemDto>>();

        group.MapDelete("/{id:int}", async (int id, IWarehouseService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }
}
