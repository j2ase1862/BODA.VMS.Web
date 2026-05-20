using System.Security.Claims;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class AlarmEndpoints
{
    public static void MapAlarmEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/alarms")
            .RequireAuthorization();

        group.MapGet("/", async (
            int? clientId, string? alarmType, string? severity, string? state,
            DateTime? startDate, DateTime? endDate,
            int? page, int? pageSize,
            IAlarmService svc) =>
        {
            var filter = new AlarmFilterDto
            {
                ClientId = clientId,
                AlarmType = alarmType,
                Severity = severity,
                State = state,
                StartDate = startDate,
                EndDate = endDate,
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(50)
            };
            var result = await svc.GetAsync(filter);
            return Results.Ok(result);
        });

        group.MapGet("/summary", async (IAlarmService svc) =>
        {
            var s = await svc.GetSummaryAsync();
            return Results.Ok(s);
        });

        group.MapGet("/{id:int}", async (int id, IAlarmService svc) =>
        {
            var a = await svc.GetByIdAsync(id);
            return a is null ? Results.NotFound() : Results.Ok(a);
        });

        group.MapPost("/{id:int}/action", async (
            int id, AlarmActionRequest req, IAlarmService svc, HttpContext ctx) =>
        {
            var userIdClaim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = ctx.User.FindFirstValue("DisplayName")
                ?? ctx.User.FindFirstValue(ClaimTypes.Name)
                ?? "unknown";
            if (!int.TryParse(userIdClaim, out var userId))
                return Results.Unauthorized();

            try
            {
                AlarmEventDto? result = req.Action switch
                {
                    "Acknowledge" => await svc.AcknowledgeAsync(id, userId, userName),
                    "Resolve" => await svc.ResolveAsync(id, userId, userName,
                        req.Resolution ?? throw new ArgumentException("Resolution is required for Resolve action")),
                    _ => throw new ArgumentException($"Unknown action: {req.Action}")
                };
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequireAuthorization(policy => policy.RequireRole("Admin", "User"));
    }
}
