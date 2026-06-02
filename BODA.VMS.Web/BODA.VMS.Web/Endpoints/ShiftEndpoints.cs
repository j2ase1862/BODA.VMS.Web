using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class ShiftEndpoints
{
    public static void MapShiftEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/shifts")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeInactive, IShiftService svc) =>
        {
            var items = await svc.GetAllAsync(includeInactive == true);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, IShiftService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (ShiftDto dto, IShiftService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/shifts/{result.Id}", result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"Shift '{dto.Name}'가 이미 존재합니다.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create shift");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(p => p.RequireRole("Admin"))
          .AddEndpointFilter<ValidationEndpointFilter<ShiftDto>>();

        group.MapPut("/{id:int}", async (int id, ShiftDto dto, IShiftService svc) =>
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
        }).RequireAuthorization(p => p.RequireRole("Admin"))
          .AddEndpointFilter<ValidationEndpointFilter<ShiftDto>>();

        group.MapDelete("/{id:int}", async (int id, IShiftService svc) =>
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

        group.MapPost("/report", async (ShiftReportRequestDto req, IShiftService svc) =>
        {
            var rows = await svc.GetReportAsync(req);
            return Results.Ok(rows);
        }).AddEndpointFilter<ValidationEndpointFilter<ShiftReportRequestDto>>();
    }
}
