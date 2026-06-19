using System.ComponentModel.DataAnnotations;

namespace BODA.VMS.Web.Data.Entities;

/// <summary>
/// 입고(적치) 확정 이력. 글라스에서 '입고 확정' 1회 = 1행.
/// PoC 감사 로그 — 운영 시 고객 ERP/WMS 입고 트랜잭션으로 대체.
/// </summary>
public class InboundReceipt
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string Barcode { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ItemCode { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;

    /// <summary>"Zone-Rack-Level-Bin" 위치 문자열(확정 시점 스냅샷).</summary>
    [MaxLength(80)]
    public string LocationText { get; set; } = string.Empty;

    /// <summary>이번 확정 수량.</summary>
    public int Qty { get; set; }

    /// <summary>확정 후 누적 재고.</summary>
    public int StockAfter { get; set; }

    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
}
