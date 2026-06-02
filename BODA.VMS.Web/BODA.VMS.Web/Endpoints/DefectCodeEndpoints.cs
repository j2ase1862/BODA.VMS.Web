using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class DefectCodeEndpoints
{
    public static void MapDefectCodeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/defect-codes")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeInactive, IDefectCodeService svc) =>
        {
            var items = await svc.GetAllAsync(includeInactive == true);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, IDefectCodeService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (DefectCodeDto dto, IDefectCodeService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/defect-codes/{result.Id}", result);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"코드 '{dto.Code}'가 이미 존재합니다.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create defect code");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<DefectCodeDto>>();

        group.MapPut("/{id:int}", async (int id, DefectCodeDto dto, IDefectCodeService svc) =>
        {
            var r = await svc.UpdateAsync(id, dto);
            return r is null ? Results.NotFound() : Results.Ok(r);
        }).RequireAuthorization(p => p.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<DefectCodeDto>>();

        group.MapDelete("/{id:int}", async (int id, IDefectCodeService svc) =>
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        // === 파레토 분석 ===
        group.MapGet("/pareto", async (
            DateTime? startDate, DateTime? endDate,
            int? clientId, int? workOrderId, int? topN,
            IDefectCodeService svc) =>
        {
            var result = await svc.GetParetoAsync(
                startDate, endDate, clientId, workOrderId,
                topN.GetValueOrDefault(20));
            return Results.Ok(result);
        });

        // === 자동완성 후보 (RecipeParameter + 실제 발생 NgCode 통합) ===
        group.MapGet("/candidates", async (int? recentDays, IDefectCodeService svc) =>
        {
            var items = await svc.GetCandidatesAsync(recentDays.GetValueOrDefault(90));
            return Results.Ok(items);
        });

        // === 미등록 NG 코드 발견 ===
        group.MapGet("/unregistered", async (int? days, IDefectCodeService svc) =>
        {
            var items = await svc.GetUnregisteredAsync(days.GetValueOrDefault(30));
            return Results.Ok(items);
        });
    }
}
