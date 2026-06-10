using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// GS 보안 — 키오스크 그룹(/api/kiosk)에 부착한 ClientApiKeyEndpointFilter 검증.
/// VMS 머신 endpoint(§5.1)와의 비대칭 해소: 호환 모드는 헤더 없이 통과(현행 UX 보존),
/// 강제 모드에서는 머신과 동일하게 X-API-Key 를 요구.
/// </summary>
public class KioskClientApiKeyTests
{
    private const string ValidKey = "TestApiKeySecret_AtLeast20Chars";

    public sealed class CompatModeFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["ClientApiKey:Required"] = "false"
        };
    }

    public sealed class EnforceModeFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["ClientApiKey:Value"] = ValidKey,
            ["ClientApiKey:Required"] = "true"
        };
    }

    public class CompatMode : IClassFixture<CompatModeFactory>
    {
        private readonly HttpClient _client;
        public CompatMode(CompatModeFactory f) => _client = f.CreateClient();

        [Fact]
        public async Task Kiosk_login_without_header_passes_filter()
        {
            // 호환 모드 — 헤더 없어도 통과. ClientIndex=999 라인 없어 404 (401 아님)
            var response = await _client.PostAsJsonAsync(
                "/api/kiosk/login",
                new KioskLoginRequest { ClientIndex = 999, EmployeeNumber = "E001", Pin = "1234" });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound,
                "이유: Required=false 면 헤더 없어도 filter 통과");
        }

        [Fact]
        public async Task Kiosk_line_meta_without_header_passes_filter()
        {
            var response = await _client.GetAsync("/api/kiosk/line/999");
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
    }

    public class EnforceMode : IClassFixture<EnforceModeFactory>
    {
        private readonly HttpClient _client;
        public EnforceMode(EnforceModeFactory f) => _client = f.CreateClient();

        [Fact]
        public async Task Kiosk_login_without_header_returns_401()
        {
            var response = await _client.PostAsJsonAsync(
                "/api/kiosk/login",
                new KioskLoginRequest { ClientIndex = 999, EmployeeNumber = "E001", Pin = "1234" });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Kiosk_login_with_wrong_header_returns_401()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/kiosk/login")
            {
                Content = JsonContent.Create(
                    new KioskLoginRequest { ClientIndex = 999, EmployeeNumber = "E001", Pin = "1234" })
            };
            req.Headers.Add("X-API-Key", "WRONG_KEY");

            var response = await _client.SendAsync(req);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Kiosk_login_with_correct_header_passes_filter()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/kiosk/login")
            {
                Content = JsonContent.Create(
                    new KioskLoginRequest { ClientIndex = 999, EmployeeNumber = "E001", Pin = "1234" })
            };
            req.Headers.Add("X-API-Key", ValidKey);

            var response = await _client.SendAsync(req);
            // 정확한 키 → filter 통과. 라인 999 없어 NotFound (401 아님)
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
