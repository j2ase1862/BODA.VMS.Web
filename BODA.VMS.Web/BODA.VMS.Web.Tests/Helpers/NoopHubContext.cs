using Microsoft.AspNetCore.SignalR;

namespace BODA.VMS.Web.Tests.Helpers;

/// <summary>
/// IHubContext&lt;THub&gt; 의 최소 noop 구현 — 테스트에서 SignalR 브로드캐스트를
/// 삼키기 위한 용도. 운영 코드의 broadcast 호출이 단순히 성공으로 끝남.
/// 호출된 method 이름과 인자를 추적해야 한다면 별도 spy 를 만들 것.
/// </summary>
public sealed class NoopHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public IHubClients Clients { get; } = new NoopHubClients();
    public IGroupManager Groups { get; } = new NoopGroupManager();
}

internal sealed class NoopClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class NoopHubClients : IHubClients
{
    private static readonly IClientProxy _proxy = new NoopClientProxy();

    public IClientProxy All => _proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Client(string connectionId) => _proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
    public IClientProxy Group(string groupName) => _proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
    public IClientProxy User(string userId) => _proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
}

internal sealed class NoopGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
