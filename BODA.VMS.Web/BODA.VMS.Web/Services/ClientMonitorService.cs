using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Services;

public class ClientMonitorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<VmsHub> _hubContext;
    private readonly ILogger<ClientMonitorService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<int, bool> _lastStatus = new();

    /// <summary>최근 검사 결과를 Running으로 판정하는 시간 창 (초)</summary>
    private const int RunningWindowSeconds = 60;

    public ClientMonitorService(
        IServiceProvider serviceProvider,
        IHubContext<VmsHub> hubContext,
        ILogger<ClientMonitorService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeoutSeconds = _configuration.GetValue("ClientMonitor:HeartbeatTimeoutSeconds", 30);
        var checkIntervalSeconds = _configuration.GetValue("ClientMonitor:CheckIntervalSeconds", 10);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();
                var statusSvc = scope.ServiceProvider.GetRequiredService<IEquipmentStatusService>();
                var alarmSvc = scope.ServiceProvider.GetRequiredService<IAlarmService>();

                var clients = await db.Clients
                    .Where(c => c.IsActive)
                    .ToListAsync(stoppingToken);

                var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);
                var runningCutoff = DateTime.UtcNow.AddSeconds(-RunningWindowSeconds);

                // 최근 검사 활동이 있는 ClientId 집합 (배치 조회로 N+1 회피)
                var clientIds = clients.Select(c => c.Id).ToList();
                var recentlyActive = await db.InspectionHistories
                    .Where(h => clientIds.Contains(h.ClientId) && h.InspectedAt > runningCutoff)
                    .Select(h => h.ClientId)
                    .Distinct()
                    .ToListAsync(stoppingToken);
                var activeSet = new HashSet<int>(recentlyActive);

                foreach (var client in clients)
                {
                    var isAlive = client.LastSeenAt.HasValue && client.LastSeenAt.Value > cutoff;

                    // 상태 결정: Down / Running / Idle
                    var newStatus = isAlive
                        ? (activeSet.Contains(client.Id) ? EquipmentStatus.Running : EquipmentStatus.Idle)
                        : EquipmentStatus.Down;

                    await statusSvc.RecordTransitionAsync(client.Id, newStatus);

                    // online/offline 변경 통지 (기존 SignalR 이벤트 유지)
                    var hadStatus = _lastStatus.TryGetValue(client.Id, out var wasAlive);
                    if (!hadStatus || wasAlive != isAlive)
                    {
                        _lastStatus[client.Id] = isAlive;
                        await _hubContext.Clients.All.SendAsync(
                            "ClientStatusChanged", client.Id, isAlive, stoppingToken);

                        // Online → Offline 전이 시 알람 생성
                        if (hadStatus && !isAlive)
                        {
                            await alarmSvc.CreateAsync(new AlarmEvent
                            {
                                ClientId = client.Id,
                                AlarmType = AlarmEventType.Offline,
                                Severity = AlarmSeverity.Major,
                                Title = $"Client offline: {client.Name}",
                                Message = $"Client #{client.ClientIndex:D2} ({client.Name}) heartbeat timeout"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in client monitoring loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), stoppingToken);
        }
    }
}
