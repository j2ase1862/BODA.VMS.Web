using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// GS 보안 — 계정 잠금(lockout) + 로그인 감사 로그 검증.
/// 정책: MaxFailedAttempts 연속 실패 → LockoutMinutes 동안 잠금 (정확한 암호도 차단),
/// 모든 인증 이벤트는 AuditLogs (EntityName="Auth") 에 기록.
/// </summary>
public class AuthServiceLockoutTests
{
    private static readonly AccountLockoutOptions ThreeStrikes = new()
    {
        MaxFailedAttempts = 3,
        LockoutMinutes = 15
    };

    private static User SeedUser(InMemorySqliteDbContext ctx, string password = "correct")
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

    [Fact]
    public async Task Consecutive_failures_reaching_threshold_locks_account()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedUser(ctx);
        var svc = AuthServiceTests.CreateService(ctx.Db, ThreeStrikes);

        for (var i = 0; i < 3; i++)
            (await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "wrong" }))
                .Should().BeNull();

        await ctx.Db.Entry(user).ReloadAsync();
        user.LockoutUntil.Should().NotBeNull();
        user.LockoutUntil!.Value.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        user.FailedLoginCount.Should().Be(0); // 잠금 시점에 리셋 — 만료 후 새로 카운트

        // 잠금 중에는 정확한 암호도 차단
        (await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "correct" }))
            .Should().BeNull();

        // 감사 로그: LoginFailed 2 + Lockout 1 + 잠금 중 시도 LoginFailed 1
        ctx.Db.AuditLogs.Count(a => a.EntityName == "Auth" && a.Action == AuditAction.Lockout)
            .Should().Be(1);
        ctx.Db.AuditLogs.Count(a => a.EntityName == "Auth" && a.Action == AuditAction.LoginFailed)
            .Should().Be(3);
    }

    [Fact]
    public async Task Success_resets_failed_count()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedUser(ctx);
        var svc = AuthServiceTests.CreateService(ctx.Db, ThreeStrikes);

        await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "wrong" });
        await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "wrong" });

        var result = await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "correct" });
        result.Should().NotBeNull();

        await ctx.Db.Entry(user).ReloadAsync();
        user.FailedLoginCount.Should().Be(0);
        user.LockoutUntil.Should().BeNull();

        ctx.Db.AuditLogs.Count(a => a.EntityName == "Auth" && a.Action == AuditAction.LoginSuccess)
            .Should().Be(1);
    }

    [Fact]
    public async Task Expired_lockout_allows_login_again()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedUser(ctx);
        user.LockoutUntil = DateTime.UtcNow.AddMinutes(-1); // 이미 만료
        ctx.Db.SaveChanges();

        var svc = AuthServiceTests.CreateService(ctx.Db, ThreeStrikes);
        var result = await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "correct" });

        result.Should().NotBeNull();
        await ctx.Db.Entry(user).ReloadAsync();
        user.LockoutUntil.Should().BeNull(); // 성공 시 잔여 잠금 흔적 정리
    }

    [Fact]
    public async Task Unapproved_user_with_valid_credentials_does_not_increment_failed_count()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedUser(ctx);
        user.IsApproved = false;
        ctx.Db.SaveChanges();

        var svc = AuthServiceTests.CreateService(ctx.Db, ThreeStrikes);
        (await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "correct" }))
            .Should().BeNull();

        await ctx.Db.Entry(user).ReloadAsync();
        user.FailedLoginCount.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_username_is_audited_without_user_id()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = AuthServiceTests.CreateService(ctx.Db, ThreeStrikes);

        await svc.LoginAsync(new LoginRequest { Username = "ghost", Password = "x" });

        var log = ctx.Db.AuditLogs.Single(a => a.EntityName == "Auth");
        log.Action.Should().Be(AuditAction.LoginFailed);
        log.UserId.Should().BeNull();
        log.UserName.Should().Be("ghost");
        log.IpAddress.Should().Be("127.0.0.1"); // StubCurrentUser 의 IP 가 기록됨
    }

    [Fact]
    public async Task MaxFailedAttempts_zero_disables_lockout()
    {
        using var ctx = new InMemorySqliteDbContext();
        var user = SeedUser(ctx);
        var svc = AuthServiceTests.CreateService(ctx.Db,
            new AccountLockoutOptions { MaxFailedAttempts = 0, LockoutMinutes = 15 });

        for (var i = 0; i < 10; i++)
            await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "wrong" });

        await ctx.Db.Entry(user).ReloadAsync();
        user.LockoutUntil.Should().BeNull();

        // 잠금 비활성이어도 정확한 암호는 즉시 통과
        (await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "correct" }))
            .Should().NotBeNull();
    }
}
