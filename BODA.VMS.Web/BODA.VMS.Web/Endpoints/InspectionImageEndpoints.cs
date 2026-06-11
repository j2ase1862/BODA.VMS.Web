using System.Text.Json;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

public static class InspectionImageEndpoints
{
    private static readonly JsonSerializerOptions MetaJson = new() { PropertyNameCaseInsensitive = true };

    public static void MapInspectionImageEndpoints(this WebApplication app)
    {
        // VMS 가 검사 이미지(멀티파트: image + meta) 업로드.
        // CorrelationKey 로 InspectionHistory 매칭 — 레코드가 아직 없으면 409 → VMS 가 재시도.
        app.MapPost("/api/inspection-images", async (
            HttpRequest request,
            BodaVmsDbContext db,
            IImageStoreService store,
            ILogger<Program> logger) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("multipart/form-data 가 필요합니다.");

            var form = await request.ReadFormAsync();
            var file = form.Files["image"];
            var metaJson = form["meta"].ToString();
            if (file is null || file.Length == 0 || string.IsNullOrWhiteSpace(metaJson))
                return Results.BadRequest("image 파일과 meta(JSON) 가 필요합니다.");

            InspectionImageMeta? meta;
            try { meta = JsonSerializer.Deserialize<InspectionImageMeta>(metaJson, MetaJson); }
            catch (Exception ex) { return Results.BadRequest($"meta 파싱 실패: {ex.Message}"); }

            if (meta is null || string.IsNullOrWhiteSpace(meta.CorrelationKey))
                return Results.BadRequest("meta.CorrelationKey 가 필요합니다.");

            // CorrelationKey 매칭. 결과 레코드가 아직 도착 전이면 409 (VMS 가 백오프 재시도).
            var history = await db.InspectionHistories
                .FirstOrDefaultAsync(h => h.CorrelationKey == meta.CorrelationKey);
            if (history is null)
            {
                return Results.Json(
                    new { reason = "history-not-found", correlationKey = meta.CorrelationKey },
                    statusCode: StatusCodes.Status409Conflict);
            }

            // 이미 매칭된 이미지가 있으면 멱등 — 그대로 성공 반환(재시도 중복 방지).
            if (!string.IsNullOrEmpty(history.ImagePath))
                return Results.Ok(new { imagePath = history.ImagePath, duplicate = true });

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            var capturedAt = DateTime.TryParse(meta.CapturedAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var ts) ? ts : history.InspectedAt;

            var relPath = await store.SaveAsync(bytes, meta.CorrelationKey,
                meta.Verdict ?? (history.IsPass ? "OK" : "NG"), meta.Ext ?? "jpg", capturedAt);

            history.ImagePath = relPath;
            await db.SaveChangesAsync();

            logger.LogInformation("검사 이미지 저장: historyId={Id}, key={Key}, path={Path}",
                history.Id, meta.CorrelationKey, relPath);

            return Results.Ok(new { imagePath = relPath });
        }).AllowAnonymous()
          .AddEndpointFilter<ClientApiKeyEndpointFilter>()
          .DisableAntiforgery();
    }

    /// <summary>VMS ImageUploadMeta 와 대응(부분 필드). 대소문자 무시 역직렬화.</summary>
    private sealed class InspectionImageMeta
    {
        public string CorrelationKey { get; set; } = string.Empty;
        public string? Verdict { get; set; }
        public string? Variant { get; set; }
        public string? Ext { get; set; }
        public string? CapturedAt { get; set; }
    }
}
