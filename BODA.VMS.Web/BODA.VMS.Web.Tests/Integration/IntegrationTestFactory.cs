using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// Endpoint integration 테스트용 공통 WebApplicationFactory base.
/// - Program.cs 시작 검증(Jwt:Key 32자+) 통과를 위해 in-memory dummy 키 주입
/// - 파생 클래스에서 Extra config 추가 가능 (ClientApiKey 등)
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    /// <summary>파생 클래스가 추가 설정 주입 (override 가능).</summary>
    protected virtual Dictionary<string, string?> ExtraConfig => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var baseConfig = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "TestSecretKey_AtLeast32CharsLong_ForIntegrationTesting_2026",
                ["Jwt:Issuer"] = "BODA.VMS.Web",
                ["Jwt:Audience"] = "BODA.VMS.Web.Client",
                ["Jwt:ExpireMinutes"] = "480"
            };
            // ExtraConfig 가 같은 키를 가지면 override
            foreach (var kv in ExtraConfig)
            {
                baseConfig[kv.Key] = kv.Value;
            }
            config.AddInMemoryCollection(baseConfig);
        });
    }
}
