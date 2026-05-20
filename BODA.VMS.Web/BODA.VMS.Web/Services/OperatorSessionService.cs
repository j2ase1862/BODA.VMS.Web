using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class OperatorSessionService : IOperatorSessionService
{
    private readonly BodaVmsDbContext _db;

    public OperatorSessionService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<OperatorSessionDto?> GetCurrentByClientIdAsync(int clientId)
    {
        var e = await _db.OperatorSessions
            .Include(s => s.Operator)
            .Include(s => s.Client)
            .Where(s => s.ClientId == clientId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
        return e is null ? null : ToDto(e);
    }

    public async Task<OperatorSessionDto?> GetCurrentByClientIndexAsync(int clientIndex)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.ClientIndex == clientIndex);
        return client is null ? null : await GetCurrentByClientIdAsync(client.Id);
    }

    public async Task<int?> ResolveCurrentOperatorIdAsync(int clientId)
    {
        return await _db.OperatorSessions
            .Where(s => s.ClientId == clientId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => (int?)s.OperatorId)
            .FirstOrDefaultAsync();
    }

    public async Task<OperatorSessionDto> StartSessionAsync(int clientId, int operatorId)
    {
        var client = await _db.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException($"Client {clientId} not found");
        var op = await _db.Operators.FindAsync(operatorId)
            ?? throw new InvalidOperationException($"Operator {operatorId} not found");
        if (!op.IsActive)
            throw new InvalidOperationException("비활성 작업자입니다.");

        // 기존 활성 세션이 있으면 자동 종료
        var existing = await _db.OperatorSessions
            .Where(s => s.ClientId == clientId && s.EndedAt == null)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var prev in existing)
        {
            prev.EndedAt = now;
            prev.EndReason = prev.OperatorId == operatorId
                ? SessionEndReason.ShiftChange  // 같은 작업자 재로그인 (드문 케이스)
                : SessionEndReason.Auto;        // 다른 작업자가 강제 교대
        }

        var session = new OperatorSession
        {
            OperatorId = operatorId,
            ClientId = clientId,
            StartedAt = now
        };
        _db.OperatorSessions.Add(session);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(session.Id))!;
    }

    public async Task<OperatorSessionDto?> EndSessionAsync(int clientId, string reason)
    {
        var session = await _db.OperatorSessions
            .Where(s => s.ClientId == clientId && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
        if (session is null) return null;

        session.EndedAt = DateTime.UtcNow;
        session.EndReason = reason;
        await _db.SaveChangesAsync();
        return await GetByIdAsync(session.Id);
    }

    public async Task<List<OperatorSessionDto>> GetHistoryAsync(
        int? clientId, int? operatorId,
        DateTime? startDate, DateTime? endDate, int limit = 200)
    {
        var q = _db.OperatorSessions
            .Include(s => s.Operator)
            .Include(s => s.Client)
            .AsQueryable();

        if (clientId.HasValue) q = q.Where(s => s.ClientId == clientId.Value);
        if (operatorId.HasValue) q = q.Where(s => s.OperatorId == operatorId.Value);
        if (startDate.HasValue) q = q.Where(s => s.StartedAt >= startDate.Value);
        if (endDate.HasValue) q = q.Where(s => s.StartedAt <= endDate.Value);

        var rows = await q
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .ToListAsync();

        return rows.Select(ToDto).ToList();
    }

    private async Task<OperatorSessionDto?> GetByIdAsync(int id)
    {
        var e = await _db.OperatorSessions
            .Include(s => s.Operator)
            .Include(s => s.Client)
            .FirstOrDefaultAsync(s => s.Id == id);
        return e is null ? null : ToDto(e);
    }

    private static OperatorSessionDto ToDto(OperatorSession s) => new()
    {
        Id = s.Id,
        OperatorId = s.OperatorId,
        OperatorName = s.Operator?.Name ?? "",
        EmployeeNumber = s.Operator?.EmployeeNumber ?? "",
        Department = s.Operator?.Department,
        ClientId = s.ClientId,
        ClientIndex = s.Client?.ClientIndex ?? 0,
        ClientName = s.Client?.Name ?? "",
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        EndReason = s.EndReason
    };
}
