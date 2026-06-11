using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Services;

/// <summary>
/// 검사 이미지 보존 정리 — 주기적으로 RetentionDays 를 넘긴 날짜 폴더를 삭제.
/// RetentionDays 0 이하면 비활성. DatabaseBackupService 와 동일한 hosted-service 패턴.
/// </summary>
public sealed class ImageRetentionBackgroundService : BackgroundService
{
    private readonly IImageStoreService _store;
    private readonly ImageStoreOptions _options;
    private readonly ILogger<ImageRetentionBackgroundService> _logger;

    public ImageRetentionBackgroundService(
        IImageStoreService store,
        IOptions<ImageStoreOptions> options,
        ILogger<ImageRetentionBackgroundService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            _logger.LogInformation("[ImageRetention] RetentionDays<=0 — 이미지 자동 정리 비활성");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.CleanupIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var removed = _store.CleanupExpired(_options.RetentionDays);
                if (removed > 0)
                    _logger.LogInformation("[ImageRetention] 오래된 이미지 폴더 {Count}개 삭제", removed);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImageRetention] 정리 실패");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }
}
