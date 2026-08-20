using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface ILotService
{
    Task<List<LotDto>> GetByWorkOrderAsync(int workOrderId);
    Task<LotDto?> GetByIdAsync(int id);
    Task<LotDto?> GetByLotNumberAsync(string lotNumber);

    /// <summary>WO 의 현재 활성(Open) Lot 1개 — VMS 가 검사 시작 시 자동 채움. 없으면 null.
    /// (멀티 Lot 병행 운용에서는 GetOpenByWorkOrderAsync 목록을 사용 — B1 하위 호환용 유지)</summary>
    Task<LotDto?> GetActiveByWorkOrderAsync(int workOrderId);

    /// <summary>WO 의 Open Lot 전체 목록 (Sequence 오름차순) — VMS 멀티 Lot 콤보용. 없으면 빈 목록.</summary>
    Task<List<LotDto>> GetOpenByWorkOrderAsync(int workOrderId);

    /// <summary>새 Lot 생성 — LotNumber 자동 채번 ({YYYYMMDD}-{OrderNo}-{Seq:D3})</summary>
    Task<LotDto> CreateAsync(int workOrderId, string? note);

    /// <summary>Lot 마감 — 더 이상 검사 결과를 받지 않음</summary>
    Task<LotDto?> CloseAsync(int id);
}
