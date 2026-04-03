using System.Text.Json;
using System.Text.Json.Nodes;

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
        });
    }

    private record VisionServerSettingsResponse(bool Enabled, string BaseUrl);
    private record VisionServerSettingsRequest(bool Enabled, string? BaseUrl);
}
