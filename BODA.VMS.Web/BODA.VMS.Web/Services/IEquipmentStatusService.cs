using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IEquipmentStatusService
{
    /// <summary>
    /// 상태 전이 기록. 같은 status면 무시. 다르면 이전 row의 EndedAt 채우고 새 row 시작.
    /// </summary>
    Task RecordTransitionAsync(int clientId, string newStatus, string? reason = null);

    /// <summary>Client별 현재 상태 (NULL이면 아직 로그 없음)</summary>
    Task<EquipmentStatusLogDto?> GetCurrentAsync(int clientId);

    /// <summary>기간 내 상태 로그</summary>
    Task<List<EquipmentStatusLogDto>> GetLogsAsync(int clientId, DateTime start, DateTime end);
}
