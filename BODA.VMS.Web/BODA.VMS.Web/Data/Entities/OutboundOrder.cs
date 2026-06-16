using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 출고 오더 헤더 (스마트 글라스 주문 기반 디렉티드 피킹 PoC).
/// 출고는 "제품"이 아니라 "오더"가 목적지를 정한다 — 목적지(도크/스테이징 레인)는 여기 헤더에.
///
/// 데이터 출처: PoC=BODA 소유(시드/관리 화면), 운영=고객사 ERP 판매오더 동기화로 승격.
/// </summary>
public class OutboundOrder
{
    [Key]
    public int Id { get; set; }

    /// <summary>주문/송장 번호. 글라스가 스캔하는 키. 고유.</summary>
    [Required, MaxLength(64)]
    public string OrderNo { get; set; } = string.Empty;

    [MaxLength(100)] public string? CustomerName { get; set; }
    [MaxLength(200)] public string? ShipTo { get; set; }

    /// <summary>물리적 출고처 — 도크도어 / 스테이징 레인 (피킹 완료 후 안내).</summary>
    [MaxLength(100)] public string? Destination { get; set; }

    /// <summary>Pending(대기) / Picking(피킹중) / Done(완료).</summary>
    [Required, MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
