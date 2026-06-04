namespace BODA.VMS.Web.Services;

/// <summary>
/// DatabaseBackupService 옵션. appsettings 의 "DatabaseBackup" 섹션에 매핑.
/// </summary>
public sealed class DatabaseBackupOptions
{
    public const string SectionName = "DatabaseBackup";

    /// <summary>false 면 hosted service 가 시작 즉시 종료 (DI 등록은 유지).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>백업 간격 (분). 기본 24 시간.</summary>
    public int IntervalMinutes { get; set; } = 24 * 60;

    /// <summary>
    /// 시작 직후 즉시 1 회 백업할지 여부. false 면 IntervalMinutes 만큼 대기 후 첫 백업.
    /// 운영 권장: false (시작 직후 백업은 직전 강제 종료시 동일 상태를 또 백업하는 낭비).
    /// </summary>
    public bool BackupOnStartup { get; set; }

    /// <summary>
    /// 백업 저장 디렉토리 절대 경로. 비어있으면 {DbDirectory}/backups 사용.
    /// 운영에서는 별도 디스크/네트워크 드라이브 경로 권장.
    /// </summary>
    public string? Destination { get; set; }

    /// <summary>보관할 백업 파일 최대 개수. 초과시 오래된 것부터 자동 삭제.</summary>
    public int RetainCount { get; set; } = 14;
}
