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

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsOpenRowUniqueViolation(ex))
        {
            // 크로스 프로세스 race 패자: 다른 writer 가 먼저 open 행을 만들어
            // UX_EquipmentStatusLogs_ClientId_Open 유니크 제약에 걸린 경우.
            // 현재 상태는 이미 다른 writer 가 기록했고, 다음 모니터 주기(수 초)에
            // 재평가되어 수렴하므로 이 전이는 안전하게 버린다.
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>SQLite UNIQUE 제약(EndedAt IS NULL partial index) 위반 여부.</summary>
    private static bool IsOpenRowUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.Sqlite.SqliteException sqlite
        && sqlite.SqliteErrorCode == 19; // SQLITE_CONSTRAINT

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
