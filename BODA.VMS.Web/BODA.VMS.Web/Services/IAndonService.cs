using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IAndonService
{
    Task<AndonSnapshotDto> GetSnapshotAsync();
}
