using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IWarehouseService
{
    /// <summary>
    /// 바코드로 입고 위치 1건 조회. 등록되지 않았거나 비활성 품목이면 null.
    /// mode("입고"/"입고제품")는 현재 동일 조회 — 향후 분기 여지를 위해 받아둠.
    /// </summary>
    Task<InboundLocationDto?> GetInboundLocationAsync(string barcode, string? mode = null);
}
