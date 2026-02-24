using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace BODA.VMS.Web.Client.Services;

public class SignalRService : IAsyncDisposable
{
    private HubConnection? _hub;
    private readonly NavigationManager _navigation;
    private readonly AuthStateProvider _authProvider;

    public event Action<int, bool>? OnClientStatusChanged;
    public event Action<string>? OnNgAlert;
    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public SignalRService(NavigationManager navigation, AuthStateProvider authProvider)
    {
        _navigation = navigation;
        _authProvider = authProvider;
    }

    public async Task StartAsync()
    {
        if (_hub is not null) return;

        var token = await _authProvider.GetTokenAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl(_navigation.ToAbsoluteUri("/hubs/vms"), options =>
            {
                if (!string.IsNullOrEmpty(token))
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<int, bool>("ClientStatusChanged", (clientId, isAlive) =>
            OnClientStatusChanged?.Invoke(clientId, isAlive));

        _hub.On<string>("NgAlert", (message) =>
            OnNgAlert?.Invoke(message));

        try
        {
            await _hub.StartAsync();
        }
        catch
        {
            // Hub not available yet — will retry on reconnect
        }
    }

    public async Task StopAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
