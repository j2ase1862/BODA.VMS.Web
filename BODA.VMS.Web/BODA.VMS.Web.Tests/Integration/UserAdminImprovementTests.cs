using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// 사용자 관리 개선 3종 (2026-08-11):
/// ① 가입 신청 → 시스템 알람(Info) 생성 ② 역할 변경 API (마지막 Admin 강등 차단)
/// ③ 검사 기준값 Guest 비노출 (parameters 그룹 Admin/User 한정).
/// </summary>
public class UserAdminImprovementTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory = new();
    private readonly HttpClient _client;

    public UserAdminImprovementTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<int> SeedUserAsync(string username, string role, bool approved = true)
    {
        using var db = _factory.CreateScopedDbContext();
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Secret12!@"),
            DisplayName = username,
            Role = role,
            IsApproved = approved
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<string> LoginAsync(string username)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = username, Password = "Secret12!@" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
    }

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    // ── ① 가입 신청 알람 ──

    [Fact]
    public async Task Register_creates_system_alarm()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "newbie",
            Password = "Secret12!@",
            DisplayName = "신입"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = _factory.CreateScopedDbContext();
        var alarm = db.AlarmEvents.FirstOrDefault(a => a.Title.Contains("newbie"));
        alarm.Should().NotBeNull("이유: 가입 신청 시 Admin 인지용 시스템 알람이 생성돼야 함");
        alarm!.ClientId.Should().BeNull();
        alarm.Severity.Should().Be(AlarmSeverity.Info);
    }

    // ── ② 역할 변경 ──

    [Fact]
    public async Task ChangeRole_updates_role()
    {
        await SeedUserAsync("boss", "Admin");
        var targetId = await SeedUserAsync("worker", "User");
        var token = await LoginAsync("boss");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put,
            $"/api/admin/users/{targetId}/role", token, new { Role = "Guest" }));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var db = _factory.CreateScopedDbContext();
        db.Users.Single(u => u.Id == targetId).Role.Should().Be("Guest");
    }

    [Fact]
    public async Task ChangeRole_last_admin_demotion_blocked_with_409()
    {
        // 팩토리가 초기 'admin' 을 시드하므로 Admin 2명에서 시작 —
        // 한 명 강등(성공) 후 마지막 남은 'admin' 강등 시도가 차단돼야 한다
        var secondId = await SeedUserAsync("second_admin", "Admin");
        var token = await LoginAsync("second_admin");

        int builtinAdminId;
        using (var db = _factory.CreateScopedDbContext())
            builtinAdminId = db.Users.Single(u => u.Username == "admin").Id;

        var demoteSecond = await _client.SendAsync(Authed(HttpMethod.Put,
            $"/api/admin/users/{secondId}/role", token, new { Role = "User" }));
        demoteSecond.StatusCode.Should().Be(HttpStatusCode.OK, "이유: Admin 2명일 땐 강등 가능");

        // JWT 클레임은 아직 Admin — 서비스는 DB 기준으로 마지막 Admin 을 판정
        var demoteLast = await _client.SendAsync(Authed(HttpMethod.Put,
            $"/api/admin/users/{builtinAdminId}/role", token, new { Role = "User" }));

        demoteLast.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var db2 = _factory.CreateScopedDbContext();
        db2.Users.Single(u => u.Id == builtinAdminId).Role.Should().Be("Admin", "이유: 마지막 Admin 강등 차단");
    }

    [Fact]
    public async Task ChangeRole_invalid_role_returns_400()
    {
        await SeedUserAsync("boss2", "Admin");
        var targetId = await SeedUserAsync("worker2", "User");
        var token = await LoginAsync("boss2");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put,
            $"/api/admin/users/{targetId}/role", token, new { Role = "SuperRoot" }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangeRole_as_user_role_returns_403()
    {
        await SeedUserAsync("plain", "User");
        var targetId = await SeedUserAsync("worker3", "User");
        var token = await LoginAsync("plain");

        var resp = await _client.SendAsync(Authed(HttpMethod.Put,
            $"/api/admin/users/{targetId}/role", token, new { Role = "Guest" }));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ③ 검사 기준값 Guest 비노출 ──

    [Fact]
    public async Task Parameters_endpoints_forbidden_for_guest_allowed_for_user()
    {
        await SeedUserAsync("guesty", "Guest");
        await SeedUserAsync("usery", "User");

        var guestToken = await LoginAsync("guesty");
        var guestResp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/parameters/recipe/1", guestToken));
        guestResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "이유: 검사 기준값은 Guest 비노출");

        var userToken = await LoginAsync("usery");
        var userResp = await _client.SendAsync(Authed(HttpMethod.Get, "/api/parameters/recipe/1", userToken));
        userResp.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "이유: User 는 조회 가능");
    }
}
