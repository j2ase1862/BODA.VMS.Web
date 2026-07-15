using BODA.VMS.Web.Data.Entities;
using BODA.VMS.Web.Setup;
using BODA.VMS.Web.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BODA.VMS.Web.Tests.Services;

/// <summary>
/// AdminAccountBootstrap — 부팅 시 admin 시드 / 강제 리셋(ForceAdminPasswordReset) /
/// 시드값-현재 비밀번호 불일치 진단 경고. 현장 401 사건(2026-07-15) 재발 방지.
/// </summary>
public class AdminAccountBootstrapTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    private static User AddAdmin(InMemorySqliteDbContext ctx, string password,
        string username = "admin", int failedCount = 0, DateTime? lockoutUntil = null)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            DisplayName = "Administrator",
            Role = "Admin",
            IsApproved = true,
            FailedLoginCount = failedCount,
            LockoutUntil = lockoutUntil,
        };
        ctx.Db.Users.Add(user);
        ctx.Db.SaveChanges();
        return user;
    }

    // ───────────────────────── 신규 시드 (기존 동작 보존) ─────────────────────────

    [Fact]
    public async Task FreshDb_SeedsAdmin_WithConfiguredPassword()
    {
        using var ctx = new InMemorySqliteDbContext();
        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db, Config(("Initial:AdminPassword", "seed-pw-123")), NullLogger.Instance);

        Assert.Equal(AdminBootstrapResult.Seeded, result);
        var admin = await ctx.Db.Users.SingleAsync(u => u.Username == "admin");
        Assert.True(BCrypt.Net.BCrypt.Verify("seed-pw-123", admin.PasswordHash));
        Assert.Equal("Admin", admin.Role);
        Assert.True(admin.IsApproved);
    }

    [Fact]
    public async Task FreshDb_CustomUsername_SeedsThatUsername()
    {
        using var ctx = new InMemorySqliteDbContext();
        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db,
            Config(("Initial:AdminUsername", "admin2"), ("Initial:AdminPassword", "seed-pw-123")),
            NullLogger.Instance);

        Assert.Equal(AdminBootstrapResult.Seeded, result);
        Assert.NotNull(await ctx.Db.Users.SingleOrDefaultAsync(u => u.Username == "admin2"));
    }

    [Fact]
    public async Task FreshDb_NoPassword_ThrowsWithGuidance()
    {
        using var ctx = new InMemorySqliteDbContext();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminAccountBootstrap.EnsureAdminAsync(ctx.Db, Config(), NullLogger.Instance));
        Assert.Contains("Initial__AdminPassword", ex.Message);
    }

    [Fact]
    public async Task FreshDb_ShortPassword_Throws()
    {
        using var ctx = new InMemorySqliteDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminAccountBootstrap.EnsureAdminAsync(
                ctx.Db, Config(("Initial:AdminPassword", "short12")), NullLogger.Instance));
    }

    // ───────────────────────── 기존 admin — 시드값 무시 + 진단 ─────────────────────────

    [Fact]
    public async Task Existing_NoInitialConfig_NoChange()
    {
        using var ctx = new InMemorySqliteDbContext();
        var admin = AddAdmin(ctx, "current-pw-1");
        var originalHash = admin.PasswordHash;

        var result = await AdminAccountBootstrap.EnsureAdminAsync(ctx.Db, Config(), NullLogger.Instance);

        Assert.Equal(AdminBootstrapResult.SkippedExisting, result);
        Assert.Equal(originalHash, (await ctx.Db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Existing_ConfigMatchesCurrentPassword_NoWarning()
    {
        using var ctx = new InMemorySqliteDbContext();
        AddAdmin(ctx, "current-pw-1");
        var logger = new CapturingLogger();

        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db, Config(("Initial:AdminPassword", "current-pw-1")), logger);

        Assert.Equal(AdminBootstrapResult.SkippedExisting, result);
        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Existing_ConfigMismatch_WarnsWithRecoveryPath_AndDoesNotChangePassword()
    {
        using var ctx = new InMemorySqliteDbContext();
        var admin = AddAdmin(ctx, "current-pw-1");
        var originalHash = admin.PasswordHash;
        var logger = new CapturingLogger();

        // 현장 401 사건 시나리오: appsettings 의 시드값을 바꿨지만 admin 은 이미 존재
        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db, Config(("Initial:AdminPassword", "different-pw-9")), logger);

        Assert.Equal(AdminBootstrapResult.MismatchWarned, result);
        Assert.Equal(originalHash, (await ctx.Db.Users.SingleAsync()).PasswordHash);
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("무시", warning.Message);
        Assert.Contains("ForceAdminPasswordReset", warning.Message);
    }

    // ───────────────────────── 강제 리셋 플래그 ─────────────────────────

    [Fact]
    public async Task ForceReset_UpdatesPassword_ClearsLockout_WritesAudit()
    {
        using var ctx = new InMemorySqliteDbContext();
        AddAdmin(ctx, "forgotten-pw-1",
            failedCount: 4, lockoutUntil: DateTime.UtcNow.AddMinutes(10));
        var logger = new CapturingLogger();

        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db,
            Config(("Initial:AdminPassword", "new-pw-12345"),
                   ("Initial:ForceAdminPasswordReset", "true")),
            logger);

        Assert.Equal(AdminBootstrapResult.ResetApplied, result);
        var admin = await ctx.Db.Users.SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify("new-pw-12345", admin.PasswordHash));
        Assert.Equal(0, admin.FailedLoginCount);
        Assert.Null(admin.LockoutUntil);
        Assert.True(admin.IsApproved);

        var audit = Assert.Single(ctx.Db.AuditLogs.Where(a => a.Action == "AdminPasswordReset"));
        Assert.Equal("User", audit.EntityName);
        Assert.Equal("admin", audit.UserName);

        // 플래그 제거 안내 경고 필수 — 켜둔 채 두면 매 부팅 재적용
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("제거"));
    }

    [Fact]
    public async Task ForceReset_FlagFalse_TreatedAsNormalBoot()
    {
        using var ctx = new InMemorySqliteDbContext();
        var admin = AddAdmin(ctx, "current-pw-1");
        var originalHash = admin.PasswordHash;

        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db,
            Config(("Initial:AdminPassword", "current-pw-1"),
                   ("Initial:ForceAdminPasswordReset", "false")),
            NullLogger.Instance);

        Assert.Equal(AdminBootstrapResult.SkippedExisting, result);
        Assert.Equal(originalHash, (await ctx.Db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task ForceReset_WithoutPassword_ThrowsWithGuidance()
    {
        using var ctx = new InMemorySqliteDbContext();
        AddAdmin(ctx, "current-pw-1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminAccountBootstrap.EnsureAdminAsync(
                ctx.Db, Config(("Initial:ForceAdminPasswordReset", "true")), NullLogger.Instance));
        Assert.Contains("Initial:AdminPassword", ex.Message);
    }

    [Fact]
    public async Task ForceReset_ShortPassword_Throws()
    {
        using var ctx = new InMemorySqliteDbContext();
        AddAdmin(ctx, "current-pw-1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AdminAccountBootstrap.EnsureAdminAsync(
                ctx.Db,
                Config(("Initial:AdminPassword", "short12"),
                       ("Initial:ForceAdminPasswordReset", "true")),
                NullLogger.Instance));
    }

    [Fact]
    public async Task ForceReset_CustomUsername_ResetsThatAccount()
    {
        using var ctx = new InMemorySqliteDbContext();
        AddAdmin(ctx, "other-pw-111", username: "admin2");

        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db,
            Config(("Initial:AdminUsername", "admin2"),
                   ("Initial:AdminPassword", "new-pw-12345"),
                   ("Initial:ForceAdminPasswordReset", "true")),
            NullLogger.Instance);

        Assert.Equal(AdminBootstrapResult.ResetApplied, result);
        var admin2 = await ctx.Db.Users.SingleAsync(u => u.Username == "admin2");
        Assert.True(BCrypt.Net.BCrypt.Verify("new-pw-12345", admin2.PasswordHash));
    }

    // 손상된 해시(BCrypt 형식 아님)는 예외 없이 "불일치 경고" 로 처리
    [Fact]
    public async Task Existing_CorruptedHash_WarnsInsteadOfThrowing()
    {
        using var ctx = new InMemorySqliteDbContext();
        ctx.Db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = "not-a-bcrypt-hash",
            DisplayName = "Administrator",
            Role = "Admin",
            IsApproved = true,
        });
        ctx.Db.SaveChanges();

        var result = await AdminAccountBootstrap.EnsureAdminAsync(
            ctx.Db, Config(("Initial:AdminPassword", "whatever-123")), new CapturingLogger());

        Assert.Equal(AdminBootstrapResult.MismatchWarned, result);
    }
}
