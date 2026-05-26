using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class OperatorSessionService : IOperatorSessionService
{
    private readonly BodaVmsDbContext _db;
    private readonly IHubContext<VmsHub> _hub;

    public OperatorSessionService(BodaVmsDbContext db, IHubContext<VmsHub> hub)
    {
        _db = db;
        _hub = hub;
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

        var dto = (await GetByIdAsync(session.Id))!;
        // 자동 종료된 기존 세션도 broadcast 해서 admin 페이지가 동시에 갱신되도록
        foreach (var prev in existing)
            await BroadcastSessionEndedAsync(prev.Id);
        await BroadcastSessionStartedAsync(dto);
        return dto;
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
        var dto = await GetByIdAsync(session.Id);
        if (dto != null) await BroadcastSessionEndedAsync(dto);
        return dto;
    }

    private async Task BroadcastSessionStartedAsync(OperatorSessionDto dto)
    {
        try { await _hub.Clients.All.SendAsync("OperatorSessionStarted", dto); }
        catch { /* hub 미가용 시 무시 — REST 응답은 정상 */ }
    }

    private async Task BroadcastSessionEndedAsync(OperatorSessionDto dto)
    {
        try { await _hub.Clients.All.SendAsync("OperatorSessionEnded", dto); }
        catch { }
    }

    /// <summary>Id 만 알 때 종료 broadcast 헬퍼 — DTO 재조회 후 송신.</summary>
    private async Task BroadcastSessionEndedAsync(int sessionId)
    {
        try
        {
            var dto = await GetByIdAsync(sessionId);
            if (dto != null) await _hub.Clients.All.SendAsync("OperatorSessionEnded", dto);
        }
        catch { }
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
        Role = s.Operator?.Role ?? OperatorRole.Operator,
        ClientId = s.ClientId,
        ClientIndex = s.Client?.ClientIndex ?? 0,
        ClientName = s.Client?.Name ?? "",
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        EndReason = s.EndReason
    };
}
