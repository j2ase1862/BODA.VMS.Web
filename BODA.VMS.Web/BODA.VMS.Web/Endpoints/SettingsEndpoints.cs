using System.Text.Json;
using System.Text.Json.Nodes;
using BODA.VMS.Web.Middleware;

namespace BODA.VMS.Web.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/visionserver", (IConfiguration config) =>
        {
            return Results.Ok(new VisionServerSettingsResponse(
                Enabled: config.GetValue<bool>("VisionServer:Enabled"),
                BaseUrl: config["VisionServer:BaseUrl"] ?? ""));
        });

        group.MapPut("/visionserver", async (
            VisionServerSettingsRequest request,
            IWebHostEnvironment env) =>
        {
            var appSettingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
            var json = await File.ReadAllTextAsync(appSettingsPath);
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })!;

            var visionServer = root["VisionServer"]?.AsObject();
            if (visionServer is null)
            {
                visionServer = new JsonObject();
                root["VisionServer"] = visionServer;
            }

            visionServer["Enabled"] = request.Enabled;
            if (request.BaseUrl is not null)
                visionServer["BaseUrl"] = request.BaseUrl;

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(appSettingsPath, root.ToJsonString(options));

            return Results.Ok();
        }).AddEndpointFilter<ValidationEndpointFilter<VisionServerSettingsRequest>>();
    }
}

public record VisionServerSettingsResponse(bool Enabled, string BaseUrl);

/// <summary>
/// /api/settings/visionserver PUT 본문. record 라도 FluentValidation 이 검증 가능.
/// public 으로 노출돼 있어 ValidationEndpointFilter 가 발견할 수 있음 — private 였을 때는
/// 외부 어셈블리 (Validators, Tests) 에서 참조 불가해 검증/테스트 불가능했음.
/// </summary>
public record VisionServerSettingsRequest(bool Enabled, string? BaseUrl);
