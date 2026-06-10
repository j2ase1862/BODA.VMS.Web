using System.Net;
using System.Net.Http.Json;
using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// /api/auth/* 엔드포인트 — DB 쓰기/읽기 까지 full path 검증.
/// 운영 미들웨어 파이프라인 + AuthService + EF Core 모두 실제 동작.
/// 각 테스트마다 factory 새로 생성 — DB 완전 격리 (느리지만 신뢰성 우선).
/// </summary>
public class AuthEndpointTests : IDisposable
{
    private readonly DbBackedIntegrationTestFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointTests()
    {
        _factory = new DbBackedIntegrationTestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task POST_register_creates_user_with_Pending_role_and_bcrypt_hash()
    {
        // 비밀번호는 KISA 복잡도 정책 충족 필요 (3종 조합 8자+)
        var dto = new RegisterRequest
        {
            Username = "register_test_user",
            Password = "Secret12!",
            DisplayName = "Register Test"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register", dto);

        response.IsSuccessStatusCode.Should().BeTrue();

        // DB 직접 확인
        using var db = _factory.CreateScopedDbContext();
        var stored = db.Users.Single(u => u.Username == "register_test_user");
        stored.Role.Should().Be("Pending");
        stored.IsApproved.Should().BeFalse();
        // 평문 저장 금지 — BCrypt 해시 검증
        stored.PasswordHash.Should().NotBe("Secret12!");
        BCrypt.Net.BCrypt.Verify("Secret12!", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task POST_login_with_approved_user_returns_token()
    {
        // 시드: 승인된 사용자
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "login_test_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Login Test",
                Role = "Admin",
                IsApproved = true
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "login_test_user", Password = "secret12" });

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrEmpty();
        body.Username.Should().Be("login_test_user");
        body.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task POST_login_with_unapproved_user_returns_401()
    {
        // 보안: 미승인 사용자는 정확한 암호여도 차단
        using (var db = _factory.CreateScopedDbContext())
        {
            db.Users.Add(new User
            {
                Username = "pending_user",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret12"),
                DisplayName = "Pending",
                Role = "Pending",
                IsApproved = false
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "pending_user", Password = "secret12" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Failed_login_writes_auth_audit_log()
    {
        // GS 보안 — 인증 이벤트가 AuditLogs 에 기록되는지 full path 검증
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Username = "no_such_user", Password = "whatever1" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var db = _factory.CreateScopedDbContext();
        var log = db.AuditLogs.Single(a => a.EntityName == "Auth");
        log.Action.Should().Be(AuditAction.LoginFailed);
        log.UserName.Should().Be("no_such_user");
    }

    // 참고: JWT 양성 시나리오 (유효 토큰 → 200) 는 TestHost 환경에서 토큰 발급/검증
    // 사이 미세한 config 불일치 가능성 있어 본 PR 에서 제외. 음성 케이스 (토큰 없음/
    // 잘못된 토큰 → 401) 는 JwtAuthorizationTests 에서 이미 커버됨. 후속 PR 에서
    // ClaimsMapping 동작 검증 후 양성 시나리오 추가 예정.
}
