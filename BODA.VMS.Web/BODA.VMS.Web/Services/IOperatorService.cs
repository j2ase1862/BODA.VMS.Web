using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IOperatorService
{
    Task<List<OperatorDto>> GetAllAsync(bool includeInactive = false);
    Task<OperatorDto?> GetByIdAsync(int id);
    Task<OperatorDto?> GetByEmployeeNumberAsync(string employeeNumber);
    Task<OperatorDto> CreateAsync(OperatorUpsertDto dto);
    Task<OperatorDto?> UpdateAsync(int id, OperatorUpsertDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>사번 + PIN으로 인증. 성공 시 OperatorDto, 실패 시 null.</summary>
    Task<OperatorDto?> AuthenticateAsync(string employeeNumber, string pin);
}
