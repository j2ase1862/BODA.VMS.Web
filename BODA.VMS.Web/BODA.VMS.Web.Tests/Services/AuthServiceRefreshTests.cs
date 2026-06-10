using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// GS 보안 — JWT refresh token 발급 / 로테이션 / 폐기(revocation) 검증.
/// access token 만료 후 재로그인 없이 갱신하되, 토큰 탈취 대비 1 회용 로테이션 +
/// 서버측 폐기를 보장. 모든 갱신/폐기는 AuditLogs(EntityName="Auth")에 기록.
/// </summary>
public class AuthServiceRefreshTests
{
    private static readonly RefreshTokenOptions SevenDays = new() { ExpireDays = 7 };

    private static User SeedApprovedUser(InMemorySqliteDbContext ctx, string password = "correct")
    {
        var user = new User
        {
            Username = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = "Alice",
            Role = "User",
            IsApproved = true
        };
        ctx.Db.Users.Add(user);
        ctx.Db.SaveChanges();
        return user;
    }

    private static AuthService Service(InMemorySqliteDbContext ctx, RefreshTokenOptions? refresh = null)
        => AuthServiceTests.CreateService(ctx.Db, refresh: refresh ?? SevenDays);

    private static LoginRequest Creds => new() { Username = "alice", Password = "correct" };

    [Fact]
    public async Task Login_issues_refresh_token_and_access_expiry()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);

        login.Should().NotBeNull();
        login!.RefreshToken.Should().NotBeNullOrWhiteSpace();
        login.AccessTokenExpiresAt.Should().BeAfter(DateTime.UtcNow);

        ctx.Db.RefreshTokens.Count(t => t.RevokedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task Refresh_with_valid_token_rotates_and_returns_new_pair()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);
        var refreshed = await svc.RefreshAsync(login!.RefreshToken);

        refreshed.Should().NotBeNull();
        refreshed!.Token.Should().NotBeNullOrWhiteSpace();
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken, "로테이션 — 새 refresh token 발급");

        // 기존 토큰은 폐기 + 대체 해시 연결, 새 토큰만 활성
        var oldHash = RefreshTokenHasher.Hash(login.RefreshToken);
        var old = ctx.Db.RefreshTokens.Single(t => t.TokenHash == oldHash);
        old.RevokedAt.Should().NotBeNull();
        old.ReplacedByTokenHash.Should().Be(RefreshTokenHasher.Hash(refreshed.RefreshToken));
        ctx.Db.RefreshTokens.Count(t => t.RevokedAt == null).Should().Be(1);

        ctx.Db.AuditLogs.Count(a => a.EntityName == "Auth" && a.Action == AuditAction.TokenRefresh)
            .Should().Be(1);
    }

    [Fact]
    public async Task Rotated_old_token_is_rejected_on_reuse()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);
        await svc.RefreshAsync(login!.RefreshToken); // 로테이션 → 원본 폐기

        // 폐기된 원본 재사용 → 거부 (탈취 흔적)
        (await svc.RefreshAsync(login.RefreshToken)).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_with_unknown_token_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        (await svc.RefreshAsync("totally-bogus-token")).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedApprovedUser(ctx);
        var svc = Service(ctx);

        // 만료된 토큰 직접 삽입
        var raw = RefreshTokenHasher.Generate();
        ctx.Db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = RefreshTokenHasher.Hash(raw),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-8)
        });
        ctx.Db.SaveChanges();

        (await svc.RefreshAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task Refresh_for_unapproved_user_returns_null_and_revokes()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);

        // 로그인 후 승인 철회 (예: 관리자 비활성화)
        user.IsApproved = false;
        ctx.Db.SaveChanges();

        (await svc.RefreshAsync(login!.RefreshToken)).Should().BeNull();

        var hash = RefreshTokenHasher.Hash(login.RefreshToken);
        ctx.Db.RefreshTokens.Single(t => t.TokenHash == hash).RevokedAt
            .Should().NotBeNull("부적격 사용자의 토큰은 폐기");
    }

    [Fact]
    public async Task Refresh_for_locked_user_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);

        user.LockoutUntil = DateTime.UtcNow.AddMinutes(15);
        ctx.Db.SaveChanges();

        (await svc.RefreshAsync(login!.RefreshToken)).Should().BeNull();
    }

    [Fact]
    public async Task Revoke_then_refresh_fails_and_audits_logout()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var login = await svc.LoginAsync(Creds);
        await svc.RevokeRefreshTokenAsync(login!.RefreshToken);

        (await svc.RefreshAsync(login.RefreshToken)).Should().BeNull("폐기된 토큰은 갱신 불가");

        ctx.Db.AuditLogs.Count(a => a.EntityName == "Auth" && a.Action == AuditAction.Logout)
            .Should().Be(1);
    }

    [Fact]
    public async Task Revoke_unknown_token_is_noop()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx);

        var act = async () => await svc.RevokeRefreshTokenAsync("nope");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExpireDays_zero_disables_refresh_issuance()
    {
        using var ctx = new InMemorySqliteDbContext();
        SeedApprovedUser(ctx);
        var svc = Service(ctx, new RefreshTokenOptions { ExpireDays = 0 });

        var login = await svc.LoginAsync(Creds);

        login!.RefreshToken.Should().BeEmpty("ExpireDays<=0 이면 refresh token 미발급");
        ctx.Db.RefreshTokens.Should().BeEmpty();
    }
}
