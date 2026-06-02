using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// /health 엔드포인트가 WebApplicationFactory 부팅 후 200 반환하는지 확인.
/// 다른 endpoint 의 integration 테스트도 이 패턴을 재사용 — Program.cs 의
/// 시작 검증(JWT Key)을 통과시키기 위해 in-memory config 로 dummy 값 주입.
/// </summary>
public class HealthEndpointTests : IClassFixture<HealthEndpointTests.TestWebAppFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(TestWebAppFactory factory)
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

    /// <summary>
    /// Program.cs 시작 검증(JWT Key 32자 이상)을 통과시키기 위해
    /// in-memory config 로 dummy 키 주입.
    /// </summary>
    public sealed class TestWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "TestSecretKey_AtLeast32CharsLong_ForIntegrationTesting_2026",
                    ["Jwt:Issuer"] = "BODA.VMS.Web",
                    ["Jwt:Audience"] = "BODA.VMS.Web.Client",
                    ["Jwt:ExpireMinutes"] = "480"
                });
            });
        }
    }
}
