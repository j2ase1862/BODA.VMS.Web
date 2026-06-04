using System;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// Option C2 (2026-06-04): admin 디폴트 시드 (admin/admin) 제거 후 Initial:AdminPassword
/// 환경변수 기반 시드. 미설정 + DB 에 admin 없으면 첫 가동 차단 (InvalidOperationException).
/// </summary>
public class InitialAdminSeedTests
{
    /// <summary>
    /// DbBacked (in-memory SQLite) — 빈 DB 라 시드 시점 도달.
    /// Initial:AdminPassword 미설정 → 부팅 차단 시나리오 검증.
    /// </summary>
    public sealed class NoInitialAdminPasswordFactory : DbBackedIntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            // 기본 baseConfig 의 Initial:AdminPassword 를 null 로 덮어쓰기 → 부팅 차단
            ["Initial:AdminPassword"] = null
        };
    }

    /// <summary>약한 비밀번호 (8자 미만) → 부팅 차단 시나리오 검증.</summary>
    public sealed class TooShortPasswordFactory : DbBackedIntegrationTestFactory
    {
        protected override Dictionary<string, string?> ExtraConfig => new()
        {
            ["Initial:AdminPassword"] = "short"
        };
    }

    [Fact]
    public void Bootstrap_without_Initial_AdminPassword_throws_InvalidOperationException()
    {
        // WebApplicationFactory.CreateClient 가 호스트 빌드 시점에 throw 캡처
        var act = () =>
        {
            using var factory = new NoInitialAdminPasswordFactory();
            using var client = factory.CreateClient();
        };

        act.Should().Throw<Exception>()
            .Where(ex => ex is InvalidOperationException
                         || ex.InnerException is InvalidOperationException
                         || ex.Message.Contains("Initial:AdminPassword")
                         || ex.Message.Contains("admin"));
    }

    [Fact]
    public void Bootstrap_with_too_short_password_throws_InvalidOperationException()
    {
        var act = () =>
        {
            using var factory = new TooShortPasswordFactory();
            using var client = factory.CreateClient();
        };

        act.Should().Throw<Exception>()
            .Where(ex => ex is InvalidOperationException
                         || ex.InnerException is InvalidOperationException
                         || ex.Message.Contains("8자")
                         || ex.Message.Contains("AdminPassword"));
    }

    [Fact]
    public void Bootstrap_with_valid_Initial_AdminPassword_succeeds()
    {
        // 기본 IntegrationTestFactory 가 "TestInitialAdminPassword_1234" 주입 — 정상 부팅
        using var factory = new IntegrationTestFactory();
        using var client = factory.CreateClient();

        var health = client.GetAsync("/health").Result;
        health.IsSuccessStatusCode.Should().BeTrue();
    }
}
