using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Middleware;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class SpcEndpoints
{
    public static void MapSpcEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/spc")
            .RequireAuthorization();

        group.MapPost("/compute", async (SpcRequestDto req, ISpcService svc) =>
        {
            var result = await svc.ComputeAsync(req);
            return Results.Ok(result);
        }).AddEndpointFilter<ValidationEndpointFilter<SpcRequestDto>>();
    }
}
