using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// GS 보안 — /api/auth/refresh + /api/auth/logout full HTTP path 검증.
/// 로그인 → refresh token 으로 access token 갱신 → 로그아웃(폐기) → 재갱신 거부.
/// </summary>
public class AuthRefreshEndpointTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public AuthRefreshEndpointTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<LoginResponse> SeedAndLoginAsync(string username = "refresh_user")
    {
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = $"User {username}",
                Role = "User",
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = username, Password = "secret12" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Login_returns_refresh_token()
    {
        var login = await SeedAndLoginAsync();
        login.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Refresh_with_valid_token_returns_new_pair()
    {
        var login = await SeedAndLoginAsync("refresh_ok");

        var resp = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest { RefreshToken = login.RefreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        refreshed!.Token.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
    }

    [Fact]
    public async Task Refresh_with_invalid_token_returns_401()
    {
        // 사용자 없이도 익명 endpoint — 무효 토큰은 401
        var resp = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest { RefreshToken = "bogus-token-value" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_token_so_refresh_is_rejected()
    {
        var login = await SeedAndLoginAsync("logout_user");

        var logout = await _client.PostAsJsonAsync(
            "/api/auth/logout", new RefreshRequest { RefreshToken = login.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        // 폐기 후 갱신 시도 → 401
        var refresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new RefreshRequest { RefreshToken = login.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
