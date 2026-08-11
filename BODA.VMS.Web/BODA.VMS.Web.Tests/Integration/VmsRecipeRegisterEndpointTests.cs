using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// POST /api/parameters/sync/recipes/{clientIndex} —
/// VisionSetup 이 로컬 생성 레시피를 Web 원장에 등록하는 익명(X-API-Key 계열) 엔드포인트.
/// 이름 기준 멱등: 동일 이름이 이미 있으면 그 ID 반환 (재시도·이름 연결 수렴).
/// </summary>
public class VmsRecipeRegisterEndpointTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public VmsRecipeRegisterEndpointTests(IntegrationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private sealed record RegisterResponse(int Id, string Name, bool Existed);

    private async Task<int> EnsureClientAsync(int clientIndex)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();
        var existing = db.Clients.FirstOrDefault(c => c.ClientIndex == clientIndex);
        if (existing != null) return existing.Id;

        // Clients.Id 는 스토어 자동 증가가 아님 — 명시 채번 (ClientService 와 동일)
        var nextId = (db.Clients.Max(c => (int?)c.Id) ?? 0) + 1;
        var client = new VisionClient
        {
            Id = nextId,
            Name = $"Line-{clientIndex}",
            IpAddress = "127.0.0.1",
            ClientIndex = clientIndex
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }

    [Fact]
    public async Task Register_creates_recipe_and_returns_id()
    {
        await EnsureClientAsync(7);

        var resp = await _client.PostAsJsonAsync("/api/parameters/sync/recipes/7",
            new { name = "VS-Recipe-A", description = "from VisionSetup" });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.Id.Should().BeGreaterThan(0);
        body.Name.Should().Be("VS-Recipe-A");
        body.Existed.Should().BeFalse();
    }

    [Fact]
    public async Task Register_same_name_is_idempotent()
    {
        await EnsureClientAsync(8);

        var first = await (await _client.PostAsJsonAsync("/api/parameters/sync/recipes/8",
            new { name = "VS-Recipe-B" })).Content.ReadFromJsonAsync<RegisterResponse>();

        var resp = await _client.PostAsJsonAsync("/api/parameters/sync/recipes/8",
            new { name = "VS-Recipe-B" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        second!.Id.Should().Be(first!.Id);
        second.Existed.Should().BeTrue();
    }

    [Fact]
    public async Task Register_unknown_client_returns_404()
    {
        var resp = await _client.PostAsJsonAsync("/api/parameters/sync/recipes/9999",
            new { name = "VS-Recipe-C" });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Register_empty_name_returns_400()
    {
        await EnsureClientAsync(9);
        var resp = await _client.PostAsJsonAsync("/api/parameters/sync/recipes/9",
            new { name = "  " });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
