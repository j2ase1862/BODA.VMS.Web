using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 입고 위치 마스터 (스마트 글라스 입고 위치 조회 PoC).
/// 바코드 1건 → 창고 위치(Zone-Rack-Level-Bin) + 3D 좌표 매핑.
///
/// 데이터 출처(설계문서 docs/SmartGlass_InboundLocation_Design.md §8):
///  - PoC: BODA 소유 (부트스트랩 시드 / CSV 업로드). 수기 CRUD(B안)는 만들지 않음.
///  - 운영: 고객사 ERP 동기화로 승격 (이 엔티티/엔드포인트는 그대로 유지).
/// </summary>
public class WarehouseItem
{
    [Key]
    public int Id { get; set; }

    /// <summary>조회 키. 물류 라벨 바코드(1D/2D). 인덱스.</summary>
    [Required, MaxLength(64)]
    public string Barcode { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // === 창고 위치 (Zone-Rack-Level-Bin) ===
    [MaxLength(20)] public string? Zone { get; set; }
    [MaxLength(20)] public string? Rack { get; set; }
    [MaxLength(20)] public string? Level { get; set; }
    [MaxLength(20)] public string? Bin { get; set; }

    // === 2차 목표(3D 하이라이트)용 좌표 ===
    public double PosX { get; set; }
    public double PosY { get; set; }
    public double PosZ { get; set; }

    public bool IsActive { get; set; } = true;

    // === 입고(적치) 확정 — 글라스 '입고 확정' 누적. 운영 시 ERP/WMS 재고로 대체. ===
    /// <summary>현재 보관 재고 수량(입고 확정 누적).</summary>
    public int StockQty { get; set; }

    /// <summary>최근 입고 확정 시각.</summary>
    public DateTime? LastInboundAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
