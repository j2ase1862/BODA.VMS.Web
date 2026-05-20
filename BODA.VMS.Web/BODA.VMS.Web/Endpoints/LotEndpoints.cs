using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class LotEndpoints
{
    public static void MapLotEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/lots")
            .RequireAuthorization();

        group.MapGet("/{id:int}", async (int id, ILotService svc) =>
        {
            var lot = await svc.GetByIdAsync(id);
            return lot is null ? Results.NotFound() : Results.Ok(lot);
        });

        group.MapGet("/by-number/{lotNumber}", async (string lotNumber, ILotService svc) =>
        {
            var lot = await svc.GetByLotNumberAsync(lotNumber);
            return lot is null ? Results.NotFound() : Results.Ok(lot);
        }).AllowAnonymous(); // VMS가 LotNumber로 조회 가능

        group.MapPost("/{id:int}/close", async (int id, ILotService svc) =>
        {
            var result = await svc.CloseAsync(id);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));
    }
}
