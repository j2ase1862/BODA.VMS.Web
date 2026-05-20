using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class AuditLogService : IAuditLogService
{
    private readonly BodaVmsDbContext _db;

    public AuditLogService(BodaVmsDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogDto>> GetAsync(AuditLogFilterDto f)
    {
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(f.EntityName)) q = q.Where(a => a.EntityName == f.EntityName);
        if (!string.IsNullOrEmpty(f.Action)) q = q.Where(a => a.Action == f.Action);
        if (f.UserId.HasValue) q = q.Where(a => a.UserId == f.UserId.Value);
        if (f.StartDate.HasValue) q = q.Where(a => a.Timestamp >= f.StartDate.Value);
        if (f.EndDate.HasValue) q = q.Where(a => a.Timestamp <= f.EndDate.Value);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((f.Page - 1) * f.PageSize)
            .Take(f.PageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                EntityName = a.EntityName,
                EntityId = a.EntityId,
                Action = a.Action,
                Changes = a.Changes,
                Timestamp = a.Timestamp,
                UserId = a.UserId,
                UserName = a.UserName,
                IpAddress = a.IpAddress
            })
            .ToListAsync();

        return new PagedResult<AuditLogDto>
        {
            Items = items,
            TotalCount = total,
            Page = f.Page,
            PageSize = f.PageSize
        };
    }

    public async Task<AuditLogDto?> GetByIdAsync(long id)
    {
        var a = await _db.AuditLogs.FindAsync(id);
        if (a is null) return null;
        return new AuditLogDto
        {
            Id = a.Id,
            EntityName = a.EntityName,
            EntityId = a.EntityId,
            Action = a.Action,
            Changes = a.Changes,
            Timestamp = a.Timestamp,
            UserId = a.UserId,
            UserName = a.UserName,
            IpAddress = a.IpAddress
        };
    }

    public async Task<List<string>> GetEntityNamesAsync()
    {
        return await _db.AuditLogs
            .Select(a => a.EntityName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }
}
