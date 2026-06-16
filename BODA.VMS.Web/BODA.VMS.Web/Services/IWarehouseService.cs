using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IWarehouseService
{
    /// <summary>
    /// 바코드로 입고 위치 1건 조회. 등록되지 않았거나 비활성 품목이면 null.
    /// mode("입고"/"입고제품")는 현재 동일 조회 — 향후 분기 여지를 위해 받아둠.
    /// </summary>
    Task<InboundLocationDto?> GetInboundLocationAsync(string barcode, string? mode = null);

    // === 입고 위치 마스터 관리(등록/수정) — 관리 화면용 ===

    /// <summary>전체 목록. includeInactive=false면 활성 항목만.</summary>
    Task<List<WarehouseItemDto>> GetAllAsync(bool includeInactive = false);

    Task<WarehouseItemDto?> GetByIdAsync(int id);

    /// <summary>바코드로 신규 등록. 같은 바코드가 이미 있으면 <see cref="DuplicateBarcodeException"/>.</summary>
    Task<WarehouseItemDto> CreateAsync(WarehouseItemDto dto);

    /// <summary>수정. 대상 없으면 null. 바코드를 다른 항목과 충돌하게 바꾸면 <see cref="DuplicateBarcodeException"/>.</summary>
    Task<WarehouseItemDto?> UpdateAsync(int id, WarehouseItemDto dto);

    Task<bool> DeleteAsync(int id);
}

/// <summary>동일 바코드 중복 등록 시도. 엔드포인트에서 409 Conflict로 변환.</summary>
public class DuplicateBarcodeException(string barcode)
    : Exception($"바코드 '{barcode}'가 이미 등록되어 있습니다.")
{
    public string Barcode { get; } = barcode;
}
