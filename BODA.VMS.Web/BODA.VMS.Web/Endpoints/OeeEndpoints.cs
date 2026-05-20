using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class OeeEndpoints
{
    public static void MapOeeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/oee")
            .RequireAuthorization();

        group.MapPost("/calculate", async (OeeRequestDto req, IOeeService svc) =>
        {
            var result = await svc.CalculateAsync(req);
            return Results.Ok(result);
        });

        group.MapGet("/logs/{clientId:int}", async (
            int clientId, DateTime startDate, DateTime endDate,
            IEquipmentStatusService svc) =>
        {
            var logs = await svc.GetLogsAsync(clientId, startDate, endDate);
            return Results.Ok(logs);
        });
    }
}
