using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// ClientApiKeyEndpointFilter 의 두 동작 모드 검증:
/// - 호환 모드(Required=false): 헤더 없어도 통과 (기존 VMS 클라이언트 호환)
/// - 강제 모드(Required=true): 헤더 없거나 불일치시 401
/// </summary>
public class ClientApiKeyFilterTests
{
    private const string ValidKey = "TestApiKeySecret_AtLeast20Chars";

    /// <summary>호환 모드 — Value 미설정, Required=false (기본값)</summary>
    public sealed class CompatModeFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["ClientApiKey:Required"] = "false"
        };
    }

    /// <summary>강제 모드 — Value 설정 + Required=true</summary>
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
        public async Task POST_heartbeat_without_header_passes_to_endpoint()
        {
            // 호환 모드라 헤더 없어도 filter 통과 — endpoint 가 NotFound (ClientIndex=0 DB 없음)
            var response = await _client.PostAsJsonAsync(
                "/api/clients/heartbeat",
                new { ClientIndex = 0 });

            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
                "이유: Required=false 면 헤더 없어도 통과");
        }
    }

    public class EnforceMode : IClassFixture<EnforceModeFactory>
    {
        private readonly HttpClient _client;
        public EnforceMode(EnforceModeFactory f) => _client = f.CreateClient();

        [Fact]
        public async Task POST_heartbeat_without_header_returns_401()
        {
            var response = await _client.PostAsJsonAsync(
                "/api/clients/heartbeat",
                new { ClientIndex = 0 });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task POST_heartbeat_with_wrong_header_returns_401()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/clients/heartbeat")
            {
                Content = JsonContent.Create(new { ClientIndex = 0 })
            };
            req.Headers.Add("X-API-Key", "WRONG_KEY");

            var response = await _client.SendAsync(req);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task POST_heartbeat_with_correct_header_passes_filter()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/clients/heartbeat")
            {
                Content = JsonContent.Create(new { ClientIndex = 0 })
            };
            req.Headers.Add("X-API-Key", ValidKey);

            var response = await _client.SendAsync(req);
            // 정확한 키 → filter 통과. endpoint 가 ClientIndex=0 못 찾아 NotFound
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
    }
}
