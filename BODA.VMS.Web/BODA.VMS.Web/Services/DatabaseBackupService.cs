using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Services;

/// <summary>
/// SQLite DB 의 일관성 있는 온라인 백업을 주기적으로 수행.
/// SqliteConnection.BackupDatabase API 사용 — write lock 잠시만 잡고 페이지 단위 복사라
/// 운영중 무중단 백업 가능. 백업 파일명: boda-vision-{yyyyMMdd-HHmmss}.db.
/// RetainCount 초과시 오래된 파일부터 자동 삭제.
/// </summary>
public sealed class DatabaseBackupService : BackgroundService
{
    private readonly IOptions<DatabaseBackupOptions> _options;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(
        IOptions<DatabaseBackupOptions> options,
        IConfiguration config,
        ILogger<DatabaseBackupService> logger)
    {
        _options = options;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("DatabaseBackup disabled — service idle.");
            return;
        }

        var sourceDbPath = TryResolveSourceDbPath();
        if (sourceDbPath is null)
        {
            _logger.LogWarning(
                "DatabaseBackup: ConnectionStrings:DefaultConnection 에서 Data Source 를 찾지 못함. 비활성화.");
            return;
        }

        var destination = ResolveDestination(opts, sourceDbPath);
        Directory.CreateDirectory(destination);

        _logger.LogInformation(
            "DatabaseBackup started. Source={Source} Destination={Destination} IntervalMinutes={Interval} Retain={Retain}",
            sourceDbPath, destination, opts.IntervalMinutes, opts.RetainCount);

        if (opts.BackupOnStartup)
        {
            await SafeBackupAsync(sourceDbPath, destination, opts.RetainCount, stoppingToken);
        }

        var delay = TimeSpan.FromMinutes(Math.Max(opts.IntervalMinutes, 1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

            await SafeBackupAsync(sourceDbPath, destination, opts.RetainCount, stoppingToken);
        }
    }

    private async Task SafeBackupAsync(string sourceDbPath, string destination, int retain, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            var path = await PerformBackupAsync(sourceDbPath, destination, ct);
            _logger.LogInformation("DatabaseBackup ok — File={File} Size={Size}", path, new FileInfo(path).Length);
            PruneOldBackups(destination, retain);
        }
        catch (Exception ex)
        {
            // 백업 실패는 운영 신호 — 다음 주기에 다시 시도
            _logger.LogError(ex, "DatabaseBackup failed.");
        }
    }

    /// <summary>온라인 백업 + 파일 경로 반환. 외부에서도 테스트 호출 가능하도록 static.</summary>
    public static async Task<string> PerformBackupAsync(
        string sourceDbPath, string destination, CancellationToken ct = default)
    {
        if (!File.Exists(sourceDbPath))
            throw new FileNotFoundException("Source DB not found.", sourceDbPath);

        Directory.CreateDirectory(destination);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destPath = Path.Combine(destination, $"boda-vision-{stamp}.db");

        await using var source = new SqliteConnection($"Data Source={sourceDbPath}");
        await source.OpenAsync(ct);
        await using var dest = new SqliteConnection($"Data Source={destPath}");
        await dest.OpenAsync(ct);

        // BackupDatabase 는 페이지 단위 복사 — 운영중 안전
        source.BackupDatabase(dest);
        return destPath;
    }

    /// <summary>오래된 파일부터 RetainCount 만 남기고 삭제. 패턴 외 파일은 건드리지 않음.</summary>
    public static int PruneOldBackups(string destination, int retainCount)
    {
        if (retainCount <= 0) return 0;
        if (!Directory.Exists(destination)) return 0;

        var files = new DirectoryInfo(destination)
            .GetFiles("boda-vision-*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var toDelete = files.Skip(retainCount).ToList();
        foreach (var f in toDelete)
        {
            try { f.Delete(); }
            catch { /* lock 등 일시 실패는 다음 주기에 재시도 */ }
        }
        return toDelete.Count;
    }

    private string? TryResolveSourceDbPath()
    {
        var conn = _config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(conn)) return null;
        try
        {
            var builder = new SqliteConnectionStringBuilder(conn);
            return builder.DataSource;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveDestination(DatabaseBackupOptions opts, string sourceDbPath)
    {
        if (!string.IsNullOrWhiteSpace(opts.Destination))
            return opts.Destination;

        var dbDir = Path.GetDirectoryName(sourceDbPath);
        return string.IsNullOrWhiteSpace(dbDir)
            ? Path.Combine(Directory.GetCurrentDirectory(), "backups")
            : Path.Combine(dbDir, "backups");
    }
}
