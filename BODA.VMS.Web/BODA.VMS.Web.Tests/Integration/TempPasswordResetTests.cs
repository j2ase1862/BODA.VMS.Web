using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Validators;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// 비밀번호 초기화 재설계 (2026-08-11):
/// ① Admin 초기화 → 서버 생성 임시 비밀번호 1회 반환 + MustChangePassword=true
/// ② 임시 비밀번호 로그인 → LoginResponse.MustChangePassword=true
/// ③ /api/auth/change-password — 현재 비밀번호 검증 / 복잡도 검증 / 성공 시 플래그 해제.
/// </summary>
public class TempPasswordResetTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory = new();
    private readonly HttpClient _client;

    public TempPasswordResetTests()
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

    private async Task<LoginResponse> LoginResponseAsync(string username, string password = "Secret12!@")
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = username, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private async Task<string> LoginAsync(string username, string password = "Secret12!@")
        => (await LoginResponseAsync(username, password)).Token;

    private HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<string> ResetPasswordAsync(int targetId, string adminToken)
    {
        var resp = await _client.SendAsync(Authed(HttpMethod.Post,
            $"/api/admin/users/{targetId}/reset-password", adminToken));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("tempPassword").GetString()!;
    }

    // ── ① 초기화: 임시 비밀번호 발급 ──

    [Fact]
    public async Task Reset_returns_temp_password_meeting_policy_and_sets_flag()
    {
        await SeedUserAsync("reset_admin", "Admin");
        var targetId = await SeedUserAsync("reset_target", "User");
        var token = await LoginAsync("reset_admin");

        var temp = await ResetPasswordAsync(targetId, token);

        temp.Should().HaveLength(12);
        PasswordPolicy.IsComplexEnough(temp).Should().BeTrue("이유: 임시 비밀번호도 KISA 복잡도 정책을 만족해야 함");

        using var db = _factory.CreateScopedDbContext();
        var user = db.Users.Single(u => u.Id == targetId);
        user.MustChangePassword.Should().BeTrue("이유: 임시 비밀번호는 다음 로그인 시 변경 강제");
        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull("이유: 초기화는 잠금 해제도 겸함");
        BCrypt.Net.BCrypt.Verify(temp, user.PasswordHash).Should().BeTrue("이유: 해시는 반환된 임시 비밀번호와 일치");
    }

    [Fact]
    public async Task Reset_unknown_user_returns_404()
    {
        await SeedUserAsync("reset_admin2", "Admin");
        var token = await LoginAsync("reset_admin2");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post,
            "/api/admin/users/999999/reset-password", token));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reset_as_non_admin_returns_403()
    {
        await SeedUserAsync("reset_plain", "User");
        var targetId = await SeedUserAsync("reset_target2", "User");
        var token = await LoginAsync("reset_plain");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post,
            $"/api/admin/users/{targetId}/reset-password", token));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── ② 임시 비밀번호 로그인 ──

    [Fact]
    public async Task Login_with_temp_password_succeeds_and_flags_must_change()
    {
        await SeedUserAsync("temp_admin", "Admin");
        var targetId = await SeedUserAsync("temp_user", "User");
        var adminToken = await LoginAsync("temp_admin");

        var temp = await ResetPasswordAsync(targetId, adminToken);

        var login = await LoginResponseAsync("temp_user", temp);
        login.MustChangePassword.Should().BeTrue("이유: 클라이언트가 /change-password 로 유도해야 함");

        // 기존 비밀번호로는 더 이상 로그인 불가
        var oldResp = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Username = "temp_user", Password = "Secret12!@" });
        oldResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── ③ change-password ──

    [Fact]
    public async Task ChangePassword_wrong_current_returns_400()
    {
        await SeedUserAsync("chg_user1", "User");
        var token = await LoginAsync("chg_user1");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/change-password", token,
            new { CurrentPassword = "WrongPass1!", NewPassword = "NewSecret34!@" }));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be("현재 비밀번호가 올바르지 않습니다");
    }

    [Fact]
    public async Task ChangePassword_weak_new_password_returns_400_validation_problem()
    {
        await SeedUserAsync("chg_user2", "User");
        var token = await LoginAsync("chg_user2");

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/change-password", token,
            new { CurrentPassword = "Secret12!@", NewPassword = "abcdefgh1" })); // 9자 2종 — 정책 미달

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("errors", out var errors).Should().BeTrue("이유: FluentValidation → ValidationProblemDetails");
        errors.TryGetProperty("NewPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_success_clears_flag_and_new_password_works()
    {
        await SeedUserAsync("chg_admin", "Admin");
        var targetId = await SeedUserAsync("chg_user3", "User");
        var adminToken = await LoginAsync("chg_admin");

        var temp = await ResetPasswordAsync(targetId, adminToken);
        var login = await LoginResponseAsync("chg_user3", temp);
        login.MustChangePassword.Should().BeTrue();

        var resp = await _client.SendAsync(Authed(HttpMethod.Post, "/api/auth/change-password", login.Token,
            new { CurrentPassword = temp, NewPassword = "BrandNew56!@" }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var db = _factory.CreateScopedDbContext())
        {
            var user = db.Users.Single(u => u.Id == targetId);
            user.MustChangePassword.Should().BeFalse("이유: 변경 완료 시 강제 플래그 해제");
            BCrypt.Net.BCrypt.Verify("BrandNew56!@", user.PasswordHash).Should().BeTrue();
        }

        var newLogin = await LoginResponseAsync("chg_user3", "BrandNew56!@");
        newLogin.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePassword_unauthenticated_returns_401()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/change-password",
            new { CurrentPassword = "Secret12!@", NewPassword = "NewSecret34!@" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
