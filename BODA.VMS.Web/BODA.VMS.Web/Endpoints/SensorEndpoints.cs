using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Endpoints;

/// <summary>
/// Predictive_DefectRate_Plan §5.2 — 환경 센서 시계열 수집.
/// Heartbeat 와 동일한 anonymous 정책: VMS 가 운영 중 인증 토큰 없이 호출.
/// </summary>
public static class SensorEndpoints
{
    public static void MapSensorEndpoints(this WebApplication app)
    {
        app.MapPost("/api/sensors/readings", async (
            SensorReadingRequest request,
            BodaVmsDbContext db) =>
        {
            var client = await db.Clients
                .FirstOrDefaultAsync(c => c.ClientIndex == request.ClientIndex);

            if (client is null)
                return Results.NotFound($"Client with index {request.ClientIndex} not found");

            // 모든 측정값이 null 이면 무의미한 row — reject 하여 디스크 낭비 방지
            if (request.TemperatureC is null
                && request.HumidityPct is null
                && request.VibrationRms is null
                && request.PressurePsi is null)
            {
                return Results.BadRequest("At least one sensor value must be provided.");
            }

            var reading = new SensorReading
            {
                ClientId = client.Id,
                Timestamp = request.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow,
                TemperatureC = request.TemperatureC,
                HumidityPct = request.HumidityPct,
                VibrationRms = request.VibrationRms,
                PressurePsi = request.PressurePsi
            };

            db.SensorReadings.Add(reading);
            await db.SaveChangesAsync();

            return Results.Ok(new { id = reading.Id });
        }).AllowAnonymous();
    }
}
