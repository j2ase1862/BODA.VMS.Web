using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// JWT 양성 시나리오 — 유효 토큰으로 보호된 endpoint 접근 200 / Role 미달시 Forbidden.
/// IntegrationTestFactory 가 JwtBearerOptions 의 IssuerSigningKey 를 테스트 키로
/// 명시 교체해 검증 통과 보장 (Program.cs AddJwtBearer 가 캡처한 키와의 시점 차이 회피).
/// </summary>
public class JwtPositiveScenarioTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public JwtPositiveScenarioTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> SeedAndLoginAsync(string username, string role)
    {
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = $"User {username}",
                Role = role,
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        var resp = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = username, Password = "secret12" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task GET_auth_me_with_valid_token_returns_user_info()
    {
        var token = await SeedAndLoginAsync("me_user", "User");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        resp.IsSuccessStatusCode.Should().BeTrue();
        var dto = await resp.Content.ReadFromJsonAsync<UserDto>();
        dto!.Username.Should().Be("me_user");
        dto.Role.Should().Be("User");
    }

    [Fact]
    public async Task GET_clients_with_authenticated_user_returns_200()
    {
        // RequireAuthorization() 만 있는 그룹 — User role 도 접근 가능
        var token = await SeedAndLoginAsync("clients_reader", "User");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/clients/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        resp.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task GET_admin_users_with_user_role_returns_403_Forbidden()
    {
        // /api/admin/* 는 RequireRole("Admin") — User role 은 403
        var token = await SeedAndLoginAsync("regular_user", "User");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "이유: 인증은 성공(401 아님)했지만 Admin role 없음 → 403");
    }

    [Fact]
    public async Task GET_admin_users_with_Admin_role_returns_200()
    {
        var token = await SeedAndLoginAsync("admin_user", "Admin");

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);

        resp.IsSuccessStatusCode.Should().BeTrue();
    }
}
