using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IReliabilityService
{
    /// <summary>라인별 MTBF/MTTR 계산. EquipmentStatusLog의 Down 전이 횟수를 Failure로 카운트.</summary>
    Task<List<ReliabilityStatsDto>> CalculateAsync(ReliabilityRequestDto req);
}
