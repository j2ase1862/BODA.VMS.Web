using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Services;

namespace BODA.VMS.Web.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/reports")
            .RequireAuthorization();

        group.MapPost("/pdf", async (ReportRequestDto req, IReportService svc) =>
        {
            try
            {
                var (pdf, fileName) = await svc.GenerateAsync(req);
                return Results.File(pdf, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }
}
