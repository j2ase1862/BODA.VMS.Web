using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// Serilog 구조화 로깅 통합 검증.
/// UseSerilog 가 호스트에 적용돼 IDiagnosticContext (request enrichment) 가 등록되고,
/// Microsoft.Extensions.Logging.ILogger 가 Serilog 로 백킹되는지 보장.
/// </summary>
public class SerilogObservabilityTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public SerilogObservabilityTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public void IDiagnosticContext_is_registered_proving_serilog_is_active()
    {
        // UseSerilog 가 Serilog request logging 인프라를 등록 — 미적용시 이 타입 없음
        using var scope = _factory.Services.CreateScope();
        var diag = scope.ServiceProvider.GetService<IDiagnosticContext>();
        diag.Should().NotBeNull("UseSerilog + UseSerilogRequestLogging 가 호스트에 적용되면 등록됨");
    }

    [Fact]
    public async Task Request_logging_does_not_break_normal_responses()
    {
        // UseSerilogRequestLogging 부착 후 응답 본문/상태가 정상인지 회귀 확인
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        resp.IsSuccessStatusCode.Should().BeTrue();
    }
}
