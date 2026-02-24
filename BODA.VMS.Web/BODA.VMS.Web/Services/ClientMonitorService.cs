using BODA.VMS.Web.Data;
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

                var clients = await db.Clients
                    .Where(c => c.IsActive)
                    .ToListAsync(stoppingToken);

                var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSeconds);

                foreach (var client in clients)
                {
                    var isAlive = client.LastSeenAt.HasValue && client.LastSeenAt.Value > cutoff;

                    // Notify only on status change
                    var hadStatus = _lastStatus.TryGetValue(client.Id, out var wasAlive);
                    if (!hadStatus || wasAlive != isAlive)
                    {
                        _lastStatus[client.Id] = isAlive;
                        await _hubContext.Clients.All.SendAsync(
                            "ClientStatusChanged", client.Id, isAlive, stoppingToken);
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
