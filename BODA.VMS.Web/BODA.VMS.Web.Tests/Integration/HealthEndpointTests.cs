using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// /health 엔드포인트가 WebApplicationFactory 부팅 후 200 반환하는지 확인.
/// IntegrationTestFactory 공통 base 재사용.
/// </summary>
public class HealthEndpointTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_health_returns_200()
    {
        var response = await _client.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue(
            "이유: 헬스 체크 endpoint 는 익명 + DB 연결 정상이면 200 반환");
    }

    [Fact]
    public async Task GET_health_live_returns_200()
    {
        var response = await _client.GetAsync("/health/live");
        response.IsSuccessStatusCode.Should().BeTrue(
            "이유: liveness probe 는 어떤 check 도 실행하지 않고 프로세스 생존만 확인");
    }
}
