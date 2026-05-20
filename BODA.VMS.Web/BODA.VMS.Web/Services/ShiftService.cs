using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ShiftService : IShiftService
{
    private readonly BodaVmsDbContext _db;

    public ShiftService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<ShiftDto>> GetAllAsync(bool includeInactive = false)
    {
        var q = _db.Shifts.AsQueryable();
        if (!includeInactive) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.StartHour).Select(s => ToDto(s)).ToListAsync();
    }

    public async Task<ShiftDto?> GetByIdAsync(int id)
    {
        var e = await _db.Shifts.FindAsync(id);
        return e is null ? null : ToDto(e);
    }

    public async Task<ShiftDto> CreateAsync(ShiftDto dto)
    {
        ValidateRange(dto);
        var e = new Shift
        {
            Name = dto.Name.Trim(),
            StartHour = dto.StartHour,
            EndHour = dto.EndHour,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        _db.Shifts.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<ShiftDto?> UpdateAsync(int id, ShiftDto dto)
    {
        ValidateRange(dto);
        var e = await _db.Shifts.FindAsync(id);
        if (e is null) return null;
        e.Name = dto.Name.Trim();
        e.StartHour = dto.StartHour;
        e.EndHour = dto.EndHour;
        e.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var hasHistory = await _db.InspectionHistories.AnyAsync(h => h.ShiftId == id);
        if (hasHistory)
            throw new InvalidOperationException("이 Shift를 참조하는 검사 이력이 있어 삭제할 수 없습니다. 대신 비활성화하세요.");

        var e = await _db.Shifts.FindAsync(id);
        if (e is null) return false;
        _db.Shifts.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int?> ResolveShiftIdAsync(DateTime time)
    {
        var shifts = await _db.Shifts.Where(s => s.IsActive).ToListAsync();
        return shifts.FirstOrDefault(s => s.Contains(time))?.Id;
    }

    public async Task<List<ShiftReportEntryDto>> GetReportAsync(ShiftReportRequestDto req)
    {
        var q = _db.InspectionHistories
            .Where(h => h.InspectedAt >= req.StartDate && h.InspectedAt <= req.EndDate);
        if (req.ClientId.HasValue)
            q = q.Where(h => h.ClientId == req.ClientId.Value);

        var shifts = await _db.Shifts.ToDictionaryAsync(s => s.Id, s => s.Name);

        var raw = await q
            .Where(h => h.ShiftId != null)
            .GroupBy(h => new { Date = h.InspectedAt.Date, h.ShiftId })
            .Select(g => new
            {
                g.Key.Date,
                ShiftId = g.Key.ShiftId!.Value,
                Total = g.Count(),
                Pass = g.Count(h => h.IsPass)
            })
            .ToListAsync();

        return raw
            .Select(r => new ShiftReportEntryDto
            {
                Date = r.Date,
                ShiftId = r.ShiftId,
                ShiftName = shifts.GetValueOrDefault(r.ShiftId, $"Shift#{r.ShiftId}"),
                TotalCount = r.Total,
                PassCount = r.Pass,
                NgCount = r.Total - r.Pass
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.ShiftId)
            .ToList();
    }

    private static void ValidateRange(ShiftDto d)
    {
        if (d.StartHour < 0 || d.StartHour > 23) throw new ArgumentException("StartHour must be 0-23");
        if (d.EndHour < 0 || d.EndHour > 23) throw new ArgumentException("EndHour must be 0-23");
        if (d.StartHour == d.EndHour) throw new ArgumentException("Start와 End가 같을 수 없습니다");
    }

    private static ShiftDto ToDto(Shift e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        StartHour = e.StartHour,
        EndHour = e.EndHour,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt
    };
}
