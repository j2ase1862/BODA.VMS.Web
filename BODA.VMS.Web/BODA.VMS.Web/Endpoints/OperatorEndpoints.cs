using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class OperatorEndpoints
{
    public static void MapOperatorEndpoints(this WebApplication app)
    {
        // === 키오스크 (Anonymous — 현장 단말에서 직접 호출) ===
        var kiosk = app.MapGroup("/api/kiosk").AllowAnonymous();

        // 현재 작업자 조회 (라인 인덱스 기준)
        kiosk.MapGet("/current/{clientIndex:int}", async (
            int clientIndex,
            IOperatorSessionService svc) =>
        {
            var s = await svc.GetCurrentByClientIndexAsync(clientIndex);
            return s is null ? Results.NoContent() : Results.Ok(s);
        });

        // 로그인 (사번 + PIN)
        kiosk.MapPost("/login", async (
            KioskLoginRequest req,
            IOperatorService opSvc,
            IOperatorSessionService sessionSvc,
            BodaVmsDbContext db) =>
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientIndex == req.ClientIndex);
            if (client is null) return Results.NotFound($"Line {req.ClientIndex} not found");

            var op = await opSvc.AuthenticateAsync(req.EmployeeNumber, req.Pin);
            if (op is null) return Results.Unauthorized();

            var session = await sessionSvc.StartSessionAsync(client.Id, op.Id);
            return Results.Ok(session);
        }).AddEndpointFilter<ValidationEndpointFilter<KioskLoginRequest>>();

        // 로그아웃 (현재 작업자 세션 종료)
        kiosk.MapPost("/logout", async (
            KioskLogoutRequest req,
            IOperatorSessionService svc,
            BodaVmsDbContext db) =>
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientIndex == req.ClientIndex);
            if (client is null) return Results.NotFound();
            var ended = await svc.EndSessionAsync(client.Id, SessionEndReason.Logout);
            return ended is null ? Results.NoContent() : Results.Ok(ended);
        });

        // 라인 메타 (키오스크 페이지에서 라인 이름 표시용)
        kiosk.MapGet("/line/{clientIndex:int}", async (int clientIndex, BodaVmsDbContext db) =>
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientIndex == clientIndex);
            if (client is null) return Results.NotFound();
            return Results.Ok(new
            {
                client.Id,
                client.ClientIndex,
                client.Name
            });
        });

        // === Operator 마스터 (Admin/User) ===
        var operators = app.MapGroup("/api/operators")
            .RequireAuthorization();

        operators.MapGet("/", async (bool? includeInactive, IOperatorService svc) =>
        {
            var items = await svc.GetAllAsync(includeInactive == true);
            return Results.Ok(items);
        });

        operators.MapGet("/{id:int}", async (int id, IOperatorService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        operators.MapPost("/", async (OperatorUpsertDto dto, IOperatorService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/operators/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"사번 '{dto.EmployeeNumber}'가 이미 존재합니다.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create operator");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"));

        operators.MapPut("/{id:int}", async (int id, OperatorUpsertDto dto, IOperatorService svc) =>
        {
            try
            {
                var r = await svc.UpdateAsync(id, dto);
                return r is null ? Results.NotFound() : Results.Ok(r);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"));

        operators.MapDelete("/{id:int}", async (int id, IOperatorService svc) =>
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
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // === Operator 세션 이력 (Admin/User) ===
        operators.MapGet("/sessions", async (
            int? clientId, int? operatorId,
            DateTime? startDate, DateTime? endDate, int? limit,
            IOperatorSessionService svc) =>
        {
            var rows = await svc.GetHistoryAsync(
                clientId, operatorId, startDate, endDate,
                limit.GetValueOrDefault(200));
            return Results.Ok(rows);
        });
    }
}
