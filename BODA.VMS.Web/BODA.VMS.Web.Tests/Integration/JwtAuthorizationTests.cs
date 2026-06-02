using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// JWT Bearer 인증 미들웨어가 RequireAuthorization endpoint 를 보호하는지 검증.
/// </summary>
public class JwtAuthorizationTests : IClassFixture<IntegrationTestFactory>
{
    private readonly HttpClient _client;

    public JwtAuthorizationTests(IntegrationTestFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_auth_me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_auth_me_with_invalid_token_returns_401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.jwt.token");

        var response = await _client.SendAsync(req);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_admin_without_token_returns_401()
    {
        // Admin endpoint 도 보호
        var response = await _client.GetAsync("/api/admin/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_clients_without_token_returns_401()
    {
        // RequireAuthorization 그룹 endpoint
        var response = await _client.GetAsync("/api/clients/");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
