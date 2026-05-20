using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IOperatorSessionService
{
    /// <summary>라인의 현재 작업자 세션 (없으면 null)</summary>
    Task<OperatorSessionDto?> GetCurrentByClientIdAsync(int clientId);
    Task<OperatorSessionDto?> GetCurrentByClientIndexAsync(int clientIndex);

    /// <summary>라인의 현재 작업자 OperatorId만 (검사 결과 자동 매칭용, 가벼움)</summary>
    Task<int?> ResolveCurrentOperatorIdAsync(int clientId);

    /// <summary>키오스크 로그인: 인증 → 기존 세션 자동 종료 → 새 세션 시작</summary>
    Task<OperatorSessionDto> StartSessionAsync(int clientId, int operatorId);

    /// <summary>키오스크 로그아웃</summary>
    Task<OperatorSessionDto?> EndSessionAsync(int clientId, string reason);

    /// <summary>세션 이력 조회 (Admin)</summary>
    Task<List<OperatorSessionDto>> GetHistoryAsync(
        int? clientId, int? operatorId,
        DateTime? startDate, DateTime? endDate, int limit = 200);
}
