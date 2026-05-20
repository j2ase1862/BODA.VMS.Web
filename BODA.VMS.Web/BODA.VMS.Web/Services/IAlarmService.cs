using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;

namespace BODA.VMS.Web.Services;

public interface IAlarmService
{
    Task<PagedResult<AlarmEventDto>> GetAsync(AlarmFilterDto filter);
    Task<AlarmEventDto?> GetByIdAsync(int id);
    Task<AlarmSummaryDto> GetSummaryAsync();

    /// <summary>알람 생성 (자동 트리거 또는 수동) + SignalR 브로드캐스트</summary>
    Task<AlarmEventDto> CreateAsync(AlarmEvent evt);

    Task<AlarmEventDto?> AcknowledgeAsync(int id, int userId, string userName);
    Task<AlarmEventDto?> ResolveAsync(int id, int userId, string userName, string resolution);
}
