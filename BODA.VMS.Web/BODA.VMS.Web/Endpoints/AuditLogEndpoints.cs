using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class AuditLogEndpoints
{
    public static void MapAuditLogEndpoints(this WebApplication app)
    {
        // Audit Log는 Admin 전용 조회 (변경/삭제 불가 — 인터셉터가 INSERT만)
        var group = app.MapGroup("/api/audit-logs")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        group.MapGet("/", async (
            string? entityName, string? action, int? userId,
            DateTime? startDate, DateTime? endDate,
            int? page, int? pageSize,
            IAuditLogService svc) =>
        {
            var filter = new AuditLogFilterDto
            {
                EntityName = entityName,
                Action = action,
                UserId = userId,
                StartDate = startDate,
                EndDate = endDate,
                Page = page.GetValueOrDefault(1),
                PageSize = pageSize.GetValueOrDefault(50)
            };
            var result = await svc.GetAsync(filter);
            return Results.Ok(result);
        });

        group.MapGet("/entity-names", async (IAuditLogService svc) =>
        {
            var names = await svc.GetEntityNamesAsync();
            return Results.Ok(names);
        });

        group.MapGet("/{id:long}", async (long id, IAuditLogService svc) =>
        {
            var a = await svc.GetByIdAsync(id);
            return a is null ? Results.NotFound() : Results.Ok(a);
        });
    }
}
