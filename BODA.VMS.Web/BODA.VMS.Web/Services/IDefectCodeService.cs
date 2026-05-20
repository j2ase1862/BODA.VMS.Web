using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IDefectCodeService
{
    Task<List<DefectCodeDto>> GetAllAsync(bool includeInactive = false);
    Task<DefectCodeDto?> GetByIdAsync(int id);
    Task<DefectCodeDto?> GetByCodeAsync(string code);
    Task<DefectCodeDto> CreateAsync(DefectCodeDto dto);
    Task<DefectCodeDto?> UpdateAsync(int id, DefectCodeDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>파레토 분석: NgCode를 집계해 빈도순 정렬 + 누적 % 계산</summary>
    Task<ParetoResultDto> GetParetoAsync(
        DateTime? startDate, DateTime? endDate,
        int? clientId, int? workOrderId, int topN);

    /// <summary>
    /// 자동완성 후보. RecipeParameter.ParamCode(사전 정의) + InspectionHistory.NgCode(실제 발생)를 통합.
    /// 이미 마스터 등록된 코드는 IsRegistered=true.
    /// </summary>
    Task<List<DefectCodeCandidateDto>> GetCandidatesAsync(int recentDays = 90);

    /// <summary>최근 N일 발생했지만 마스터에 없는 NgCode 목록 (빈도순)</summary>
    Task<List<UnregisteredNgCodeDto>> GetUnregisteredAsync(int days = 30);
}
