using System.Security.Claims;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/maintenance")
            .RequireAuthorization();

        group.MapGet("/schedules", async (bool? includeInactive, int? clientId, IMaintenanceService svc) =>
        {
            var items = await svc.GetSchedulesAsync(includeInactive == true, clientId);
            return Results.Ok(items);
        });

        group.MapGet("/schedules/due", async (int? withinDays, IMaintenanceService svc) =>
        {
            var items = await svc.GetDueSchedulesAsync(withinDays.GetValueOrDefault(7));
            return Results.Ok(items);
        });

        group.MapGet("/schedules/{id:int}", async (int id, IMaintenanceService svc) =>
        {
            var s = await svc.GetScheduleByIdAsync(id);
            return s is null ? Results.NotFound() : Results.Ok(s);
        });

        group.MapPost("/schedules", async (
            MaintenanceScheduleUpsertDto dto, IMaintenanceService svc, ILogger<Program> logger) =>
        {
            try
            {
                var s = await svc.CreateScheduleAsync(dto);
                return Results.Created($"/api/maintenance/schedules/{s.Id}", s);
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create schedule");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<MaintenanceScheduleUpsertDto>>();

        group.MapPut("/schedules/{id:int}", async (
            int id, MaintenanceScheduleUpsertDto dto, IMaintenanceService svc) =>
        {
            try
            {
                var s = await svc.UpdateScheduleAsync(id, dto);
                return s is null ? Results.NotFound() : Results.Ok(s);
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<MaintenanceScheduleUpsertDto>>();

        group.MapDelete("/schedules/{id:int}", async (int id, IMaintenanceService svc) =>
        {
            try
            {
                var ok = await svc.DeleteScheduleAsync(id);
                return ok ? Results.Ok() : Results.NotFound();
            }
            catch (InvalidOperationException ex) { return Results.Conflict(ex.Message); }
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // PM 수행
        group.MapPost("/schedules/{id:int}/perform", async (
            int id, PerformMaintenanceRequest req,
            IMaintenanceService svc, HttpContext ctx) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = ctx.User.FindFirstValue("DisplayName")
                ?? ctx.User.FindFirstValue(ClaimTypes.Name);
            int? userId = int.TryParse(userIdClaim, out var u) ? u : null;

            try
            {
                var rec = await svc.PerformAsync(id, req, userId, userName);
                return Results.Created($"/api/maintenance/records/{rec.Id}", rec);
            }
            catch (InvalidOperationException ex) { return Results.NotFound(ex.Message); }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<PerformMaintenanceRequest>>();

        group.MapGet("/records", async (
            int? scheduleId, int? clientId,
            DateTime? startDate, DateTime? endDate, int? limit,
            IMaintenanceService svc) =>
        {
            var rows = await svc.GetRecordsAsync(scheduleId, clientId, startDate, endDate,
                limit.GetValueOrDefault(200));
            return Results.Ok(rows);
        });

        // === 신뢰성 통계 ===
        var rel = app.MapGroup("/api/reliability").RequireAuthorization();
        rel.MapPost("/calculate", async (ReliabilityRequestDto req, IReliabilityService svc) =>
        {
            var stats = await svc.CalculateAsync(req);
            return Results.Ok(stats);
        }).AddEndpointFilter<ValidationEndpointFilter<ReliabilityRequestDto>>();
    }
}
