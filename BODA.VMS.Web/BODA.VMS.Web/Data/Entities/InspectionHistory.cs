using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BODA.VMS.Web.Data.Entities;

public class InspectionHistory
{
    [Key]
    public int Id { get; set; }

    public int ClientId { get; set; }

    [ForeignKey(nameof(ClientId))]
    public VisionClient Client { get; set; } = null!;

    [MaxLength(100)]
    public string? RecipeName { get; set; }

    public bool IsPass { get; set; }

    [MaxLength(50)]
    public string? NgCode { get; set; }

    /// <summary>
    /// JSON-serialized tool measurement results (up to 7 tools).
    /// </summary>
    public string? ToolResults { get; set; }

    /// <summary>
    /// Relative path to the inspection image file (e.g. /images/2026-06-11/NG/{key}.jpg).
    /// VMS 이미지 업로드(/api/inspection-images)가 CorrelationKey 매칭으로 채운다.
    /// </summary>
    [MaxLength(500)]
    public string? ImagePath { get; set; }

    /// <summary>
    /// VMS 가 결과·이미지 두 업로드에 동일하게 싣는 상관 키
    /// ({yyyyMMddHHmmssfff}|{camera}|{step}|{verdict}). 이미지↔레코드 순서무관 매칭용.
    /// </summary>
    [MaxLength(80)]
    public string? CorrelationKey { get; set; }

    public DateTime InspectedAt { get; set; } = DateTime.UtcNow;

    // === 추적성(Traceability) 컬럼 — Phase 1 (4M 중 Material/Method/Man 식별) ===

    /// <summary>
    /// 검사 결과가 귀속되는 생산 오더. NULL이면 오더와 무관한 단발성 검사(점검 등).
    /// </summary>
    public int? WorkOrderId { get; set; }

    /// <summary>
    /// 검사 결과가 귀속되는 로트. NULL이면 로트 미할당.
    /// </summary>
    public int? LotId { get; set; }

    /// <summary>
    /// 작업자 식별자(Users.Id). VMS 단말에 로그인한 작업자.
    /// </summary>
    public int? OperatorId { get; set; }

    /// <summary>
    /// 개별 제품 시리얼/바코드 (예: SN-20260519-00123).
    /// </summary>
    [MaxLength(80)]
    public string? SerialNumber { get; set; }

    /// <summary>
    /// 교대조 식별자. InspectedAt 기준으로 자동 매칭.
    /// </summary>
    public int? ShiftId { get; set; }

    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public WorkOrder? WorkOrder { get; set; }

    [ForeignKey(nameof(LotId))]
    public Lot? Lot { get; set; }

    [ForeignKey(nameof(OperatorId))]
    public User? Operator { get; set; }

    // ─── Predictive_DefectRate_Plan §5.1 (V1/V2/V3) 예측 피처 ───
    // 모두 nullable — 구버전 VMS 클라이언트(필드 미전송)와의 후방호환.

    /// <summary>V2: 검사 1회 소요 시간(ms). 가동 페이스 둔화 ↔ 품질 저하 선행 신호.</summary>
    public int? CycleTimeMs { get; set; }

    /// <summary>V1: 이미지 평균 밝기(0~255).</summary>
    public double? Brightness { get; set; }

    /// <summary>V1: 이미지 명암 표준편차.</summary>
    public double? ContrastStd { get; set; }

    /// <summary>V1: Laplacian variance — 초점/선명도 점수.</summary>
    public double? FocusScore { get; set; }

    /// <summary>V1: blob 개수(Otsu 이진화 후 connected components).</summary>
    public int? BlobCount { get; set; }

    /// <summary>V1: 가장 큰 blob 면적(px).</summary>
    public double? MaxBlobAreaPx { get; set; }

    /// <summary>V3: DL 모델 신뢰도(0~1).</summary>
    public double? DlConfidence { get; set; }

    /// <summary>V3: 사용된 DL 모델 버전(피처 분포 변화 추적용).</summary>
    [MaxLength(50)]
    public string? DlModelVersion { get; set; }
}
