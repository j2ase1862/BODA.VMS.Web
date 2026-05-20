using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ReliabilityService : IReliabilityService
{
    private readonly BodaVmsDbContext _db;

    public ReliabilityService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<ReliabilityStatsDto>> CalculateAsync(ReliabilityRequestDto req)
    {
        var start = req.StartDate;
        var end = req.EndDate > DateTime.UtcNow ? DateTime.UtcNow : req.EndDate;

        var clientsQuery = _db.Clients.Where(c => c.IsActive);
        if (req.ClientId.HasValue) clientsQuery = clientsQuery.Where(c => c.Id == req.ClientId.Value);
        var clients = await clientsQuery.OrderBy(c => c.ClientIndex).ToListAsync();

        var result = new List<ReliabilityStatsDto>();

        foreach (var c in clients)
        {
            var logs = await _db.EquipmentStatusLogs
                .Where(e => e.ClientId == c.Id)
                .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
                .OrderBy(e => e.StartedAt)
                .ToListAsync();

            double runningSec = 0, downSec = 0;
            int failureCount = 0;

            foreach (var log in logs)
            {
                var s = log.StartedAt < start ? start : log.StartedAt;
                var e = log.EndedAt ?? DateTime.UtcNow;
                if (e > end) e = end;
                var sec = (e - s).TotalSeconds;
                if (sec <= 0) continue;

                if (log.Status == EquipmentStatus.Running) runningSec += sec;
                if (log.Status == EquipmentStatus.Down)
                {
                    downSec += sec;
                    // 기간 내에 새로 시작된 Down만 Failure로 카운트 (기간 시작 시점부터 이어진 Down은 제외)
                    if (log.StartedAt >= start) failureCount++;
                }
            }

            var runHours = runningSec / 3600.0;
            var downHours = downSec / 3600.0;

            double? mtbf = failureCount > 0 ? runHours / failureCount : (double?)null;
            double? mttr = failureCount > 0 ? downHours / failureCount : (double?)null;
            double? availability = (mtbf.HasValue && mttr.HasValue && (mtbf + mttr) > 0)
                ? mtbf / (mtbf + mttr)
                : null;

            result.Add(new ReliabilityStatsDto
            {
                ClientId = c.Id,
                ClientIndex = c.ClientIndex,
                ClientName = c.Name,
                StartDate = start,
                EndDate = end,
                FailureCount = failureCount,
                TotalRunningHours = Math.Round(runHours, 2),
                TotalDownHours = Math.Round(downHours, 2),
                MtbfHours = mtbf.HasValue ? Math.Round(mtbf.Value, 2) : null,
                MttrHours = mttr.HasValue ? Math.Round(mttr.Value, 2) : null,
                Availability = availability.HasValue ? Math.Round(availability.Value, 4) : null
            });
        }

        return result;
    }
}
