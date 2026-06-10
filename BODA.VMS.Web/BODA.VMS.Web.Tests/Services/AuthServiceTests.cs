using BODA.VMS.Web.Client.Models;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Services;
using BODA.VMS.Web.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BODA.VMS.Web.Tests.Services;

public class AuthServiceTests
{
    private static JwtTokenService CreateJwtService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKey_AtLeast32CharsLong_ForTesting_2026",
                ["Jwt:Issuer"] = "BODA.VMS.Web.Tests",
                ["Jwt:Audience"] = "Tests",
                ["Jwt:ExpireMinutes"] = "60"
            })
            .Build();
        return new JwtTokenService(config);
    }

    internal static AuthService CreateService(
        BodaVmsDbContext db,
        AccountLockoutOptions? lockout = null,
        RefreshTokenOptions? refresh = null)
        => new(db, CreateJwtService(), new StubCurrentUser(),
               Options.Create(lockout ?? new AccountLockoutOptions()),
               Options.Create(refresh ?? new RefreshTokenOptions()));

    internal sealed class StubCurrentUser : ICurrentUserService
    {
        public int? UserId => null;
        public string? UserName => null;
        public string? IpAddress => "127.0.0.1";
    }

    [Fact]
    public async Task Login_with_invalid_username_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = CreateService(ctx.Db);

        var result = await svc.LoginAsync(new LoginRequest { Username = "nobody", Password = "x" });
        result.Should().BeNull();
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_null()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Users.Add(new User
        {
            Username = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct"),
            DisplayName = "Alice",
            Role = "User",
            IsApproved = true
        });
        await ctx.Db.SaveChangesAsync();

        var svc = CreateService(ctx.Db);
        var result = await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "wrong" });
        result.Should().BeNull();
    }

    [Fact]
    public async Task Login_with_unapproved_user_returns_null()
    {
        // 보안 핵심: 미승인 계정은 정확한 암호여도 로그인 차단
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Users.Add(new User
        {
            Username = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret"),
            DisplayName = "Alice",
            Role = "Pending",
            IsApproved = false
        });
        await ctx.Db.SaveChangesAsync();

        var svc = CreateService(ctx.Db);
        var result = await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "secret" });
        result.Should().BeNull();
    }

    [Fact]
    public async Task Login_with_approved_user_returns_token()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Users.Add(new User
        {
            Username = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret"),
            DisplayName = "Alice Lee",
            Role = "Admin",
            IsApproved = true
        });
        await ctx.Db.SaveChangesAsync();

        var svc = CreateService(ctx.Db);
        var result = await svc.LoginAsync(new LoginRequest { Username = "alice", Password = "secret" });

        result.Should().NotBeNull();
        result!.Token.Should().NotBeNullOrEmpty();
        result.Username.Should().Be("alice");
        result.DisplayName.Should().Be("Alice Lee");
        result.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Register_new_user_persists_with_Pending_role_and_unapproved()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = CreateService(ctx.Db);

        var (success, _) = await svc.RegisterAsync(new RegisterRequest
        {
            Username = "newbie",
            Password = "secret12",
            DisplayName = "Newbie"
        });

        success.Should().BeTrue();
        var stored = ctx.Db.Users.Single(u => u.Username == "newbie");
        stored.Role.Should().Be("Pending");
        stored.IsApproved.Should().BeFalse();
        // 비밀번호는 절대 평문 저장하지 않음
        stored.PasswordHash.Should().NotBe("secret12");
        BCrypt.Net.BCrypt.Verify("secret12", stored.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public async Task Register_duplicate_username_returns_failure()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Users.Add(new User
        {
            Username = "alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("x"),
            DisplayName = "Alice",
            Role = "User",
            IsApproved = true
        });
        await ctx.Db.SaveChangesAsync();

        var svc = CreateService(ctx.Db);
        var (success, message) = await svc.RegisterAsync(new RegisterRequest
        {
            Username = "alice",
            Password = "secret12",
            DisplayName = "Other Alice"
        });

        success.Should().BeFalse();
        message.Should().Contain("exists");
    }

    [Fact]
    public async Task GetUserByIdAsync_returns_null_when_not_found()
    {
        using var ctx = new InMemorySqliteDbContext();
        var svc = CreateService(ctx.Db);
        (await svc.GetUserByIdAsync(999)).Should().BeNull();
    }
}
