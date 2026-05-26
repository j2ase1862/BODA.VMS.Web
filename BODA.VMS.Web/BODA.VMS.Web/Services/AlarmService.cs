using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class AlarmService : IAlarmService
{
    private readonly BodaVmsDbContext _db;
    private readonly IHubContext<VmsHub> _hub;

    public AlarmService(BodaVmsDbContext db, IHubContext<VmsHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<PagedResult<AlarmEventDto>> GetAsync(AlarmFilterDto f)
    {
        var query = _db.AlarmEvents
            .Include(a => a.Client)
            .AsQueryable();

        if (f.ClientId.HasValue) query = query.Where(a => a.ClientId == f.ClientId.Value);
        if (!string.IsNullOrEmpty(f.AlarmType)) query = query.Where(a => a.AlarmType == f.AlarmType);
        if (!string.IsNullOrEmpty(f.Severity)) query = query.Where(a => a.Severity == f.Severity);
        if (f.StartDate.HasValue) query = query.Where(a => a.OccurredAt >= f.StartDate.Value);
        if (f.EndDate.HasValue) query = query.Where(a => a.OccurredAt <= f.EndDate.Value);

        if (!string.IsNullOrEmpty(f.State))
        {
            query = f.State switch
            {
                "New" => query.Where(a => a.AcknowledgedAt == null && a.ResolvedAt == null),
                "Ack" => query.Where(a => a.AcknowledgedAt != null && a.ResolvedAt == null),
                "Resolved" => query.Where(a => a.ResolvedAt != null),
                _ => query
            };
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .Select(a => ToDto(a))
            .ToListAsync();

        return new PagedResult<AlarmEventDto>
        {
            Items = items,
            TotalCount = total,
            Page = f.Page,
            PageSize = f.PageSize
        };
    }

    public async Task<AlarmEventDto?> GetByIdAsync(int id)
    {
        var e = await _db.AlarmEvents.Include(a => a.Client).FirstOrDefaultAsync(a => a.Id == id);
        return e is null ? null : ToDto(e);
    }

    public async Task<AlarmSummaryDto> GetSummaryAsync()
    {
        var newCount = await _db.AlarmEvents.CountAsync(a => a.AcknowledgedAt == null && a.ResolvedAt == null);
        var ackCount = await _db.AlarmEvents.CountAsync(a => a.AcknowledgedAt != null && a.ResolvedAt == null);
        var critCount = await _db.AlarmEvents.CountAsync(a => a.Severity == AlarmSeverity.Critical && a.ResolvedAt == null);
        return new AlarmSummaryDto
        {
            NewCount = newCount,
            AckCount = ackCount,
            CriticalCount = critCount
        };
    }

    public async Task<AlarmEventDto> CreateAsync(AlarmEvent evt)
    {
        _db.AlarmEvents.Add(evt);
        await _db.SaveChangesAsync();

        // 새 알람을 SignalR로 즉시 푸시 (Client는 reload 트리거로 활용 가능)
        var dto = await GetByIdAsync(evt.Id);
        if (dto is not null)
        {
            await _hub.Clients.All.SendAsync("AlarmCreated", dto);
            // 기존 NgAlert 호환성 유지
            if (evt.AlarmType == AlarmEventType.NG)
                await _hub.Clients.All.SendAsync("NgAlert", evt.Title);
        }
        return dto!;
    }

    public async Task<AlarmEventDto?> AcknowledgeAsync(int id, int userId, string userName)
    {
        var a = await _db.AlarmEvents.FindAsync(id);
        if (a is null) return null;
        if (a.AcknowledgedAt.HasValue) return await GetByIdAsync(id); // 이미 ack됨

        a.AcknowledgedAt = DateTime.UtcNow;
        a.AcknowledgedBy = userId;
        a.AcknowledgedByName = userName;
        await _db.SaveChangesAsync();

        var dto = await GetByIdAsync(id);
        if (dto is not null)
            await _hub.Clients.All.SendAsync("AlarmUpdated", dto);
        return dto;
    }

    public async Task<AlarmEventDto?> ResolveAsync(int id, int userId, string userName, string resolution)
    {
        var a = await _db.AlarmEvents.FindAsync(id);
        if (a is null) return null;

        // Ack 없이 바로 Resolve도 허용 (작업자가 즉시 해제하는 경우)
        if (!a.AcknowledgedAt.HasValue)
        {
            a.AcknowledgedAt = DateTime.UtcNow;
            a.AcknowledgedBy = userId;
            a.AcknowledgedByName = userName;
        }
        a.ResolvedAt = DateTime.UtcNow;
        a.ResolvedBy = userId;
        a.ResolvedByName = userName;
        a.Resolution = resolution;
        await _db.SaveChangesAsync();

        var dto = await GetByIdAsync(id);
        if (dto is not null)
            await _hub.Clients.All.SendAsync("AlarmUpdated", dto);
        return dto;
    }

    public async Task<int> AcknowledgeAllAsync(int userId, string userName)
    {
        var now = DateTime.UtcNow;
        var newOnes = await _db.AlarmEvents
            .Where(a => a.AcknowledgedAt == null && a.ResolvedAt == null)
            .ToListAsync();
        if (newOnes.Count == 0) return 0;

        foreach (var a in newOnes)
        {
            a.AcknowledgedAt = now;
            a.AcknowledgedBy = userId;
            a.AcknowledgedByName = userName;
        }
        await _db.SaveChangesAsync();

        // 각 알람마다 broadcast — 다른 관리자 화면이 즉시 갱신되도록.
        // 대량일 경우 단일 broadcast 도 가능하지만 클라이언트 호환성을 위해 기존 채널 사용.
        foreach (var a in newOnes)
        {
            var dto = await GetByIdAsync(a.Id);
            if (dto is not null)
                await _hub.Clients.All.SendAsync("AlarmUpdated", dto);
        }
        return newOnes.Count;
    }

    private static AlarmEventDto ToDto(AlarmEvent a) => new()
    {
        Id = a.Id,
        ClientId = a.ClientId,
        ClientName = a.Client?.Name,
        ClientIndex = a.Client?.ClientIndex,
        AlarmType = a.AlarmType,
        Severity = a.Severity,
        Title = a.Title,
        Message = a.Message,
        OccurredAt = a.OccurredAt,
        AcknowledgedAt = a.AcknowledgedAt,
        AcknowledgedBy = a.AcknowledgedBy,
        AcknowledgedByName = a.AcknowledgedByName,
        ResolvedAt = a.ResolvedAt,
        ResolvedBy = a.ResolvedBy,
        ResolvedByName = a.ResolvedByName,
        Resolution = a.Resolution,
        RelatedHistoryId = a.RelatedHistoryId
    };
}
