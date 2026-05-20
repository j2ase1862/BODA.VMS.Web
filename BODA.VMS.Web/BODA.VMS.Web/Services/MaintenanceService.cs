using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly BodaVmsDbContext _db;

    public MaintenanceService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<MaintenanceScheduleDto>> GetSchedulesAsync(bool includeInactive = false, int? clientId = null)
    {
        var q = _db.MaintenanceSchedules.Include(s => s.Client).AsQueryable();
        if (!includeInactive) q = q.Where(s => s.IsActive);
        if (clientId.HasValue) q = q.Where(s => s.ClientId == clientId.Value || s.ClientId == null);

        return await q.OrderBy(s => s.NextDueAt).Select(s => ToDto(s)).ToListAsync();
    }

    public async Task<MaintenanceScheduleDto?> GetScheduleByIdAsync(int id)
    {
        var s = await _db.MaintenanceSchedules.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        return s is null ? null : ToDto(s);
    }

    public async Task<MaintenanceScheduleDto> CreateScheduleAsync(MaintenanceScheduleUpsertDto dto)
    {
        if (dto.IntervalDays <= 0) throw new ArgumentException("IntervalDays는 1 이상이어야 합니다.");

        var now = DateTime.UtcNow;
        var entity = new MaintenanceSchedule
        {
            ClientId = dto.ClientId,
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IntervalDays = dto.IntervalDays,
            EstimatedDurationMinutes = dto.EstimatedDurationMinutes,
            NextDueAt = dto.NextDueAt ?? now.AddDays(dto.IntervalDays),
            IsActive = dto.IsActive,
            CreatedAt = now
        };
        _db.MaintenanceSchedules.Add(entity);
        await _db.SaveChangesAsync();
        return (await GetScheduleByIdAsync(entity.Id))!;
    }

    public async Task<MaintenanceScheduleDto?> UpdateScheduleAsync(int id, MaintenanceScheduleUpsertDto dto)
    {
        var s = await _db.MaintenanceSchedules.FindAsync(id);
        if (s is null) return null;
        if (dto.IntervalDays <= 0) throw new ArgumentException("IntervalDays는 1 이상이어야 합니다.");

        s.ClientId = dto.ClientId;
        s.Name = dto.Name.Trim();
        s.Description = dto.Description;
        s.IntervalDays = dto.IntervalDays;
        s.EstimatedDurationMinutes = dto.EstimatedDurationMinutes;
        if (dto.NextDueAt.HasValue) s.NextDueAt = dto.NextDueAt.Value;
        s.IsActive = dto.IsActive;
        s.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetScheduleByIdAsync(id);
    }

    public async Task<bool> DeleteScheduleAsync(int id)
    {
        var hasRec = await _db.MaintenanceRecords.AnyAsync(r => r.ScheduleId == id);
        if (hasRec)
            throw new InvalidOperationException("수행 기록이 있는 일정은 삭제할 수 없습니다. 비활성화하세요.");

        var s = await _db.MaintenanceSchedules.FindAsync(id);
        if (s is null) return false;
        _db.MaintenanceSchedules.Remove(s);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<MaintenanceScheduleDto>> GetDueSchedulesAsync(int withinDays = 7)
    {
        var cutoff = DateTime.UtcNow.AddDays(withinDays);
        return await _db.MaintenanceSchedules
            .Include(s => s.Client)
            .Where(s => s.IsActive && s.NextDueAt <= cutoff)
            .OrderBy(s => s.NextDueAt)
            .Select(s => ToDto(s))
            .ToListAsync();
    }

    public async Task<MaintenanceRecordDto> PerformAsync(int scheduleId, PerformMaintenanceRequest req,
        int? userId, string? userName)
    {
        var s = await _db.MaintenanceSchedules.FindAsync(scheduleId)
            ?? throw new InvalidOperationException($"Schedule {scheduleId} not found");

        var now = DateTime.UtcNow;
        var prevDue = s.NextDueAt;
        var newDue = now.AddDays(s.IntervalDays);

        var record = new MaintenanceRecord
        {
            ScheduleId = scheduleId,
            ClientId = req.ClientId ?? s.ClientId,
            PerformedAt = now,
            ActualDurationMinutes = req.ActualDurationMinutes,
            PerformedByUserId = userId,
            PerformedByName = userName,
            Notes = req.Notes,
            PreviousDueAt = prevDue,
            NewDueAt = newDue
        };
        _db.MaintenanceRecords.Add(record);

        s.LastPerformedAt = now;
        s.NextDueAt = newDue;
        s.UpdatedAt = now;

        await _db.SaveChangesAsync();

        return (await GetRecordByIdAsync(record.Id))!;
    }

    public async Task<List<MaintenanceRecordDto>> GetRecordsAsync(
        int? scheduleId, int? clientId,
        DateTime? startDate, DateTime? endDate, int limit = 200)
    {
        var q = _db.MaintenanceRecords
            .Include(r => r.Schedule)
            .Include(r => r.Client)
            .AsQueryable();

        if (scheduleId.HasValue) q = q.Where(r => r.ScheduleId == scheduleId.Value);
        if (clientId.HasValue) q = q.Where(r => r.ClientId == clientId.Value);
        if (startDate.HasValue) q = q.Where(r => r.PerformedAt >= startDate.Value);
        if (endDate.HasValue) q = q.Where(r => r.PerformedAt <= endDate.Value);

        var rows = await q
            .OrderByDescending(r => r.PerformedAt)
            .Take(limit)
            .ToListAsync();
        return rows.Select(ToRecordDto).ToList();
    }

    private async Task<MaintenanceRecordDto?> GetRecordByIdAsync(int id)
    {
        var r = await _db.MaintenanceRecords
            .Include(x => x.Schedule)
            .Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? null : ToRecordDto(r);
    }

    private static MaintenanceScheduleDto ToDto(MaintenanceSchedule s) => new()
    {
        Id = s.Id,
        ClientId = s.ClientId,
        ClientName = s.Client?.Name,
        ClientIndex = s.Client?.ClientIndex,
        Name = s.Name,
        Description = s.Description,
        IntervalDays = s.IntervalDays,
        EstimatedDurationMinutes = s.EstimatedDurationMinutes,
        LastPerformedAt = s.LastPerformedAt,
        NextDueAt = s.NextDueAt,
        IsActive = s.IsActive,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt
    };

    private static MaintenanceRecordDto ToRecordDto(MaintenanceRecord r) => new()
    {
        Id = r.Id,
        ScheduleId = r.ScheduleId,
        ScheduleName = r.Schedule?.Name ?? "",
        ClientId = r.ClientId,
        ClientName = r.Client?.Name,
        PerformedAt = r.PerformedAt,
        ActualDurationMinutes = r.ActualDurationMinutes,
        PerformedByName = r.PerformedByName,
        Notes = r.Notes,
        PreviousDueAt = r.PreviousDueAt,
        NewDueAt = r.NewDueAt
    };
}
