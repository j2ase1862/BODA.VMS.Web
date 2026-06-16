using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// 입고 위치 마스터 관리(등록/수정)용 DTO. 글라스 조회 응답(<see cref="InboundLocationDto"/>)과 별개.
/// WarehouseItem 엔티티와 1:1. (스마트 글라스 입고 위치 등록 화면)
/// </summary>
public class WarehouseItemDto
{
    public int Id { get; set; }

    /// <summary>조회 키 — 물류 라벨 바코드(1D/2D).</summary>
    [Required(ErrorMessage = "바코드는 필수입니다.")]
    [MaxLength(64)]
    public string Barcode { get; set; } = string.Empty;

    [Required(ErrorMessage = "품목 코드는 필수입니다.")]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "품목명은 필수입니다.")]
    [MaxLength(200)]
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

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Zone-Rack-Level-Bin 조합 (표시용, 읽기 전용). 글라스 응답의 LocationText와 동일 규칙.</summary>
    public string LocationText =>
        string.Join("-", new[] { Zone, Rack, Level, Bin }.Where(s => !string.IsNullOrEmpty(s)));
}
