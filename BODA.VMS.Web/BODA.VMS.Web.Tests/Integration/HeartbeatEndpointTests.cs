using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// POST /api/clients/heartbeat — 시드된 Client 의 LastSeenAt 갱신 검증.
/// X-API-Key 호환 모드(Required=false) 라 헤더 없이도 통과.
/// </summary>
public class HeartbeatEndpointTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public HeartbeatEndpointTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task POST_heartbeat_updates_LastSeenAt_for_existing_client()
    {
        // 시드 — ClientIndex 50 (다른 테스트와 중첩 방지)
        int clientId;
        using (var db = _factory.CreateScopedDbContext())
        {
            var c = new VisionClient
            {
                ClientIndex = 50,
                Name = "Test Line 50",
                IpAddress = "192.168.50.1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Clients.Add(c);
            await db.SaveChangesAsync();
            clientId = c.Id;
        }

        var beforeHeartbeat = DateTime.UtcNow;
        var response = await _client.PostAsJsonAsync(
            "/api/clients/heartbeat",
            new { ClientIndex = 50, HostName = "TEST-HOST", SwName = "TestVMS" });

        response.IsSuccessStatusCode.Should().BeTrue();

        using var verifyDb = _factory.CreateScopedDbContext();
        var updated = verifyDb.Clients.Single(c => c.Id == clientId);
        updated.LastSeenAt.Should().NotBeNull();
        updated.LastSeenAt!.Value.Should().BeOnOrAfter(beforeHeartbeat);
        updated.HostName.Should().Be("TEST-HOST");
        updated.SwName.Should().Be("TestVMS");
    }

    [Fact]
    public async Task POST_heartbeat_for_unknown_client_returns_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/clients/heartbeat",
            new { ClientIndex = 99 });  // 미존재
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_register_creates_client_then_idempotent_on_duplicate()
    {
        // 첫 호출 — 신규 생성
        var r1 = await _client.PostAsJsonAsync(
            "/api/clients/register",
            new { ClientIndex = 51, Name = "Line 51", IpAddress = "192.168.51.1" });
        r1.IsSuccessStatusCode.Should().BeTrue();

        // 두번째 호출 — 동일 ClientIndex, 다른 Name → 멱등 (기존 반환)
        var r2 = await _client.PostAsJsonAsync(
            "/api/clients/register",
            new { ClientIndex = 51, Name = "Different Name", IpAddress = "10.0.0.1" });
        r2.IsSuccessStatusCode.Should().BeTrue();

        using var db = _factory.CreateScopedDbContext();
        var clients = db.Clients.Where(c => c.ClientIndex == 51).ToList();
        clients.Should().HaveCount(1, "이유: 같은 ClientIndex 재요청은 멱등 — 1 row 만 존재");
        clients[0].Name.Should().Be("Line 51", "이유: 멱등이라 두번째 호출의 새 값으로 update 하지 않음");
    }

    [Fact]
    public async Task POST_disconnect_clears_LastSeenAt()
    {
        // 시드: heartbeat 받은 적 있는 Client
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Clients.Add(new VisionClient
            {
                ClientIndex = 52,
                Name = "Line 52",
                IpAddress = "192.168.52.1",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow.AddSeconds(-3)
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/clients/disconnect",
            new { ClientIndex = 52 });
        response.IsSuccessStatusCode.Should().BeTrue();

        using var verifyDb = _factory.CreateScopedDbContext();
        var c = verifyDb.Clients.Single(x => x.ClientIndex == 52);
        c.LastSeenAt.Should().BeNull("이유: disconnect 는 LastSeenAt 을 즉시 null 로 — offline 표시");
    }
}
