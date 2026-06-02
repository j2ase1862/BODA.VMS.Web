using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.AspNetCore.SignalR;
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
            IAlarmService alarmSvc,
            IShiftService shiftSvc,
            IOperatorSessionService operatorSessionSvc,
            IHubContext<VmsPublicHub> hub,
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

            var inspectedAt = request.Results.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow;
            var shiftId = await shiftSvc.ResolveShiftIdAsync(inspectedAt);

            // OperatorId 자동 매칭: 명시적 전달 우선, 없으면 라인의 현재 키오스크 세션에서 조회
            var operatorId = request.OperatorId
                ?? await operatorSessionSvc.ResolveCurrentOperatorIdAsync(client.Id);

            var history = new Data.Entities.InspectionHistory
            {
                ClientId = client.Id,
                RecipeName = recipe?.Name ?? $"Recipe#{request.RecipeId}",
                IsPass = isPass,
                NgCode = ngCodes.Any() ? string.Join(",", ngCodes) : null,
                ToolResults = JsonSerializer.Serialize(toolResults),
                InspectedAt = inspectedAt,
                WorkOrderId = request.WorkOrderId,
                LotId = request.LotId,
                OperatorId = operatorId,
                SerialNumber = request.SerialNumber,
                ShiftId = shiftId,
                // Predictive_DefectRate_Plan §5.1 — V1/V2/V3 예측 피처
                // (구버전 VMS 가 보내지 않으면 자연스럽게 NULL)
                CycleTimeMs = request.CycleTimeMs,
                Brightness = request.Brightness,
                ContrastStd = request.ContrastStd,
                FocusScore = request.FocusScore,
                BlobCount = request.BlobCount,
                MaxBlobAreaPx = request.MaxBlobAreaPx,
                DlConfidence = request.DlConfidence,
                DlModelVersion = request.DlModelVersion
            };

            db.InspectionHistories.Add(history);
            await db.SaveChangesAsync(); // history.Id 확보 (다음 측정값 저장에 필요)

            // === ParameterMeasurement 정규화 저장 (Phase 2 SPC) ===
            var measurements = request.Results.Select(r => new Data.Entities.ParameterMeasurement
            {
                HistoryId = history.Id,
                RecipeId = request.RecipeId,
                ParamCode = r.ParamCode,
                MeasuredValue = r.MeasuredValue,
                Judgment = r.Judgment,
                InspectedAt = history.InspectedAt,
                ClientId = client.Id,
                WorkOrderId = request.WorkOrderId,
                LotId = request.LotId
            }).ToList();
            db.ParameterMeasurements.AddRange(measurements);

            // === 추적성 카운터 자동 갱신 (Phase 1) ===
            if (request.LotId.HasValue)
            {
                var lot = await db.Lots.FindAsync(request.LotId.Value);
                if (lot is not null && lot.Status == Data.Entities.LotStatus.Open)
                {
                    lot.Quantity++;
                    if (isPass) lot.PassCount++; else lot.NgCount++;
                }
            }

            // Stage 3: WO 진행률 응답을 위한 snapshot 보관
            Data.Entities.WorkOrder? touchedWo = null;
            bool woJustCompleted = false;

            if (request.WorkOrderId.HasValue)
            {
                var wo = await db.WorkOrders.FindAsync(request.WorkOrderId.Value);
                if (wo is not null)
                {
                    // Planned 상태였다면 자동 InProgress 전이 (첫 검사 결과 도착 = 시작 시점)
                    if (wo.Status == Data.Entities.WorkOrderStatus.Planned)
                    {
                        wo.Status = Data.Entities.WorkOrderStatus.InProgress;
                        wo.ActualStartAt ??= DateTime.UtcNow;
                    }

                    if (wo.Status == Data.Entities.WorkOrderStatus.InProgress)
                    {
                        wo.ProducedQuantity++;
                        if (isPass) wo.PassQuantity++; else wo.NgQuantity++;
                        wo.UpdatedAt = DateTime.UtcNow;

                        // Stage 3: 계획 수량 도달 → 자동 Completed 전이
                        if (wo.PlannedQuantity > 0 && wo.ProducedQuantity >= wo.PlannedQuantity)
                        {
                            wo.Status = Data.Entities.WorkOrderStatus.Completed;
                            wo.ActualEndAt = DateTime.UtcNow;
                            woJustCompleted = true;
                        }
                    }
                    touchedWo = wo;
                }
            }

            await db.SaveChangesAsync();

            // === NG 발생 시 알람 생성 (Phase 2 Alarm) ===
            if (!isPass)
            {
                var ngCodeStr = history.NgCode ?? "?";
                await alarmSvc.CreateAsync(new Data.Entities.AlarmEvent
                {
                    ClientId = client.Id,
                    AlarmType = Data.Entities.AlarmEventType.NG,
                    Severity = Data.Entities.AlarmSeverity.Major,
                    Title = $"NG @ {client.Name} — Recipe {recipe?.Name ?? request.RecipeId.ToString()}",
                    Message = $"NG codes: {ngCodeStr}" +
                              (request.LotId.HasValue ? $" / LotId={request.LotId}" : "") +
                              (request.SerialNumber is not null ? $" / SN={request.SerialNumber}" : ""),
                    OccurredAt = history.InspectedAt,
                    RelatedHistoryId = history.Id
                });
            }

            // Stage 3: WO 진행률 응답에 포함 (VMS 가 헤더 칩 갱신 + 알람/자동정지 판정에 사용)
            object? workOrderInfo = null;
            if (touchedWo is not null)
            {
                workOrderInfo = new
                {
                    id = touchedWo.Id,
                    orderNo = touchedWo.OrderNo,
                    plannedQuantity = touchedWo.PlannedQuantity,
                    producedQuantity = touchedWo.ProducedQuantity,
                    passQuantity = touchedWo.PassQuantity,
                    ngQuantity = touchedWo.NgQuantity,
                    status = touchedWo.Status,
                    completed = woJustCompleted
                };

                // C5: 모든 연결된 VMS / 라이브 모니터 화면에 진행률 broadcast.
                // 본인 (업로드한 VMS) 도 같은 페이로드를 응답으로 받지만, idempotent 핸들러라 무해.
                try
                {
                    await hub.Clients.All.SendAsync("WorkOrderUpdated", workOrderInfo);
                    if (woJustCompleted)
                        await hub.Clients.All.SendAsync("WorkOrderCompleted", workOrderInfo);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[VmsPublicHub] WO broadcast failed");
                }
            }

            return Results.Ok(new
            {
                historyId = history.Id,
                isPass,
                workOrder = workOrderInfo
            });
        }).AllowAnonymous()
          .AddEndpointFilter<ValidationEndpointFilter<ParameterResultUploadRequest>>();

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
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<RecipeParameterDto>>();

        // 배치 생성 — 배치 검증은 ValidationFilter 가 List 자체에 적용되지 않으므로
        // 서비스 레이어에서 항목별 검증 권장 (별도 PR).
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
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"))
          .AddEndpointFilter<ValidationEndpointFilter<RecipeParameterDto>>();

        // 삭제
        group.MapDelete("/{id:int}", async (int id, IRecipeParameterService service) =>
        {
            var success = await service.DeleteAsync(id);
            return success ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }
}
