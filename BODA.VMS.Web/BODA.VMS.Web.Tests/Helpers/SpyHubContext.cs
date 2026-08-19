using Microsoft.AspNetCore.SignalR;

namespace BODA.VMS.Web.Tests.Helpers;

/// <summary>
/// IHubContext&lt;THub&gt; 스파이 — 브로드캐스트된 (메서드 이름, 인자)를 기록한다.
/// NoopHubContext 와 달리 "무엇이 전송됐는지"를 검증할 때 사용.
/// </summary>
public sealed class SpyHubContext<THub> : IHubContext<THub> where THub : Hub
{
    public List<(string Method, object?[] Args)> Sent { get; } = new();

    public IHubClients Clients { get; }
    public IGroupManager Groups { get; } = new NoopGroupManager();

    public SpyHubContext()
    {
        Clients = new SpyHubClients(Sent);
    }

    private sealed class SpyClientProxy : IClientProxy
    {
        private readonly List<(string, object?[])> _sent;
        public SpyClientProxy(List<(string, object?[])> sent) => _sent = sent;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _sent.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class SpyHubClients : IHubClients
    {
        private readonly IClientProxy _proxy;
        public SpyHubClients(List<(string, object?[])> sent) => _proxy = new SpyClientProxy(sent);

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
}
