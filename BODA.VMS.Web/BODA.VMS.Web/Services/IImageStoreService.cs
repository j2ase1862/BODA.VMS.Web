namespace BODA.VMS.Web.Services;

/// <summary>
/// 검사 이미지 파일 저장소 — 디스크 기반. {Root}/{yyyy-MM-dd}/{verdict}/{key}.{ext}.
/// 서빙은 /images 정적 파일 매핑(Program.cs).
/// </summary>
public interface IImageStoreService
{
    /// <summary>이미지 저장 루트 절대 경로(정적 서빙 FileProvider 와 공유).</summary>
    string RootPath { get; }

    /// <summary>
    /// 바이트를 저장하고 서빙용 상대 URL(/images/...)을 반환.
    /// </summary>
    Task<string> SaveAsync(byte[] bytes, string correlationKey, string verdict,
        string ext, DateTime capturedAt, CancellationToken ct = default);

    /// <summary>보존 기간을 넘긴 날짜 폴더 삭제. 삭제한 폴더 수 반환. (0 이하 보존이면 no-op)</summary>
    int CleanupExpired(int retentionDays);
}
