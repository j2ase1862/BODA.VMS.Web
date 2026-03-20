using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class InspectionItemEndpoints
{
    public static void MapInspectionItemEndpoints(this WebApplication app)
    {
        // === VMS 동기화 (Anonymous) ===

        // VMS가 자기 클라이언트의 레시피 목록 조회
        app.MapGet("/api/parameters/sync/recipes/{clientIndex:int}", async (
            int clientIndex,
            BodaVmsDbContext db) =>
        {
            var client = await db.Clients
                .FirstOrDefaultAsync(c => c.ClientIndex == clientIndex);

            if (client is null)
                return Results.NotFound();

            var recipes = await db.Recipes
                .Where(r => r.ClientId == client.Id)
                .Select(r => new { r.Id, r.Name, r.Description })
                .ToListAsync();

            return Results.Ok(recipes);
        }).AllowAnonymous();

        // VMS가 특정 레시피의 파라미터 다운로드 → Dictionary<int, double> 캐시용
        app.MapGet("/api/parameters/sync/recipe/{recipeId:int}", async (
            int recipeId,
            IRecipeParameterService service) =>
        {
            var items = await service.GetByRecipeAsync(recipeId);
            return Results.Ok(items.Where(i => i.IsActive));
        }).AllowAnonymous();

        // VMS가 검사 결과 업로드
        app.MapPost("/api/parameters/results", async (
            ParameterResultUploadRequest request,
            BodaVmsDbContext db,
            ILogger<Program> logger) =>
        {
            var client = await db.Clients
                .FirstOrDefaultAsync(c => c.ClientIndex == request.ClientIndex);

            if (client is null)
                return Results.NotFound($"Client with index {request.ClientIndex} not found");

            var recipe = await db.Recipes.FindAsync(request.RecipeId);

            var toolResults = request.Results.Select(r => new ToolResultItem
            {
                ToolName = r.ParamCode.ToString(),
                ToolType = "Parameter",
                Value = r.MeasuredValue,
                Min = 0,
                Max = 0,
                IsPass = r.Judgment == "OK"
            }).ToList();

            var isPass = request.Results.All(r => r.Judgment == "OK");
            var ngCodes = request.Results
                .Where(r => r.Judgment == "NG")
                .Select(r => r.ParamCode.ToString());

            var history = new Data.Entities.InspectionHistory
            {
                ClientId = client.Id,
                RecipeName = recipe?.Name ?? $"Recipe#{request.RecipeId}",
                IsPass = isPass,
                NgCode = ngCodes.Any() ? string.Join(",", ngCodes) : null,
                ToolResults = JsonSerializer.Serialize(toolResults),
                InspectedAt = request.Results.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow
            };

            db.InspectionHistories.Add(history);
            await db.SaveChangesAsync();

            return Results.Ok(new { historyId = history.Id, isPass });
        }).AllowAnonymous();

        // === 관리 엔드포인트 (인증 필요) ===
        var group = app.MapGroup("/api/parameters")
            .RequireAuthorization();

        // 프리셋 그룹 목록
        group.MapGet("/presets", () =>
        {
            return Results.Ok(ParameterPresetGroup.PresetGroups);
        });

        // 다음 사용 가능한 코드 번호 조회
        group.MapGet("/next-code/{recipeId:int}", async (
            int recipeId,
            IRecipeParameterService service) =>
        {
            var nextCode = await service.GetNextAvailableCodeAsync(recipeId);
            return Results.Ok(new { nextCode });
        });

        // 레시피별 파라미터 조회
        group.MapGet("/recipe/{recipeId:int}", async (
            int recipeId,
            IRecipeParameterService service) =>
        {
            var items = await service.GetByRecipeAsync(recipeId);
            return Results.Ok(items);
        });

        // 단건 조회
        group.MapGet("/{id:int}", async (int id, IRecipeParameterService service) =>
        {
            var item = await service.GetByIdAsync(id);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        // 단건 생성
        group.MapPost("/", async (
            RecipeParameterDto dto,
            IRecipeParameterService service,
            ILogger<Program> logger) =>
        {
            try
            {
                var result = await service.CreateAsync(dto);
                return Results.Created($"/api/parameters/{result.Id}", result);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
            {
                return Results.Conflict($"ParamCode {dto.ParamCode} already exists for this recipe.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create parameter {Code}", dto.ParamCode);
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        // 배치 생성
        group.MapPost("/batch", async (
            List<RecipeParameterDto> dtos,
            IRecipeParameterService service,
            ILogger<Program> logger) =>
        {
            try
            {
                var results = await service.CreateBatchAsync(dtos);
                return Results.Created("/api/parameters", results);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create parameter batch");
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        // 수정
        group.MapPut("/{id:int}", async (
            int id,
            RecipeParameterDto dto,
            IRecipeParameterService service) =>
        {
            var result = await service.UpdateAsync(id, dto);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));

        // 삭제
        group.MapDelete("/{id:int}", async (int id, IRecipeParameterService service) =>
        {
            var success = await service.DeleteAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }
}
