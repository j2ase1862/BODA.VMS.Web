using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products")
            .RequireAuthorization();

        group.MapGet("/", async (bool? includeInactive, IProductService svc) =>
        {
            var items = await svc.GetAllAsync(includeInactive == true);
            return Results.Ok(items);
        });

        group.MapGet("/{id:int}", async (int id, IProductService svc) =>
        {
            var item = await svc.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapGet("/by-code/{code}", async (string code, IProductService svc) =>
        {
            var item = await svc.GetByCodeAsync(code);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (ProductDto dto, IProductService svc, ILogger<Program> logger) =>
        {
            try
            {
                var result = await svc.CreateAsync(dto);
                return Results.Created($"/api/products/{result.Id}", result);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"제품 코드 '{dto.Code}'가 이미 존재합니다.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create product {Code}", dto.Code);
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapPut("/{id:int}", async (int id, ProductDto dto, IProductService svc) =>
        {
            var result = await svc.UpdateAsync(id, dto);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        group.MapDelete("/{id:int}", async (int id, IProductService svc) =>
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
    }
}
