using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> GetAsync(AuditLogFilterDto filter);
    Task<AuditLogDto?> GetByIdAsync(long id);
    Task<List<string>> GetEntityNamesAsync();
}
