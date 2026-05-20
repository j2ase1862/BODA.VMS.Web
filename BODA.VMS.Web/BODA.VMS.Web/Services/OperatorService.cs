using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class OperatorService : IOperatorService
{
    private readonly BodaVmsDbContext _db;

    public OperatorService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<List<OperatorDto>> GetAllAsync(bool includeInactive = false)
    {
        var q = _db.Operators.AsQueryable();
        if (!includeInactive) q = q.Where(o => o.IsActive);
        return await q.OrderBy(o => o.EmployeeNumber)
            .Select(o => ToDto(o))
            .ToListAsync();
    }

    public async Task<OperatorDto?> GetByIdAsync(int id)
    {
        var e = await _db.Operators.FindAsync(id);
        return e is null ? null : ToDto(e);
    }

    public async Task<OperatorDto?> GetByEmployeeNumberAsync(string employeeNumber)
    {
        var e = await _db.Operators.FirstOrDefaultAsync(o => o.EmployeeNumber == employeeNumber);
        return e is null ? null : ToDto(e);
    }

    public async Task<OperatorDto> CreateAsync(OperatorUpsertDto dto)
    {
        ValidatePin(dto.Pin, isCreate: true);
        var e = new Operator
        {
            EmployeeNumber = dto.EmployeeNumber.Trim(),
            Name = dto.Name.Trim(),
            Department = string.IsNullOrWhiteSpace(dto.Department) ? null : dto.Department.Trim(),
            PinHash = BCrypt.Net.BCrypt.HashPassword(dto.Pin!),
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        _db.Operators.Add(e);
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<OperatorDto?> UpdateAsync(int id, OperatorUpsertDto dto)
    {
        var e = await _db.Operators.FindAsync(id);
        if (e is null) return null;

        e.EmployeeNumber = dto.EmployeeNumber.Trim();
        e.Name = dto.Name.Trim();
        e.Department = string.IsNullOrWhiteSpace(dto.Department) ? null : dto.Department.Trim();
        e.IsActive = dto.IsActive;

        // PIN은 입력된 경우에만 갱신
        if (!string.IsNullOrWhiteSpace(dto.Pin))
        {
            ValidatePin(dto.Pin, isCreate: false);
            e.PinHash = BCrypt.Net.BCrypt.HashPassword(dto.Pin);
        }

        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(e);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // 세션 이력이 있으면 삭제 거부 (감사 추적성)
        var hasSession = await _db.OperatorSessions.AnyAsync(s => s.OperatorId == id);
        if (hasSession)
            throw new InvalidOperationException("세션 이력이 있는 작업자는 삭제할 수 없습니다. 비활성화하세요.");

        var e = await _db.Operators.FindAsync(id);
        if (e is null) return false;
        _db.Operators.Remove(e);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<OperatorDto?> AuthenticateAsync(string employeeNumber, string pin)
    {
        var e = await _db.Operators
            .FirstOrDefaultAsync(o => o.EmployeeNumber == employeeNumber && o.IsActive);
        if (e is null) return null;
        if (!BCrypt.Net.BCrypt.Verify(pin, e.PinHash)) return null;
        return ToDto(e);
    }

    private static void ValidatePin(string? pin, bool isCreate)
    {
        if (isCreate && string.IsNullOrWhiteSpace(pin))
            throw new ArgumentException("PIN은 필수입니다.");
        if (!string.IsNullOrWhiteSpace(pin) && pin.Length < 4)
            throw new ArgumentException("PIN은 최소 4자리여야 합니다.");
    }

    private static OperatorDto ToDto(Operator e) => new()
    {
        Id = e.Id,
        EmployeeNumber = e.EmployeeNumber,
        Name = e.Name,
        Department = e.Department,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
