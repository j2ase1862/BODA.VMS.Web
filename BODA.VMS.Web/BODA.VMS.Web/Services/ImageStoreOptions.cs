namespace BODA.VMS.Web.Services;

/// <summary>
/// 검사 이미지 저장소 옵션. appsettings 의 "ImageStore" 섹션에 매핑.
/// </summary>
public sealed class ImageStoreOptions
{
    public const string SectionName = "ImageStore";

    /// <summary>이미지 저장 루트 디렉토리. 비어있으면 {DbDirectory}/images 사용.</summary>
    public string? RootPath { get; set; }

    /// <summary>보존 기간(일). 0 이하면 자동 삭제 안 함.</summary>
    public int RetentionDays { get; set; }

    /// <summary>정리 주기(분). 기본 6시간.</summary>
    public int CleanupIntervalMinutes { get; set; } = 360;
}
