using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class OeeService : IOeeService
{
    private readonly BodaVmsDbContext _db;

    public OeeService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<OeeResultDto> CalculateAsync(OeeRequestDto req)
    {
        var start = req.StartDate;
        var end = req.EndDate > DateTime.UtcNow ? DateTime.UtcNow : req.EndDate;
        var periodSec = (end - start).TotalSeconds;
        if (periodSec <= 0) periodSec = 1;

        var clientsQuery = _db.Clients.Where(c => c.IsActive);
        if (req.ClientId.HasValue)
            clientsQuery = clientsQuery.Where(c => c.Id == req.ClientId.Value);
        var clients = await clientsQuery.ToListAsync();

        var result = new OeeResultDto
        {
            StartDate = start,
            EndDate = end,
            Clients = new List<OeeClientResultDto>()
        };

        foreach (var c in clients)
        {
            // 1) 상태 로그 → 각 Status별 누적 시간 (기간 클리핑)
            var logs = await _db.EquipmentStatusLogs
                .Where(e => e.ClientId == c.Id)
                .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
                .ToListAsync();

            double running = 0, idle = 0, down = 0;
            foreach (var log in logs)
            {
                var s = log.StartedAt < start ? start : log.StartedAt;
                var e = (log.EndedAt ?? DateTime.UtcNow);
                if (e > end) e = end;
                var sec = (e - s).TotalSeconds;
                if (sec <= 0) continue;

                switch (log.Status)
                {
                    case EquipmentStatus.Running: running += sec; break;
                    case EquipmentStatus.Idle: idle += sec; break;
                    case EquipmentStatus.Down: down += sec; break;
                }
            }

            // 로그가 없는 기간(=상태 미확정)은 Down으로 간주 (보수적)
            var accountedSec = running + idle + down;
            if (accountedSec < periodSec)
                down += (periodSec - accountedSec);

            // 2) 검사 결과 집계
            var insp = await _db.InspectionHistories
                .Where(h => h.ClientId == c.Id && h.InspectedAt >= start && h.InspectedAt <= end)
                .GroupBy(h => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Pass = g.Count(h => h.IsPass)
                })
                .FirstOrDefaultAsync();
            var total = insp?.Total ?? 0;
            var pass = insp?.Pass ?? 0;
            var ng = total - pass;

            // 3) OEE 계산
            var available = running + idle;
            var availability = periodSec > 0 ? available / periodSec : 0;
            var performance = available > 0 ? running / available : 0;
            var quality = total > 0 ? (double)pass / total : 1.0;
            var oee = availability * performance * quality;

            result.Clients.Add(new OeeClientResultDto
            {
                ClientId = c.Id,
                ClientName = c.Name,
                ClientIndex = c.ClientIndex,
                PeriodSeconds = periodSec,
                RunningSeconds = running,
                IdleSeconds = idle,
                DownSeconds = down,
                TotalInspections = total,
                PassCount = pass,
                NgCount = ng,
                Availability = Math.Round(availability, 4),
                Performance = Math.Round(performance, 4),
                Quality = Math.Round(quality, 4),
                Oee = Math.Round(oee, 4)
            });
        }

        result.Clients = result.Clients.OrderBy(c => c.ClientIndex).ToList();
        return result;
    }
}
