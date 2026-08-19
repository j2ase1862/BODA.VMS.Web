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

            // 이미 매칭된 이미지가 있으면 기본 멱등(재시도 중복 방지) — 단, 사이클 키 공유
            // (VMS 사이클 누적: 한 사이클의 여러 카메라 이미지가 같은 키로 도착) 환경에서
            // NG 이력에 OK 이미지가 먼저 붙은 경우는 NG 이미지가 대체한다 (2026-08-19 현장:
            // NG 상세에 이미지가 없거나 OK 이미지가 보이는 문제).
            if (!string.IsNullOrEmpty(history.ImagePath)
                && !ShouldReplaceExisting(history.ImagePath, meta.Verdict, history.IsPass))
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

    /// <summary>
    /// 기존 이미지가 있어도 새 이미지로 대체할지 — NG 이력인데 붙어 있는 이미지가
    /// OK 변형(경로 /OK/)이고 들어온 이미지가 NG 면 대체. 그 외에는 첫 이미지 유지(멱등).
    /// 저장 경로는 ImageStoreService 가 항상 '/' 구분 + OK|NG 폴더로 생성한다.
    /// </summary>
    internal static bool ShouldReplaceExisting(string existingPath, string? incomingVerdict, bool historyIsPass)
        => !historyIsPass
           && string.Equals(incomingVerdict, "NG", StringComparison.OrdinalIgnoreCase)
           && !existingPath.Contains("/NG/", StringComparison.OrdinalIgnoreCase);

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
