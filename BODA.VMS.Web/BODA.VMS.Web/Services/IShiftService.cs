using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IShiftService
{
    Task<List<ShiftDto>> GetAllAsync(bool includeInactive = false);
    Task<ShiftDto?> GetByIdAsync(int id);
    Task<ShiftDto> CreateAsync(ShiftDto dto);
    Task<ShiftDto?> UpdateAsync(int id, ShiftDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>주어진 시각이 속하는 활성 Shift Id (없으면 null)</summary>
    Task<int?> ResolveShiftIdAsync(DateTime time);

    /// <summary>교대조별 생산 리포트 (날짜 × Shift)</summary>
    Task<List<ShiftReportEntryDto>> GetReportAsync(ShiftReportRequestDto req);
}
