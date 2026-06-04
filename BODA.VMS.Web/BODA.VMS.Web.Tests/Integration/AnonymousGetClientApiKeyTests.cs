using System.Net;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// VMS 머신용 익명 GET endpoint 들이 X-API-Key 강제 모드에서 차단/통과되는지 검증.
/// 대상 5 endpoint:
/// - GET /api/parameters/sync/recipes/{clientIndex}
/// - GET /api/parameters/sync/recipe/{recipeId}
/// - GET /api/lots/by-number/{lotNumber}
/// - GET /api/lots/active-by-workorder/{workOrderId}
/// - GET /api/workorders/by-client/{clientIndex}
/// - GET /api/predictions/current/{clientIndex}
///
/// 호환 모드 (Required=false) 에서는 헤더 없어도 통과 — 기존 VMS 클라이언트 호환 보장.
/// </summary>
public class AnonymousGetClientApiKeyTests
{
    private const string ValidKey = "TestApiKeySecret_AtLeast20Chars";

    public sealed class EnforceModeFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["ClientApiKey:Value"] = ValidKey,
            ["ClientApiKey:Required"] = "true"
        };
    }

    public sealed class CompatModeFactory : IntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["ClientApiKey:Required"] = "false"
        };
    }

    public class EnforceMode : IClassFixture<EnforceModeFactory>
    {
        private readonly HttpClient _client;
        public EnforceMode(EnforceModeFactory f) => _client = f.CreateClient();

        [Theory]
        [InlineData("/api/parameters/sync/recipes/0")]
        [InlineData("/api/parameters/sync/recipe/1")]
        [InlineData("/api/lots/by-number/LOT-DUMMY")]
        [InlineData("/api/lots/active-by-workorder/1")]
        [InlineData("/api/workorders/by-client/0")]
        [InlineData("/api/predictions/current/0")]
        public async Task GET_anonymous_endpoint_without_header_returns_401(string url)
        {
            var resp = await _client.GetAsync(url);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"이유: Required=true 면 {url} 도 X-API-Key 헤더 없으면 차단");
        }

        [Theory]
        [InlineData("/api/parameters/sync/recipes/0")]
        [InlineData("/api/lots/by-number/LOT-DUMMY")]
        [InlineData("/api/workorders/by-client/0")]
        public async Task GET_anonymous_endpoint_with_wrong_header_returns_401(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-API-Key", "WRONG_KEY");

            var resp = await _client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [InlineData("/api/parameters/sync/recipes/0")]
        [InlineData("/api/lots/by-number/LOT-DUMMY")]
        [InlineData("/api/workorders/by-client/0")]
        public async Task GET_anonymous_endpoint_with_correct_header_passes_filter(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-API-Key", ValidKey);

            var resp = await _client.SendAsync(req);
            // 정확한 키 → filter 통과. endpoint 자체 응답은 200/404 등 — 401 만 아니면 OK
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        }
    }

    public class CompatMode : IClassFixture<CompatModeFactory>
    {
        private readonly HttpClient _client;
        public CompatMode(CompatModeFactory f) => _client = f.CreateClient();

        [Theory]
        [InlineData("/api/parameters/sync/recipes/0")]
        [InlineData("/api/parameters/sync/recipe/1")]
        [InlineData("/api/lots/by-number/LOT-DUMMY")]
        [InlineData("/api/lots/active-by-workorder/1")]
        [InlineData("/api/workorders/by-client/0")]
        [InlineData("/api/predictions/current/0")]
        public async Task GET_anonymous_endpoint_without_header_passes_in_compat_mode(string url)
        {
            // 호환 모드 — 기존 VMS 클라이언트가 헤더 없이 호출해도 401 안남
            var resp = await _client.GetAsync(url);
            resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
                $"이유: Required=false 면 {url} 호출 시 X-API-Key 없어도 통과");
        }
    }
}
