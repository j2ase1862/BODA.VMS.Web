using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Services;

/// <summary>
/// 디스크 기반 검사 이미지 저장소. 루트는 옵션의 RootPath, 없으면 {DB 디렉토리}/images.
/// </summary>
public sealed class ImageStoreService : IImageStoreService
{
    private readonly ILogger<ImageStoreService> _logger;

    public string RootPath { get; }

    public ImageStoreService(
        IOptions<ImageStoreOptions> options,
        IConfiguration configuration,
        ILogger<ImageStoreService> logger)
    {
        _logger = logger;
        RootPath = ResolveRoot(options.Value, configuration);
        Directory.CreateDirectory(RootPath);
    }

    public async Task<string> SaveAsync(byte[] bytes, string correlationKey, string verdict,
        string ext, DateTime capturedAt, CancellationToken ct = default)
    {
        var dateFolder = capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var verdictFolder = string.Equals(verdict, "OK", StringComparison.OrdinalIgnoreCase) ? "OK" : "NG";
        var safeKey = Sanitize(correlationKey);
        var safeExt = Sanitize(ext).TrimStart('.');
        if (string.IsNullOrEmpty(safeExt)) safeExt = "jpg";

        var dir = Path.Combine(RootPath, dateFolder, verdictFolder);
        Directory.CreateDirectory(dir);
        var fileName = $"{safeKey}.{safeExt}";
        var fullPath = Path.Combine(dir, fileName);

        await File.WriteAllBytesAsync(fullPath, bytes, ct);

        // 정적 서빙 RequestPath(/images) 기준 상대 URL. 슬래시는 항상 '/'.
        return $"/images/{dateFolder}/{verdictFolder}/{fileName}";
    }

    public int CleanupExpired(int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(RootPath)) return 0;

        var cutoff = DateTime.Today.AddDays(-retentionDays);
        int removed = 0;
        try
        {
            foreach (var dir in Directory.GetDirectories(RootPath))
            {
                var name = Path.GetFileName(dir);
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var folderDate))
                {
                    continue; // 날짜 폴더가 아니면 보호
                }
                if (folderDate >= cutoff) continue;

                try { Directory.Delete(dir, recursive: true); removed++; }
                catch (Exception ex) { _logger.LogWarning(ex, "이미지 폴더 삭제 실패: {Dir}", dir); }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "이미지 보존 정리 오류");
        }
        return removed;
    }

    private static string ResolveRoot(ImageStoreOptions opts, IConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(opts.RootPath)) return opts.RootPath!;

        // 기본: DB 파일 디렉토리 옆 images/
        var conn = config.GetConnectionString("DefaultConnection") ?? string.Empty;
        var marker = "Data Source=";
        var idx = conn.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var dbPath = conn[(idx + marker.Length)..].Trim();
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dbDir)) return Path.Combine(dbDir, "images");
        }
        return Path.Combine(AppContext.BaseDirectory, "images");
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
