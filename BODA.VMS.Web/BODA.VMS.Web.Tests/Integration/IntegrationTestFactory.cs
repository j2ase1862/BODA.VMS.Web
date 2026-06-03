using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BODA.VMS.Web.Tests.Integration;

/// <summary>
/// Endpoint integration 테스트용 공통 WebApplicationFactory base.
/// - Program.cs 시작 검증(Jwt:Key 32자+) 통과를 위해 in-memory dummy 키 주입
/// - JwtBearerOptions.TokenValidationParameters 의 IssuerSigningKey 를 시작 후 재설정
///   (Program.cs 가 builder.Configuration 에서 키를 캡처하는 시점과 WebApplicationFactory
///   의 ConfigureAppConfiguration 적용 시점 차이로 키 불일치 발생 — Configure&lt;JwtBearerOptions&gt;
///   는 post-build 시점에 적용되므로 테스트용 키로 확실히 덮어쓴다)
/// - 파생 클래스에서 Extra config 추가 가능 (ClientApiKey 등)
/// </summary>
public class IntegrationTestFactory : WebApplicationFactory<Program>
{
    internal const string TestJwtKey = "TestSecretKey_AtLeast32CharsLong_ForIntegrationTesting_2026";
    internal const string TestJwtIssuer = "BODA.VMS.Web";
    internal const string TestJwtAudience = "BODA.VMS.Web.Client";

    /// <summary>파생 클래스가 추가 설정 주입 (override 가능).</summary>
    protected virtual Dictionary<string, string?> ExtraConfig => new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var baseConfig = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = TestJwtIssuer,
                ["Jwt:Audience"] = TestJwtAudience,
                ["Jwt:ExpireMinutes"] = "480"
            };
            // ExtraConfig 가 같은 키를 가지면 override
            foreach (var kv in ExtraConfig)
            {
                baseConfig[kv.Key] = kv.Value;
            }
            config.AddInMemoryCollection(baseConfig);
        });

        builder.ConfigureServices(services =>
        {
            // Configure<JwtBearerOptions> 는 post-Configure 단계에서 호출되므로 Program.cs 의
            // AddJwtBearer 가 캡처한 IssuerSigningKey 를 테스트 키로 강제 교체.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
                opts.TokenValidationParameters.ValidIssuer = TestJwtIssuer;
                opts.TokenValidationParameters.ValidAudience = TestJwtAudience;
            });
        });
    }
}
