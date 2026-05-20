using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class EquipmentStatusService : IEquipmentStatusService
{
    private readonly BodaVmsDbContext _db;

    public EquipmentStatusService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task RecordTransitionAsync(int clientId, string newStatus, string? reason = null)
    {
        var open = await _db.EquipmentStatusLogs
            .Where(e => e.ClientId == clientId && e.EndedAt == null)
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync();

        var now = DateTime.UtcNow;

        if (open is not null && open.Status == newStatus)
            return; // 동일 상태 — 변화 없음

        if (open is not null)
        {
            open.EndedAt = now;
        }

        _db.EquipmentStatusLogs.Add(new EquipmentStatusLog
        {
            ClientId = clientId,
            Status = newStatus,
            StartedAt = now,
            Reason = reason
        });

        await _db.SaveChangesAsync();
    }

    public async Task<EquipmentStatusLogDto?> GetCurrentAsync(int clientId)
    {
        var e = await _db.EquipmentStatusLogs
            .Include(x => x.Client)
            .Where(x => x.ClientId == clientId && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync();

        return e is null ? null : ToDto(e);
    }

    public async Task<List<EquipmentStatusLogDto>> GetLogsAsync(int clientId, DateTime start, DateTime end)
    {
        var logs = await _db.EquipmentStatusLogs
            .Include(e => e.Client)
            .Where(e => e.ClientId == clientId)
            .Where(e => e.StartedAt < end && (e.EndedAt == null || e.EndedAt > start))
            .OrderBy(e => e.StartedAt)
            .ToListAsync();

        return logs.Select(ToDto).ToList();
    }

    private static EquipmentStatusLogDto ToDto(EquipmentStatusLog e)
    {
        var endedAt = e.EndedAt ?? DateTime.UtcNow;
        return new EquipmentStatusLogDto
        {
            Id = e.Id,
            ClientId = e.ClientId,
            ClientName = e.Client?.Name,
            Status = e.Status,
            StartedAt = e.StartedAt,
            EndedAt = e.EndedAt,
            DurationSeconds = (endedAt - e.StartedAt).TotalSeconds,
            Reason = e.Reason
        };
    }
}
