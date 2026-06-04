using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// SecurityHeaders:ContentSecurityPolicy 환경변수로 CSP override 가능한지 검증.
/// 운영에서 추가 도메인 (CDN/외부 API) 화이트리스트가 필요할 때 코드 변경 없이 적용.
/// </summary>
public class ContentSecurityPolicyOverrideTests : IClassFixture<ContentSecurityPolicyOverrideTests.CustomCspFactory>
{
    public const string CustomCsp = "default-src 'self' https://cdn.example.com; script-src 'self' 'wasm-unsafe-eval'";

    public sealed class CustomCspFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["SecurityHeaders:ContentSecurityPolicy"] = CustomCsp
        };
    }

    private readonly HttpClient _client;
    public ContentSecurityPolicyOverrideTests(CustomCspFactory f) => _client = f.CreateClient();

    [Fact]
    public async Task Custom_csp_from_config_overrides_default()
    {
        var resp = await _client.GetAsync("/health");
        resp.Headers.GetValues("Content-Security-Policy").Single().Should().Be(CustomCsp);
    }
}
