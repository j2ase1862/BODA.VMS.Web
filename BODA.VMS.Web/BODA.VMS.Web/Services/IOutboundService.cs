using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IOutboundService
{
    // === 글라스 피킹 (읽기 가이드) ===

    /// <summary>글라스 출고 목록 — 기본은 활성(미완료) 주문, status 지정 시 해당 상태만. 경량 요약.</summary>
    Task<List<GlassOrderDto>> GetGlassOrdersAsync(string? status = null);

    /// <summary>주문번호로 피킹 목록 조회(피킹 위치는 WarehouseItem에서 조인). 없으면 null.</summary>
    Task<PickListDto?> GetPickListAsync(string orderNo);

    /// <summary>
    /// 스캔 검증 피킹 확정(2차). 스캔한 바코드가 주문의 미완료 라인과 일치하면 pickedQty 증가,
    /// 주문 상태를 Picking 으로. 주문이 없으면 null.
    /// </summary>
    Task<PickConfirmResult?> ConfirmPickAsync(string orderNo, string barcode, int qty = 1);

    /// <summary>출하 확정(2차) — 주문 상태를 Done 으로. 갱신된 피킹 목록 반환. 주문 없으면 null.</summary>
    Task<PickListDto?> ConfirmShipAsync(string orderNo);

    // === 출고 오더 관리(등록/수정) ===

    Task<List<OutboundOrderDto>> GetAllAsync();
    Task<OutboundOrderDto?> GetByIdAsync(int id);

    /// <summary>신규 등록. 같은 OrderNo가 있으면 <see cref="DuplicateOrderNoException"/>.</summary>
    Task<OutboundOrderDto> CreateAsync(OutboundOrderDto dto);

    /// <summary>수정(헤더 + 라인 전체 교체). 대상 없으면 null. OrderNo 충돌 시 예외.</summary>
    Task<OutboundOrderDto?> UpdateAsync(int id, OutboundOrderDto dto);

    Task<bool> DeleteAsync(int id);
}

/// <summary>출고 주문번호 중복. 엔드포인트에서 409 Conflict로 변환.</summary>
public class DuplicateOrderNoException(string orderNo)
    : Exception($"주문번호 '{orderNo}'가 이미 존재합니다.")
{
    public string OrderNo { get; } = orderNo;
}
