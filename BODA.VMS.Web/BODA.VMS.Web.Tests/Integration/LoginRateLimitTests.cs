using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// GS 보안 — 로그인 endpoint IP 단위 rate limiting (429) 검증.
/// TestServer 는 RemoteIpAddress 가 없어 모든 요청이 "unknown" 파티션 공유 —
/// 각 테스트가 전용 factory 를 생성해 윈도우 격리 (AuthEndpointTests 패턴).
/// </summary>
public class LoginRateLimitTests
{
    private sealed class RateLimitFactory : DbBackedIntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["LoginRateLimit:PermitLimit"] = "3",
            // 긴 윈도우 — 테스트 도중 윈도우가 넘어가 flake 되는 것 방지
            ["LoginRateLimit:WindowSeconds"] = "300"
        };
    }

    [Fact]
    public async Task Auth_login_over_limit_returns_429_problem_details()
    {
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient();
        var dto = new LoginRequest { Username = "nonexistent", Password = "whatever1" };

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/auth/login", dto);
            ok.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"attempt {i + 1} is within the permit limit");
        }

        var rejected = await client.PostAsJsonAsync("/api/auth/login", dto);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("Too Many Requests");
    }

    [Fact]
    public async Task Kiosk_login_over_limit_returns_429()
    {
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient();
        // 존재하지 않는 라인 — 404 응답이지만 rate limiter 는 endpoint 도달 전에 카운트
        var dto = new KioskLoginRequest { ClientIndex = 999, EmployeeNumber = "E001", Pin = "1234" };

        for (var i = 0; i < 3; i++)
        {
            var ok = await client.PostAsJsonAsync("/api/kiosk/login", dto);
            ok.StatusCode.Should().Be(HttpStatusCode.NotFound,
                $"attempt {i + 1} is within the permit limit");
        }

        var rejected = await client.PostAsJsonAsync("/api/kiosk/login", dto);
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Endpoints_without_policy_are_not_limited()
    {
        using var factory = new RateLimitFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/health/live");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
