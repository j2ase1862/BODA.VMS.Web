using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class WorkOrderEndpoints
{
    public static void MapWorkOrderEndpoints(this WebApplication app)
    {
        // 익명 — VMS Client 가 자기 ClientIndex 기준으로 작업지시 목록 조회 (Stage 2)
        // 인증된 /api/workorders 그룹과 별개. 옵션 status (예: "InProgress","Planned").
        app.MapGet("/api/workorders/by-client/{clientIndex:int}", async (
            int clientIndex,
            string? status,
            BodaVmsDbContext db,
            IWorkOrderService svc) =>
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientIndex == clientIndex);
            if (client is null)
                return Results.Ok(new List<WorkOrderDto>());
            var items = await svc.GetAllAsync(status, client.Id);
            return Results.Ok(items);
        }).AllowAnonymous();

        var group = app.MapGroup("/api/workorders")
            .RequireAuthorization();

        group.MapGet("/", async (string? status, int? clientId, IWorkOrderService svc) =>
        {
            var items = await svc.GetAllAsync(status, clientId);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, IWorkOrderService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapGet("/next-order-no", async (IWorkOrderService svc) =>
        {
            var orderNo = await svc.GenerateNextOrderNoAsync();
            return Results.Ok(new { orderNo });
        });

        group.MapPost("/", async (WorkOrderDto dto, IWorkOrderService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/workorders/{result.Id}", result);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"OrderNo '{dto.OrderNo}'가 이미 존재합니다.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create work order");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapPut("/{id:int}", async (int id, WorkOrderDto dto, IWorkOrderService svc) =>
        {
            try
            {
                var result = await svc.UpdateAsync(id, dto);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapPost("/{id:int}/status", async (
            int id,
            WorkOrderStatusChangeRequest req,
            IWorkOrderService svc) =>
        {
            try
            {
                var result = await svc.ChangeStatusAsync(id, req.Action);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapDelete("/{id:int}", async (int id, IWorkOrderService svc) =>
        {
            try
            {
                var ok = await svc.DeleteAsync(id);
                return ok ? Results.Ok() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));

        // === Lot subresource ===

        group.MapGet("/{id:int}/lots", async (int id, ILotService svc) =>
        {
            var lots = await svc.GetByWorkOrderAsync(id);
            return Results.Ok(lots);
        });

        group.MapPost("/{id:int}/lots", async (int id, CreateLotRequest req, ILotService svc) =>
        {
            try
            {
                var lot = await svc.CreateAsync(id, req.Note);
                return Results.Created($"/api/lots/{lot.Id}", lot);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(ex.Message);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));
    }
}
