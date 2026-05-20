using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface ISpcService
{
    /// <summary>Xbar-R 관리도 + Cp/Cpk 계산</summary>
    Task<SpcResultDto> ComputeAsync(SpcRequestDto request);
}
