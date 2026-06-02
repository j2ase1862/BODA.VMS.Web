using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// SecurityHeadersMiddleware 가 모든 응답에 baseline 보안 헤더를 적용하는지 검증.
/// GS 인증 보안성 항목 — clickjacking / MIME 스니핑 / referrer 노출 방어.
/// </summary>
public class SecurityHeadersTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public SecurityHeadersTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task All_baseline_security_headers_present()
    {
        var response = await _client.GetAsync("/health");
        var h = response.Headers;

        h.Should().ContainKey("X-Frame-Options");
        h.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("SAMEORIGIN");

        h.Should().ContainKey("X-Content-Type-Options");
        h.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");

        h.Should().ContainKey("Referrer-Policy");
        h.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("strict-origin-when-cross-origin");

        h.Should().ContainKey("Permissions-Policy");
        h.GetValues("Permissions-Policy").Single()
            .Should().Contain("camera=()").And.Contain("microphone=()")
            .And.Contain("geolocation=()").And.Contain("payment=()");
    }

    [Fact]
    public async Task Security_headers_present_on_404_too()
    {
        // 미들웨어가 파이프라인 초기에 헤더 적용 → 응답 코드 무관하게 항상 존재
        var response = await _client.GetAsync("/api/nonexistent-endpoint");
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.Should().ContainKey("X-Content-Type-Options");
    }
}
