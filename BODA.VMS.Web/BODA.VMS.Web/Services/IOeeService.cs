using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IOeeService
{
    Task<OeeResultDto> CalculateAsync(OeeRequestDto req);
}
