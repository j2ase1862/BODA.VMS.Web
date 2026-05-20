using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IWorkOrderService
{
    Task<List<WorkOrderDto>> GetAllAsync(string? status = null, int? clientId = null);
    Task<WorkOrderDto?> GetByIdAsync(int id);
    Task<WorkOrderDto> CreateAsync(WorkOrderDto dto);
    Task<WorkOrderDto?> UpdateAsync(int id, WorkOrderDto dto);
    Task<bool> DeleteAsync(int id);

    /// <summary>상태 전이: Start (Planned→InProgress), Complete (InProgress→Completed), Close (Completed→Closed)</summary>
    Task<WorkOrderDto?> ChangeStatusAsync(int id, string action);

    /// <summary>WorkOrder 발행 시 사용할 다음 OrderNo 생성 (WO-YYYYMMDD-NNN)</summary>
    Task<string> GenerateNextOrderNoAsync();
}
