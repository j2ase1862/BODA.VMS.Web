using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class AndonEndpoints
{
    public static void MapAndonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/andon")
            .RequireAuthorization();

        group.MapGet("/snapshot", async (IAndonService svc) =>
        {
            var s = await svc.GetSnapshotAsync();
            return Results.Ok(s);
        });
    }
}
